using System.Collections.Immutable;
using FsCheck.Xunit;
using Tessitura.Core;
using Tessitura.Editing;
using Xunit;

namespace Tessitura.Core.Tests;

public sealed class EditingTests
{
    private static readonly EventId MainEventId = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));
    private static readonly EditContext Context = new(0, 0, 1);

    [Fact]
    public void InsertNoteAddsWrittenPitchWithoutChangingOriginalSnapshot()
    {
        Score original = CreateScore();
        Pitch pitch = new(Step.G, -1, 5);

        Score edited = new InsertNoteCommand(MainEventId, pitch).Apply(original, Context);

        Chord originalChord = Assert.IsType<Chord>(GetEvent(original));
        Chord editedChord = Assert.IsType<Chord>(GetEvent(edited));
        Assert.Single(originalChord.Notes);
        Assert.Equal(new Note(pitch), editedChord.Notes[1]);
        Assert.NotSame(original.Content, edited.Content);
    }

    [Fact]
    public void InsertNoteReplacesRestAtItsExactMusicalPosition()
    {
        Score original = CreateScoreWithRest();

        Score edited = new InsertNoteCommand(MainEventId, new Pitch(Step.D, 0, 4)).Apply(original, Context);

        Chord chord = Assert.IsType<Chord>(GetEvent(edited));
        Assert.Equal(Fraction.Zero, chord.Onset);
        Assert.Equal(new Duration(NoteValue.Whole, 0), chord.Duration);
        Assert.Single(chord.Notes);
        Assert.IsType<Rest>(GetEvent(original));
    }

    [Fact]
    public void DeleteNoteRemovesOneChordToneAndTurnsTheLastToneIntoARest()
    {
        Score original = CreateScoreWithNotes(
            new Note(new Pitch(Step.C, 0, 4)),
            new Note(new Pitch(Step.E, 0, 4)));

        Score oneTone = new DeleteNoteCommand(MainEventId, 1).Apply(original, Context);
        Chord remainingChord = Assert.IsType<Chord>(GetEvent(oneTone));
        Assert.Equal(new Note(new Pitch(Step.C, 0, 4)), Assert.Single(remainingChord.Notes));

        Score emptyChord = new DeleteNoteCommand(MainEventId, 0).Apply(oneTone, Context);
        Rest replacement = Assert.IsType<Rest>(GetEvent(emptyChord));
        Assert.Equal(Fraction.Zero, replacement.Onset);
        Assert.Equal(new Duration(NoteValue.Whole, 0), replacement.Duration);
    }

    [Fact]
    public void ChangePitchPreservesWrittenSpelling()
    {
        Score original = CreateScore();
        Pitch writtenPitch = new(Step.B, 1, 3);

        Score edited = new ChangePitchCommand(MainEventId, 0, writtenPitch).Apply(original, Context);

        Note changed = Assert.IsType<Chord>(GetEvent(edited)).Notes[0];
        Assert.Equal(writtenPitch, changed.Pitch);
        Assert.Equal(60, changed.Pitch.MidiNumber);
        Assert.NotEqual(new Pitch(Step.C, 0, 4), changed.Pitch);
    }

    [Fact]
    public void ChangeAlterationAndDurationAndDotCountUpdateExactValues()
    {
        Score original = CreateScore();
        Score altered = new ChangeAlterationCommand(MainEventId, 0, -1).Apply(original, Context);
        Assert.Equal(-1, Assert.IsType<Chord>(GetEvent(altered)).Notes[0].Pitch.Alter);

        Duration dottedEighth = new(NoteValue.Eighth, 1);
        Score resized = new ChangeDurationCommand(MainEventId, dottedEighth).Apply(altered, Context);
        Assert.Equal(dottedEighth, Assert.IsType<Chord>(GetEvent(resized)).Duration);
        Assert.Equal(new Fraction(3, 16), Assert.IsType<Chord>(GetEvent(resized)).Duration.Length);

        Score redotted = new ChangeDotCountCommand(MainEventId, 2).Apply(resized, Context);
        Assert.Equal(new Duration(NoteValue.Eighth, 2), Assert.IsType<Chord>(GetEvent(redotted)).Duration);
        Assert.Equal(new Fraction(7, 32), Assert.IsType<Chord>(GetEvent(redotted)).Duration.Length);
    }

    [Fact]
    public void ChangeTieUpdatesOneNoteAndRoundTripsThroughHistory()
    {
        Score original = CreateScore();
        Score tied = new ChangeTieCommand(MainEventId, 0, TiedToNext: true).Apply(original, Context);
        History history = new(original);
        history.Push(tied, "Change tie", Selection.Empty);

        Assert.False(Assert.IsType<Chord>(GetEvent(original)).Notes[0].TiedToNext);
        Assert.True(Assert.IsType<Chord>(GetEvent(tied)).Notes[0].TiedToNext);
        Assert.Equal(original, history.Undo());
        Assert.Equal(tied, history.Redo());

        Score repitched = new ChangePitchCommand(MainEventId, 0, new Pitch(Step.D, 0, 4))
            .Apply(tied, Context);
        Assert.True(Assert.IsType<Chord>(GetEvent(repitched)).Notes[0].TiedToNext);

        Score untied = new ChangeTieCommand(MainEventId, 0, TiedToNext: false).Apply(tied, Context);
        Assert.False(Assert.IsType<Chord>(GetEvent(untied)).Notes[0].TiedToNext);
    }

    [Fact]
    public void HistoryUndoAndRedoRestoreScoreAndSelectionSnapshots()
    {
        Score initial = CreateScore();
        Selection initialSelection = Selection.Empty;
        Selection insertedSelection = new([new SelectionItem(MainEventId, 1)]);
        History history = new(initial, initialSelection);

        Score inserted = new InsertNoteCommand(MainEventId, new Pitch(Step.E, 0, 4)).Apply(initial, Context);
        history.Push(inserted, "Insert note", insertedSelection);
        Score changed = new ChangePitchCommand(MainEventId, 1, new Pitch(Step.F, 0, 4))
            .Apply(inserted, Context);
        history.Push(changed, "Change pitch", Selection.Empty);

        Assert.Equal(changed, history.CurrentScore);
        Assert.True(history.CanUndo);
        Assert.Equal(inserted, history.Undo());
        Assert.Equal(insertedSelection, history.CurrentSelection);
        Assert.Equal(initial, history.Undo());
        Assert.Equal(initialSelection, history.CurrentSelection);
        Assert.False(history.CanUndo);
        Assert.Equal(initial, history.Undo());

        Assert.Equal(inserted, history.Redo());
        Assert.Equal(insertedSelection, history.CurrentSelection);
        Assert.Equal(changed, history.Redo());
        Assert.False(history.CanRedo);
        Assert.Equal(changed, history.Redo());
    }

    [Fact]
    public void PushingAfterUndoDiscardsTheRedoBranch()
    {
        Score initial = CreateScore();
        History history = new(initial);
        Score first = new InsertNoteCommand(MainEventId, new Pitch(Step.E, 0, 4)).Apply(initial, Context);
        history.Push(first, "Insert note", Selection.Empty);
        Score second = new ChangePitchCommand(MainEventId, 1, new Pitch(Step.F, 0, 4)).Apply(first, Context);
        history.Push(second, "Change pitch", Selection.Empty);

        Assert.Equal(first, history.Undo());
        Assert.True(history.CanRedo);

        Score branch = new ChangePitchCommand(MainEventId, 1, new Pitch(Step.G, 0, 4)).Apply(first, Context);
        history.Push(branch, "Change pitch", Selection.Empty);

        Assert.Equal(branch, history.CurrentScore);
        Assert.False(history.CanRedo);
        Assert.Equal(first, history.Undo());
        Assert.Equal(initial, history.Undo());
    }

    [Property(MaxTest = 200)]
    public void UndoingGeneratedCommandSequencesRestoresTheInitialScore(
        int first,
        int second,
        int third,
        int fourth,
        int fifth,
        int sixth)
    {
        Score initial = CreateScore();
        History history = new(initial);
        int[] operations = [first, second, third, fourth, fifth, sixth];
        Score current = initial;

        foreach (int operation in operations)
        {
            IScoreCommand command = CreateCommand(operation, current);
            current = command.Apply(current, Context);
            history.Push(current, command.Description, Selection.Empty);
        }

        for (int index = 0; index < operations.Length; index++)
        {
            current = history.Undo();
        }

        Assert.Equal(initial, current);
        Assert.Equal(initial, history.CurrentScore);
    }

    private static IScoreCommand CreateCommand(int value, Score score)
    {
        MusicEvent musicEvent = GetEvent(score);
        return Math.Abs((long)value % 7) switch
        {
            0 => new InsertNoteCommand(MainEventId, CreatePitch(value)),
            1 when musicEvent is Chord => new ChangePitchCommand(MainEventId, 0, CreatePitch(value)),
            2 when musicEvent is Chord => new ChangeAlterationCommand(MainEventId, 0, (int)(value % 8)),
            3 => new ChangeDurationCommand(MainEventId, CreateDuration(value)),
            4 => new ChangeDotCountCommand(MainEventId, (int)(Math.Abs((long)value) % 4)),
            5 when musicEvent is Chord chord && chord.Notes.Length > 1 =>
                new DeleteNoteCommand(MainEventId, chord.Notes.Length - 1),
            6 when musicEvent is Chord => new ChangeTieCommand(MainEventId, 0, TiedToNext: value % 2 == 0),
            _ => new InsertNoteCommand(MainEventId, CreatePitch(value)),
        };
    }

    private static Pitch CreatePitch(int value) => new(
        (Step)(Math.Abs((long)value) % 7),
        (int)(value % 4),
        4 + value % 3);

    private static Duration CreateDuration(int value)
    {
        NoteValue noteValue = (NoteValue)(1 << (int)(Math.Abs((long)value) % 8));
        int maximumDots = 62 - System.Numerics.BitOperations.Log2((uint)noteValue);
        return new Duration(noteValue, (int)(Math.Abs((long)value) % Math.Min(maximumDots + 1, 4)));
    }

    private static Score CreateScore() => CreateScoreWithNotes(new Note(new Pitch(Step.C, 0, 4)));

    private static Score CreateScoreWithRest()
    {
        ImmutableArray<MusicEvent> events =
        [new Rest(MainEventId, Fraction.Zero, new Duration(NoteValue.Whole, 0))];
        return CreateScore(events);
    }

    private static Score CreateScoreWithNotes(params Note[] notes)
    {
        ImmutableArray<MusicEvent> events =
        [new Chord(MainEventId, Fraction.Zero, new Duration(NoteValue.Whole, 0), [.. notes], StemDirection.Auto)];
        return CreateScore(events);
    }

    private static Score CreateScore(ImmutableArray<MusicEvent> events)
    {
        StaffMeasure staffMeasure = new([new Voice(1, events)]);
        return new Score(
            new ScoreMetadata("Test", "Composer"),
            [new Instrument("Piano", [new Staff("Staff")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), staffMeasure));
    }

    private static MusicEvent GetEvent(Score score) =>
        score.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[0];
}
