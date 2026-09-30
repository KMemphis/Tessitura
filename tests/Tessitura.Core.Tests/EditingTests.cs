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
    public void ShorteningANoteFillsTheNewGapWithAnExactRest()
    {
        EventId secondEventId = new(Guid.Parse("10000000-0000-0000-0000-000000000002"));
        EventId restEventId = new(Guid.Parse("10000000-0000-0000-0000-000000000003"));
        Score score = CreateScore(
        [
            new Chord(MainEventId, Fraction.Zero, new Duration(NoteValue.Quarter, 0),
                [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto),
            new Chord(secondEventId, new Fraction(1, 4), new Duration(NoteValue.Quarter, 0),
                [new Note(new Pitch(Step.D, 0, 4))], StemDirection.Auto),
            new Rest(restEventId, new Fraction(1, 2), new Duration(NoteValue.Half, 0)),
        ]);

        Score edited = new ChangeDurationCommand(
            MainEventId,
            new Duration(NoteValue.Eighth, 0)).Apply(score, Context);

        ImmutableArray<MusicEvent> events = GetVoice(edited, 0).Events;
        Assert.Equal(5, events.Length);
        Assert.Equal(new Duration(NoteValue.Eighth, 0), events[0].Duration);
        Rest insertedRest = Assert.IsType<Rest>(events[1]);
        Assert.Equal(new Fraction(1, 8), insertedRest.Onset);
        Assert.Equal(new Duration(NoteValue.Eighth, 0), insertedRest.Duration);
        Assert.Equal(new Fraction(1, 4), events[2].Onset);
        Assert.IsType<Chord>(events[2]);
        AssertVoiceValid(edited, 0);
    }

    [Fact]
    public void ExtendingADurationShiftsFollowingNotesAndConsumesOverlappingRests()
    {
        EventId secondEventId = new(Guid.Parse("15000000-0000-0000-0000-000000000001"));
        EventId trailingRestId = new(Guid.Parse("15000000-0000-0000-0000-000000000002"));
        Score score = CreateScore(
        [
            new Chord(MainEventId, Fraction.Zero, new Duration(NoteValue.Quarter, 0),
                [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto),
            new Chord(secondEventId, new Fraction(1, 4), new Duration(NoteValue.Quarter, 0),
                [new Note(new Pitch(Step.D, 0, 4))], StemDirection.Auto),
            new Rest(trailingRestId, new Fraction(1, 2), new Duration(NoteValue.Half, 0)),
        ]);

        Score edited = new ChangeDurationCommand(
            MainEventId,
            new Duration(NoteValue.Half, 0)).Apply(score, Context);

        ImmutableArray<MusicEvent> events = GetVoice(edited, 0).Events;
        Assert.Equal(new Duration(NoteValue.Half, 0), events[0].Duration);
        Chord shiftedChord = Assert.IsType<Chord>(events[1]);
        Assert.Equal(new Fraction(1, 2), shiftedChord.Onset);
        Assert.Equal(new Fraction(3, 4), events[2].Onset);
        Assert.Equal(new Duration(NoteValue.Quarter, 0), events[2].Duration);
        Assert.IsType<Rest>(events[2]);
        AssertVoiceValid(edited, 0);
    }

    [Fact]
    public void ExtendingANoteAcrossABarlineSplitsItAndAddsATie()
    {
        EventId leadingRestId = new(Guid.Parse("20000000-0000-0000-0000-000000000001"));
        EventId nextMeasureRestId = new(Guid.Parse("20000000-0000-0000-0000-000000000002"));
        Score score = CreateScore(
            [new Measure(1, new TimeSignature(4, 4)), new Measure(2, new TimeSignature(4, 4))],
            [new Rest(leadingRestId, Fraction.Zero, new Duration(NoteValue.Half, 1)),
             new Chord(MainEventId, new Fraction(3, 4), new Duration(NoteValue.Quarter, 0),
                 [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto)],
            [new Rest(nextMeasureRestId, Fraction.Zero, new Duration(NoteValue.Whole, 0))]);

        Score edited = new ChangeDurationCommand(
            MainEventId,
            new Duration(NoteValue.Half, 0)).Apply(score, Context);

        ImmutableArray<MusicEvent> firstMeasure = GetVoice(edited, 0).Events;
        Chord firstFragment = Assert.IsType<Chord>(firstMeasure[^1]);
        Assert.Equal(new Fraction(3, 4), firstFragment.Onset);
        Assert.Equal(new Duration(NoteValue.Quarter, 0), firstFragment.Duration);
        Assert.True(firstFragment.Notes[0].TiedToNext);

        ImmutableArray<MusicEvent> secondMeasure = GetVoice(edited, 1).Events;
        Chord continuation = Assert.IsType<Chord>(secondMeasure[0]);
        Assert.Equal(Fraction.Zero, continuation.Onset);
        Assert.Equal(new Duration(NoteValue.Quarter, 0), continuation.Duration);
        Assert.Equal(firstFragment.Notes[0].Pitch, continuation.Notes[0].Pitch);
        Assert.False(continuation.Notes[0].TiedToNext);
        Assert.NotEqual(firstFragment.Id, continuation.Id);
        AssertVoiceValid(edited, 0);
        AssertVoiceValid(edited, 1);
    }

    [Fact]
    public void ExtendingPastTheLastMeasureAddsMeasuresAndKeepsTheScoreValid()
    {
        Score score = CreateTwoStaffScore();

        Score edited = new ChangeDurationCommand(
            MainEventId,
            new Duration(NoteValue.Whole, 2)).Apply(score, Context);

        Assert.Equal(2, edited.Measures.Length);
        Chord firstFragment = Assert.IsType<Chord>(GetVoice(edited, 0).Events[0]);
        Chord continuation = Assert.IsType<Chord>(GetVoice(edited, 1).Events[0]);
        Assert.Equal(new Duration(NoteValue.Whole, 0), firstFragment.Duration);
        Assert.True(firstFragment.Notes[0].TiedToNext);
        Assert.Equal(new Duration(NoteValue.Half, 1), continuation.Duration);
        Assert.False(continuation.Notes[0].TiedToNext);
        Assert.Equal(new Duration(NoteValue.Quarter, 0), GetVoice(edited, 1).Events[1].Duration);
        Assert.IsType<Rest>(edited.Content[new StaffMeasureKey(1, 1)].Voices[0].Events[0]);
        AssertScoreVoicesValid(edited);
    }

    [Fact]
    public void RewritesCompoundMeterRestsAtTheDottedQuarterPulse()
    {
        EventId restEventId = new(Guid.Parse("30000000-0000-0000-0000-000000000001"));
        Score score = CreateScore(
        [
            new Measure(1, new TimeSignature(6, 8)),
        ],
        [
            new Chord(MainEventId, Fraction.Zero, new Duration(NoteValue.Quarter, 1),
                [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto),
            new Rest(restEventId, new Fraction(3, 8), new Duration(NoteValue.Quarter, 1)),
        ]);

        Score edited = new ChangeDurationCommand(
            MainEventId,
            new Duration(NoteValue.Quarter, 0)).Apply(score, Context);

        ImmutableArray<MusicEvent> events = GetVoice(edited, 0).Events;
        Assert.Equal(3, events.Length);
        Assert.IsType<Chord>(events[0]);
        Assert.Equal(new Fraction(1, 4), events[1].Onset);
        Assert.Equal(new Duration(NoteValue.Eighth, 0), events[1].Duration);
        Assert.IsType<Rest>(events[1]);
        Assert.Equal(new Fraction(3, 8), events[2].Onset);
        Assert.Equal(new Duration(NoteValue.Quarter, 1), events[2].Duration);
        AssertVoiceValid(edited, 0);
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
        Score initial = CreateRhythmPropertyScore();
        History history = new(initial);
        int[] operations = [first, second, third, fourth, fifth, sixth];
        Score current = initial;

        foreach (int operation in operations)
        {
            IScoreCommand command = CreateCommand(operation, current);
            current = command.Apply(current, Context);
            AssertScoreVoicesValid(current);
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
        NoteValue noteValue = Math.Abs((long)value % 3) switch
        {
            0 => NoteValue.Sixteenth,
            1 => NoteValue.Eighth,
            _ => NoteValue.Quarter,
        };
        return new Duration(noteValue, (int)(Math.Abs((long)value) % 3));
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

    private static Score CreateScore(ImmutableArray<Measure> measures, params ImmutableArray<MusicEvent>[] eventsByMeasure)
    {
        if (measures.Length != eventsByMeasure.Length)
        {
            throw new ArgumentException("Each measure needs one voice event list.", nameof(eventsByMeasure));
        }

        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        for (int index = 0; index < measures.Length; index++)
        {
            content.Add(new StaffMeasureKey(0, index), new StaffMeasure([new Voice(1, eventsByMeasure[index])]));
        }

        return new Score(
            new ScoreMetadata("Test", "Composer"),
            [new Instrument("Piano", [new Staff("Staff")])],
            measures,
            content.ToImmutable());
    }

    private static Score CreateRhythmPropertyScore()
    {
        EventId firstRestId = new(Guid.Parse("40000000-0000-0000-0000-000000000001"));
        EventId secondRestId = new(Guid.Parse("40000000-0000-0000-0000-000000000002"));
        EventId thirdRestId = new(Guid.Parse("40000000-0000-0000-0000-000000000003"));
        EventId nextMeasureRestId = new(Guid.Parse("40000000-0000-0000-0000-000000000004"));
        return CreateScore(
            [new Measure(1, new TimeSignature(4, 4)), new Measure(2, new TimeSignature(4, 4))],
            [new Chord(MainEventId, Fraction.Zero, new Duration(NoteValue.Eighth, 0),
                 [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto),
             new Rest(firstRestId, new Fraction(1, 8), new Duration(NoteValue.Half, 0)),
             new Rest(secondRestId, new Fraction(5, 8), new Duration(NoteValue.Quarter, 0)),
             new Rest(thirdRestId, new Fraction(7, 8), new Duration(NoteValue.Eighth, 0))],
            [new Rest(nextMeasureRestId, Fraction.Zero, new Duration(NoteValue.Whole, 0))]);
    }

    private static Score CreateTwoStaffScore()
    {
        ScoreMetadata metadata = new("Test", "Composer");
        ImmutableArray<Instrument> instruments =
        [new Instrument("Piano", [new Staff("Right hand"), new Staff("Left hand")])];
        ImmutableArray<Measure> measures = [new Measure(1, new TimeSignature(4, 4))];
        ImmutableArray<MusicEvent> rightEvents =
        [new Chord(MainEventId, Fraction.Zero, new Duration(NoteValue.Whole, 0),
            [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto)];
        ImmutableArray<MusicEvent> leftEvents =
        [new Rest(new EventId(Guid.Parse("50000000-0000-0000-0000-000000000001")),
            Fraction.Zero, new Duration(NoteValue.Whole, 0))];
        ImmutableDictionary<StaffMeasureKey, StaffMeasure> content =
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, rightEvents)]))
                .Add(new StaffMeasureKey(1, 0), new StaffMeasure([new Voice(1, leftEvents)]));

        return new Score(metadata, instruments, measures, content);
    }

    private static Voice GetVoice(Score score, int measureIndex) =>
        score.Content[new StaffMeasureKey(0, measureIndex)].Voices[0];

    private static void AssertVoiceValid(Score score, int measureIndex)
    {
        Assert.True(ScoreValidator.IsMeasureValid(
            score.Measures[measureIndex],
            score.Content[new StaffMeasureKey(0, measureIndex)]));
    }

    private static void AssertScoreVoicesValid(Score score)
    {
        foreach (KeyValuePair<StaffMeasureKey, StaffMeasure> entry in score.Content)
        {
            Assert.InRange(entry.Key.MeasureIndex, 0, score.Measures.Length - 1);
            Assert.True(ScoreValidator.IsMeasureValid(score.Measures[entry.Key.MeasureIndex], entry.Value));
        }
    }

    private static MusicEvent GetEvent(Score score) =>
        score.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[0];
}
