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

    [Fact]
    public void ShiftSelectionIncludesEventsBetweenMusicalEndpoints()
    {
        Score score = CreateTwoStaffScore(out EventId[] eventIds);
        ScoreInputController input = new(score);

        input.SelectEvent(eventIds[0]);
        input.SelectEvent(eventIds[6], extendRange: true);

        Assert.Equal(eventIds.Take(3).Concat(eventIds.Skip(4).Take(3)),
            input.CurrentSelection.Items.Select(item => item.EventId));
        Assert.Equal(
            new SelectionRange(new MusicalSelectionPoint(0, Fraction.Zero),
                new MusicalSelectionPoint(1, new Fraction(1, 2))),
            input.CurrentSelection.Range);
    }

    [Fact]
    public void ControlSelectionAddsAnotherElementToTheSelectionList()
    {
        Score score = CreateFourNoteScore(out EventId[] eventIds);
        ScoreInputController input = new(score);

        input.SelectEvent(eventIds[0]);
        input.SelectEvent(eventIds[2], additive: true);

        Assert.Equal([eventIds[0], eventIds[2]],
            input.CurrentSelection.Items.Select(item => item.EventId));
        Assert.Null(input.CurrentSelection.Range);
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

    private static Score CreateFourNoteScore(out EventId[] eventIds)
    {
        eventIds = Enumerable.Range(0, 4).Select(_ => new EventId(Guid.NewGuid())).ToArray();
        ImmutableArray<MusicEvent>.Builder events = ImmutableArray.CreateBuilder<MusicEvent>(4);
        for (int index = 0; index < eventIds.Length; index++)
        {
            events.Add(new Chord(
                eventIds[index],
                new Fraction(index, 4),
                new Duration(NoteValue.Quarter, 0),
                [new Note(new Pitch(Step.C, 0, 4))],
                StemDirection.Auto));
        }

        return new Score(
            new ScoreMetadata("Selection", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
            new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, events.ToImmutable())])));
    }

    private static Score CreateTwoStaffScore(out EventId[] eventIds)
    {
        eventIds = Enumerable.Range(0, 8).Select(_ => new EventId(Guid.NewGuid())).ToArray();
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        for (int staffIndex = 0; staffIndex < 2; staffIndex++)
        {
            ImmutableArray<MusicEvent>.Builder events = ImmutableArray.CreateBuilder<MusicEvent>(4);
            for (int noteIndex = 0; noteIndex < 4; noteIndex++)
            {
                events.Add(new Chord(
                    eventIds[staffIndex * 4 + noteIndex],
                    new Fraction(noteIndex, 4),
                    new Duration(NoteValue.Quarter, 0),
                    [new Note(new Pitch(Step.C, 0, staffIndex == 0 ? 4 : 3))],
                    StemDirection.Auto));
            }

            content.Add(new StaffMeasureKey(staffIndex, 0),
                new StaffMeasure([new Voice(1, events.ToImmutable())]));
        }

        return new Score(
            new ScoreMetadata("Selection", ""),
            [new Instrument("Piano", [new Staff("Treble"), new Staff("Bass")])],
            [new Measure(1, new TimeSignature(4, 4))],
            content.ToImmutable());
    }
}
