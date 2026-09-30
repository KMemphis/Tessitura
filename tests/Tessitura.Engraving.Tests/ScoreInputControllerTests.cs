using System.Collections.Immutable;
using Avalonia.Input;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Editing;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class ScoreInputControllerTests
{
    [Fact]
    public void KeyboardActionsWriteCmajorScaleAcrossMeasureBoundary()
    {
        ScoreInputController input = new(CreateBlankScore());
        using TemporaryShortcutSettings settings = new();
        ActionRegistry actions = settings.CreateRegistry(input);

        Assert.True(actions.TryExecute(Key.N, KeyModifiers.None));
        foreach (Key key in new[] { Key.C, Key.D, Key.E, Key.F, Key.G, Key.A, Key.B, Key.C })
        {
            Assert.True(actions.TryExecute(key, KeyModifiers.None));
        }

        Pitch[] actual = ReadPitches(input.CurrentScore);
        Pitch[] expected =
        [
            new(Step.C, 0, 4),
            new(Step.D, 0, 4),
            new(Step.E, 0, 4),
            new(Step.F, 0, 4),
            new(Step.G, 0, 4),
            new(Step.A, 0, 4),
            new(Step.B, 0, 4),
            new(Step.C, 0, 5),
        ];

        Assert.Equal(expected, actual);
        Assert.Equal(2, input.CurrentScore.Measures.Length);
        Assert.Equal(new Fraction(2, 1), input.Cursor.Position);
        Assert.All(input.CurrentScore.Measures.Select((measure, index) =>
        {
            StaffMeasure content = input.CurrentScore.Content[new StaffMeasureKey(0, index)];
            return ScoreValidator.IsMeasureValid(measure, content);
        }), Assert.True);
    }

    [Fact]
    public void EscapeLeavesNoteEntryAndLetterKeysStopEditing()
    {
        Score initial = CreateBlankScore();
        ScoreInputController input = new(initial);
        using TemporaryShortcutSettings settings = new();
        ActionRegistry actions = settings.CreateRegistry(input);

        Assert.Equal(ScoreInputMode.Selection, input.Mode);
        Assert.True(actions.TryExecute(Key.N, KeyModifiers.None));
        Assert.Equal(ScoreInputMode.NoteEntry, input.Mode);
        Assert.True(actions.TryExecute(Key.Escape, KeyModifiers.None));
        Assert.Equal(ScoreInputMode.Selection, input.Mode);
        Assert.True(actions.TryExecute(Key.A, KeyModifiers.None));
        Assert.Empty(ReadPitches(input.CurrentScore));
        Assert.Equal(initial, input.CurrentScore);
    }

    [Fact]
    public void DurationAndDotShortcutsControlTheNextWrittenNote()
    {
        ScoreInputController input = new(CreateBlankScore());
        using TemporaryShortcutSettings settings = new();
        ActionRegistry actions = settings.CreateRegistry(input);

        actions.TryExecute(Key.N, KeyModifiers.None);
        actions.TryExecute(Key.D3, KeyModifiers.None);
        actions.TryExecute(Key.OemPeriod, KeyModifiers.None);
        actions.TryExecute(Key.A, KeyModifiers.None);

        Chord chord = Assert.IsType<Chord>(input.CurrentScore.Content[new StaffMeasureKey(0, 0)]
            .Voices[0].Events[0]);
        Assert.Equal(new Duration(NoteValue.Sixteenth, 1), chord.Duration);
        Assert.Equal(new Fraction(3, 32), input.Cursor.Position);
    }

    [Fact]
    public void NearestOctaveFollowsPreviousWrittenPitchInEitherDirection()
    {
        ScoreInputController input = new(CreateBlankScore());
        using TemporaryShortcutSettings settings = new();
        ActionRegistry actions = settings.CreateRegistry(input);
        actions.TryExecute(Key.N, KeyModifiers.None);
        actions.TryExecute(Key.B, KeyModifiers.None);
        actions.TryExecute(Key.C, KeyModifiers.None);
        actions.TryExecute(Key.B, KeyModifiers.None);

        Assert.Equal(
            [new Pitch(Step.B, 0, 4), new Pitch(Step.C, 0, 5), new Pitch(Step.B, 0, 4)],
            ReadPitches(input.CurrentScore));
    }

    [Fact]
    public void UndoAndRedoRestoreScoreAndMusicalCursor()
    {
        ScoreInputController input = new(CreateBlankScore());
        using TemporaryShortcutSettings settings = new();
        ActionRegistry actions = settings.CreateRegistry(input);
        actions.TryExecute(Key.N, KeyModifiers.None);
        actions.TryExecute(Key.C, KeyModifiers.None);

        actions.TryExecute(Key.Z, KeyModifiers.Control);
        Assert.Empty(ReadPitches(input.CurrentScore));
        Assert.Equal(Fraction.Zero, input.Cursor.Position);

        actions.TryExecute(Key.Y, KeyModifiers.Control);
        Assert.Equal([new Pitch(Step.C, 0, 4)], ReadPitches(input.CurrentScore));
        Assert.Equal(new Fraction(1, 4), input.Cursor.Position);
    }

    [Fact]
    public void UndoRestoresThePreviousPitchForNearestOctaveSelection()
    {
        ScoreInputController input = new(CreateBlankScore());
        using TemporaryShortcutSettings settings = new();
        ActionRegistry actions = settings.CreateRegistry(input);
        actions.TryExecute(Key.N, KeyModifiers.None);
        actions.TryExecute(Key.C, KeyModifiers.None);
        actions.TryExecute(Key.A, KeyModifiers.None);

        actions.TryExecute(Key.Z, KeyModifiers.Control);
        Assert.Equal(new Pitch(Step.C, 0, 4), input.LastEnteredPitch);
        actions.TryExecute(Key.F, KeyModifiers.None);

        Assert.Equal(
            [new Pitch(Step.C, 0, 4), new Pitch(Step.F, 0, 4)],
            ReadPitches(input.CurrentScore));
    }

    private sealed class TemporaryShortcutSettings : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "Tessitura-" + Guid.NewGuid().ToString("N"));

        public ActionRegistry CreateRegistry(ScoreInputController input) =>
            ActionRegistry.LoadOrCreate(input.CreateActions(), Path.Combine(_directory, "shortcuts.json"));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }

    private static Pitch[] ReadPitches(Score score) => score.Content
        .OrderBy(pair => pair.Key.MeasureIndex)
        .SelectMany(pair => pair.Value.Voices[0].Events)
        .OfType<Chord>()
        .SelectMany(chord => chord.Notes)
        .Select(note => note.Pitch)
        .ToArray();

    private static Score CreateBlankScore()
    {
        Measure measure = new(1, new TimeSignature(4, 4));
        Rest rest = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Whole, 0));
        return new Score(
            new ScoreMetadata("Untitled", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [measure],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [rest])])));
    }
}
