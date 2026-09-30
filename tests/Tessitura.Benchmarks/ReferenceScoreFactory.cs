using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Smufl;

namespace Tessitura.Benchmarks;

internal static class ReferenceScoreFactory
{
    public const int StaffCount = 30;
    public const int MeasureCount = 300;
    public const int ChangedMeasure = MeasureCount / 2;

    public static Score Create(bool advancedNotation = false)
    {
        ImmutableArray<Instrument>.Builder instruments = ImmutableArray.CreateBuilder<Instrument>(StaffCount);
        for (int staffIndex = 0; staffIndex < StaffCount; staffIndex++)
        {
            instruments.Add(new Instrument($"Instrument {staffIndex + 1}",
                [new Staff($"Staff {staffIndex + 1}")]));
        }

        ImmutableArray<Measure>.Builder measures = ImmutableArray.CreateBuilder<Measure>(MeasureCount);
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        ImmutableArray<Attachment>.Builder attachments = ImmutableArray.CreateBuilder<Attachment>();
        ImmutableArray<Spanner>.Builder spanners = ImmutableArray.CreateBuilder<Spanner>();
        Duration quarter = new(NoteValue.Quarter, 0);
        for (int measureIndex = 0; measureIndex < MeasureCount; measureIndex++)
        {
            measures.Add(new Measure(measureIndex + 1, new TimeSignature(4, 4)));
            for (int staffIndex = 0; staffIndex < StaffCount; staffIndex++)
            {
                ImmutableArray<MusicEvent>.Builder events = ImmutableArray.CreateBuilder<MusicEvent>(4);
                EventId firstEvent = default;
                EventId lastEvent = default;
                for (int beat = 0; beat < 4; beat++)
                {
                    EventId eventId = new(Guid.NewGuid());
                    firstEvent = beat == 0 ? eventId : firstEvent;
                    lastEvent = eventId;
                    events.Add(new Chord(eventId, new Fraction(beat, 4), quarter,
                        [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto));

                    if (advancedNotation)
                    {
                        if (beat == 0 && measureIndex % 12 == 0)
                        {
                            attachments.Add(new DynamicAttachment(eventId,
                                measureIndex % 24 == 0 ? DynamicLevel.F : DynamicLevel.Mp));
                        }

                        if (beat == 0 && measureIndex % 4 == 0)
                        {
                            attachments.Add(new ArticulationAttachment(eventId, ArticulationKind.Staccato));
                        }

                        if (staffIndex == 0 && beat == 0)
                        {
                            if (measureIndex % 4 == 0)
                            {
                                attachments.Add(new ChordSymbolAttachment(eventId, Step.C, 0, "maj7"));
                                attachments.Add(new LyricAttachment(eventId, 1, "la"));
                            }

                            if (measureIndex % 12 == 0)
                            {
                                attachments.Add(new TempoAttachment(eventId, quarter, 112));
                            }

                            if (measureIndex % 24 == 0)
                            {
                                attachments.Add(new TextAttachment(eventId, "dolce"));
                            }
                        }
                    }
                }

                content.Add(new StaffMeasureKey(staffIndex, measureIndex),
                    new StaffMeasure([new Voice(1, events.MoveToImmutable())]));
                if (advancedNotation && staffIndex == 0 && measureIndex % 12 == 0)
                {
                    spanners.Add(new Spanner(firstEvent, lastEvent, SpannerKind.Crescendo));
                }
            }
        }

        return new Score(new ScoreMetadata("Tessitura Performance Reference", "Tessitura"),
            instruments.MoveToImmutable(), measures.MoveToImmutable(), content.ToImmutable(),
            attachments.ToImmutable(), spanners.ToImmutable());
    }

    public static Score WithAccidental(Score score, bool sharp)
    {
        StaffMeasureKey key = new(0, ChangedMeasure);
        StaffMeasure staffMeasure = score.Content[key];
        Voice voice = staffMeasure.Voices[0];
        Chord chord = (Chord)voice.Events[0];
        Chord changedChord = chord with
        {
            Notes = [new Note(new Pitch(Step.C, sharp ? 1 : 0, 4))],
        };
        Voice changedVoice = voice with { Events = voice.Events.SetItem(0, changedChord) };
        StaffMeasure changedStaffMeasure = staffMeasure with
        {
            Voices = staffMeasure.Voices.SetItem(0, changedVoice),
        };
        return score with { Content = score.Content.SetItem(key, changedStaffMeasure) };
    }

    public static SmuflMetadata LoadMetadata()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string metadataPath = Path.Combine(directory.FullName, "assets", "fonts", "Bravura.json");
            string glyphNamesPath = Path.Combine(directory.FullName, "assets", "fonts", "smufl_glyph_names.json");
            if (File.Exists(metadataPath) && File.Exists(glyphNamesPath))
            {
                return SmuflMetadata.Load(metadataPath, glyphNamesPath);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Bravura metadata was not found above the benchmark directory.");
    }
}
