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
                List<EventDto> events = [];
                foreach (MusicEvent musicEvent in voice.Events)
                {
                    List<NoteDto> notes = [];
                    string kind = "rest";
                    int stem = 0;
                    if (musicEvent is Chord chord)
                    {
                        kind = "chord";
                        stem = (int)chord.Stem;
                        foreach (Note note in chord.Notes)
                        {
                            notes.Add(new NoteDto((int)note.Pitch.Step, note.Pitch.Alter,
                                note.Pitch.Octave, note.TiedToNext));
                        }
                    }

                    events.Add(new EventDto(kind, musicEvent.Id.Value, musicEvent.Onset.Num,
                        musicEvent.Onset.Den, (int)musicEvent.Duration.Value, musicEvent.Duration.Dots,
                        stem, notes));
                }

                voices.Add(new VoiceDto(voice.Number, events));
            }

            content.Add(new StaffMeasureDto(key.StaffIndex, key.MeasureIndex, voices));
        }

        return new ScoreDto(score.Metadata.Title, score.Metadata.Composer, instruments, measures, content);
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
                ImmutableArray<MusicEvent>.Builder events = ImmutableArray.CreateBuilder<MusicEvent>();
                foreach (EventDto e in voice.Events)
                {
                    EventId id = new(e.Id);
                    Fraction onset = new(e.OnsetNum, e.OnsetDen);
                    Duration duration = new((NoteValue)e.Value, e.Dots);
                    if (e.Kind == "rest")
                    {
                        events.Add(new Rest(id, onset, duration));
                        continue;
                    }

                    if (e.Kind != "chord")
                    {
                        throw new InvalidDataException($"Unknown event kind '{e.Kind}'.");
                    }

                    ImmutableArray<Note>.Builder notes = ImmutableArray.CreateBuilder<Note>();
                    foreach (NoteDto note in e.Notes)
                    {
                        notes.Add(new Note(new Pitch((Step)note.Step, note.Alter, note.Octave), note.Tied));
                    }

                    events.Add(new Chord(id, onset, duration, notes.ToImmutable(), (StemDirection)e.Stem));
                }

                voices.Add(new Voice(voice.Number, events.ToImmutable()));
            }

            content[new StaffMeasureKey(entry.Staff, entry.Measure)] = new StaffMeasure(voices.ToImmutable());
        }

        return new Score(new ScoreMetadata(dto.Title, dto.Composer), instruments.ToImmutable(),
            measures.ToImmutable(), content.ToImmutable());
    }
}
