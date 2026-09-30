using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.IO.Tess;

internal static class ScoreMapper
{
    public static ScoreDto ToDto(Score score)
    {
        List<InstrumentDto> instruments = [];
        foreach (Instrument instrument in score.Instruments)
        {
            List<StaffDto> staves = [];
            foreach (Staff staff in instrument.Staves)
            {
                staves.Add(new StaffDto(staff.Name, (int)staff.InitialClef));
            }

            instruments.Add(new InstrumentDto(instrument.Name, staves));
        }

        List<MeasureDto> measures = [];
        foreach (Measure measure in score.Measures)
        {
            measures.Add(new MeasureDto(measure.Number, measure.TimeSignature.Numerator,
                measure.TimeSignature.Denominator, measure.KeySignature.Fifths));
        }

        List<StaffMeasureKey> keys = [.. score.Content.Keys];
        keys.Sort((a, b) => a.StaffIndex != b.StaffIndex
            ? a.StaffIndex.CompareTo(b.StaffIndex) : a.MeasureIndex.CompareTo(b.MeasureIndex));
        List<StaffMeasureDto> content = [];
        foreach (StaffMeasureKey key in keys)
        {
            List<VoiceDto> voices = [];
            foreach (Voice voice in score.Content[key].Voices)
            {
                List<EventDto> events = [.. voice.Events.Select(ToEventDto)];
                voices.Add(new VoiceDto(voice.Number, events));
            }

            content.Add(new StaffMeasureDto(key.StaffIndex, key.MeasureIndex, voices));
        }

        List<AttachmentDto> attachments = [];
        foreach (Attachment attachment in score.AttachmentList)
        {
            attachments.Add(attachment switch
            {
                ArticulationAttachment a => new AttachmentDto("articulation", a.Target.Value, (int)a.Kind),
                DynamicAttachment d => new AttachmentDto("dynamic", d.Target.Value, (int)d.Level),
                TempoAttachment t => new AttachmentDto("tempo", t.Target.Value, (int)t.Beat.Value, t.Beat.Dots, t.Bpm),
                TextAttachment x => new AttachmentDto("text", x.Target.Value, 0, Text: x.Text),
                ChordSymbolAttachment c => new AttachmentDto("chord", c.Target.Value, (int)c.Root, c.RootAlter, Text: c.Quality,
                    Value3: c.BassAlter, Value4: c.Bass is Step bass ? (int)bass : -1),
                _ => throw new InvalidOperationException($"Attachment {attachment.GetType().Name} cannot be saved."),
            });
        }

        return new ScoreDto(score.Metadata.Title, score.Metadata.Composer, instruments, measures, content,
            attachments.Count == 0 ? null : attachments);
    }

    private static EventDto ToEventDto(MusicEvent musicEvent)
    {
        if (musicEvent is TupletGroup group)
        {
            return new EventDto("tuplet", group.Id.Value, group.Onset.Num, group.Onset.Den, (int)group.Duration.Value,
                group.Duration.Dots, 0, [], group.Actual, group.Normal, [.. group.Children.Select(ToEventDto)]);
        }

        List<NoteDto> notes = [];
        string kind = "rest";
        int stem = 0;
        if (musicEvent is Chord chord)
        {
            kind = "chord";
            stem = (int)chord.Stem;
            foreach (Note note in chord.Notes)
            {
                notes.Add(new NoteDto((int)note.Pitch.Step, note.Pitch.Alter, note.Pitch.Octave, note.TiedToNext));
            }
        }

        return new EventDto(kind, musicEvent.Id.Value, musicEvent.Onset.Num, musicEvent.Onset.Den,
            (int)musicEvent.Duration.Value, musicEvent.Duration.Dots, stem, notes);
    }

    private static MusicEvent FromEventDto(EventDto e)
    {
        EventId id = new(e.Id);
        Fraction onset = new(e.OnsetNum, e.OnsetDen);
        Duration duration = new((NoteValue)e.Value, e.Dots);
        switch (e.Kind)
        {
            case "rest":
                return new Rest(id, onset, duration);
            case "tuplet":
                return new TupletGroup(id, onset, duration, e.Actual, e.Normal,
                    [.. (e.Children ?? []).Select(FromEventDto)]);
            case "chord":
                ImmutableArray<Note>.Builder notes = ImmutableArray.CreateBuilder<Note>();
                foreach (NoteDto note in e.Notes)
                {
                    notes.Add(new Note(new Pitch((Step)note.Step, note.Alter, note.Octave), note.Tied));
                }

                return new Chord(id, onset, duration, notes.ToImmutable(), (StemDirection)e.Stem);
            default:
                throw new InvalidDataException($"Unknown event kind '{e.Kind}'.");
        }
    }

    public static Score FromDto(ScoreDto dto)
    {
        ImmutableArray<Instrument>.Builder instruments = ImmutableArray.CreateBuilder<Instrument>();
        foreach (InstrumentDto instrument in dto.Instruments)
        {
            ImmutableArray<Staff>.Builder staves = ImmutableArray.CreateBuilder<Staff>();
            foreach (StaffDto staff in instrument.Staves)
            {
                staves.Add(new Staff(staff.Name, (Clef)staff.Clef));
            }

            instruments.Add(new Instrument(instrument.Name, staves.ToImmutable()));
        }

        ImmutableArray<Measure>.Builder measures = ImmutableArray.CreateBuilder<Measure>();
        foreach (MeasureDto measure in dto.Measures)
        {
            measures.Add(new Measure(measure.Number,
                new TimeSignature(measure.Numerator, measure.Denominator),
                new KeySignature(measure.Fifths)));
        }

        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        foreach (StaffMeasureDto entry in dto.Content)
        {
            ImmutableArray<Voice>.Builder voices = ImmutableArray.CreateBuilder<Voice>();
            foreach (VoiceDto voice in entry.Voices)
            {
                ImmutableArray<MusicEvent> events = [.. voice.Events.Select(FromEventDto)];
                voices.Add(new Voice(voice.Number, events));
            }

            content[new StaffMeasureKey(entry.Staff, entry.Measure)] = new StaffMeasure(voices.ToImmutable());
        }

        ImmutableArray<Attachment> attachments = [.. (dto.Attachments ?? []).Select(a => a.Kind switch
        {
            "articulation" => (Attachment)new ArticulationAttachment(new EventId(a.Target), (ArticulationKind)a.Value),
            "dynamic" => new DynamicAttachment(new EventId(a.Target), (DynamicLevel)a.Value),
            "tempo" => new TempoAttachment(new EventId(a.Target), new Duration((NoteValue)a.Value, a.Value2), a.Number),
            "text" => new TextAttachment(new EventId(a.Target), a.Text ?? ""),
            "chord" => new ChordSymbolAttachment(new EventId(a.Target), (Step)a.Value, a.Value2, a.Text ?? "",
                a.Value4 >= 0 ? (Step)a.Value4 : null, a.Value3),
            _ => throw new InvalidDataException($"Unknown attachment kind '{a.Kind}'."),
        })];
        return new Score(new ScoreMetadata(dto.Title, dto.Composer), instruments.ToImmutable(),
            measures.ToImmutable(), content.ToImmutable(), attachments.IsEmpty ? default : attachments);
    }
}
