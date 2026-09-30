using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Smufl;

namespace Tessitura.Benchmarks;

internal static class ReferenceScoreFactory
{
    public const int StaffCount = 30;
    public const int MeasureCount = 300;
    public const int ChangedMeasure = MeasureCount / 2;

    public static Score Create()
    {
        ImmutableArray<Staff>.Builder staves = ImmutableArray.CreateBuilder<Staff>(StaffCount);
        for (int staffIndex = 0; staffIndex < StaffCount; staffIndex++)
        {
            staves.Add(new Staff($"Staff {staffIndex + 1}"));
        }

        ImmutableArray<Instrument> instruments = [new Instrument("Reference", staves.MoveToImmutable())];
        ImmutableArray<Measure>.Builder measures = ImmutableArray.CreateBuilder<Measure>(MeasureCount);
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        Duration quarter = new(NoteValue.Quarter, 0);
        for (int measureIndex = 0; measureIndex < MeasureCount; measureIndex++)
        {
            measures.Add(new Measure(measureIndex + 1, new TimeSignature(4, 4)));
            for (int staffIndex = 0; staffIndex < StaffCount; staffIndex++)
            {
                ImmutableArray<MusicEvent>.Builder events = ImmutableArray.CreateBuilder<MusicEvent>(4);
                for (int beat = 0; beat < 4; beat++)
                {
                    events.Add(new Chord(new EventId(Guid.Empty), new Fraction(beat, 4), quarter,
                        [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto));
                }

                content.Add(new StaffMeasureKey(staffIndex, measureIndex),
                    new StaffMeasure([new Voice(1, events.MoveToImmutable())]));
            }
        }

        return new Score(new ScoreMetadata("Tessitura Performance Reference", "Tessitura"),
            instruments, measures.MoveToImmutable(), content.ToImmutable());
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
