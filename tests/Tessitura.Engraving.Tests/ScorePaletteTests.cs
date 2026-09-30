using System.Collections.Immutable;
using Tessitura.App;
using Tessitura.Core;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class ScorePaletteTests
{
    [Fact]
    public void ClefKeyAndAccidentalPalettesApplyToSelectionAndUndo()
    {
        ImmutableArray<Chord> chords = CreateFourQuarterNotes();
        Score original = CreateScore(chords, new TimeSignature(4, 4));
        ScoreInputController input = new(original);
        using ScoreWindowShell shell = new(new ScoreCanvas(), input);
        string settings = Path.Combine(Path.GetTempPath(), $"tessitura-palette-{Guid.NewGuid():N}.json");
        try
        {
            ActionRegistry actions = CreateActions(input, shell, settings);
            input.SelectEvent(chords[0].Id);

            Assert.True(actions.TryExecute("palette.clef.bass"));
            Assert.True(actions.TryExecute("palette.key.sharps.2"));
            Assert.True(actions.TryExecute("palette.accidental.sharp"));

            Assert.Equal(Clef.Bass, input.CurrentScore.Instruments[0].Staves[0].InitialClef);
            Assert.Equal(new KeySignature(2), input.CurrentScore.Measures[0].KeySignature);
            Chord changed = Assert.IsType<Chord>(input.CurrentScore.Content[
                new StaffMeasureKey(0, 0)].Voices[0].Events[0]);
            Assert.Equal(1, changed.Notes[0].Pitch.Alter);
            Assert.Equal(chords[0].Id, Assert.Single(input.CurrentSelection.Items).EventId);

            input.Undo();
            input.Undo();
            input.Undo();

            Assert.Equal(original, input.CurrentScore);
            Assert.Equal(chords[0].Id, Assert.Single(input.CurrentSelection.Items).EventId);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void MeterPaletteRebarsMusicAndPreservesTheScoreInvariant()
    {
        ImmutableArray<Chord> chords = CreateFourQuarterNotes();
        Score original = CreateScore(chords, new TimeSignature(4, 4));
        ScoreInputController input = new(original);
        using ScoreWindowShell shell = new(new ScoreCanvas(), input);
        string settings = Path.Combine(Path.GetTempPath(), $"tessitura-meter-{Guid.NewGuid():N}.json");
        try
        {
            ActionRegistry actions = CreateActions(input, shell, settings);
            input.SelectEvent(chords[0].Id);

            Assert.True(actions.TryExecute("palette.meter.3-4"));

            Assert.Equal(new TimeSignature(3, 4), input.CurrentScore.Measures[0].TimeSignature);
            Assert.True(input.CurrentScore.Measures.Length >= 2);
            for (int measureIndex = 0; measureIndex < input.CurrentScore.Measures.Length; measureIndex++)
            {
                StaffMeasure staffMeasure = input.CurrentScore.Content[new StaffMeasureKey(0, measureIndex)];
                Assert.True(ScoreValidator.IsMeasureValid(input.CurrentScore.Measures[measureIndex], staffMeasure));
            }

            HashSet<EventId> remainingIds = [];
            foreach (StaffMeasure staffMeasure in input.CurrentScore.Content.Values)
            {
                foreach (Voice voice in staffMeasure.Voices)
                {
                    foreach (MusicEvent musicEvent in voice.Events)
                    {
                        if (musicEvent is Chord)
                        {
                            remainingIds.Add(musicEvent.Id);
                        }
                    }
                }
            }

            Assert.Equal(chords.Length, remainingIds.Count);
            foreach (Chord chord in chords)
            {
                Assert.Contains(chord.Id, remainingIds);
            }

            input.Undo();

            Assert.Equal(original, input.CurrentScore);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    private static ActionRegistry CreateActions(
        ScoreInputController input,
        ScoreWindowShell shell,
        string settings)
    {
        ActionRegistry actions = ActionRegistry.LoadOrCreate(
            input.CreateActions().AddRange(shell.CreateActions()), settings);
        shell.AttachActionRegistry(actions);
        return actions;
    }

    private static ImmutableArray<Chord> CreateFourQuarterNotes()
    {
        ImmutableArray<Chord>.Builder chords = ImmutableArray.CreateBuilder<Chord>(4);
        for (int index = 0; index < 4; index++)
        {
            chords.Add(new Chord(new EventId(Guid.NewGuid()), new Fraction(index, 4),
                new Duration(NoteValue.Quarter, 0),
                [new Note(new Pitch(Step.C, 0, 4 + index))], StemDirection.Auto));
        }

        return chords.MoveToImmutable();
    }

    private static Score CreateScore(ImmutableArray<Chord> chords, TimeSignature timeSignature)
    {
        ImmutableArray<MusicEvent> events = [.. chords];
        return new Score(new ScoreMetadata("Test", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, timeSignature)],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, events)])));
    }
}
