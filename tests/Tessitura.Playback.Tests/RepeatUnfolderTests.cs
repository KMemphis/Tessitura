using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Playback.Performance;
using Xunit;

namespace Tessitura.Playback.Tests;

public sealed class RepeatUnfolderTests
{
    [Fact]
    public void UnfoldsASimpleRepeatTwice()
    {
        Score score = ScoreWith(
            new RepeatInfo(StartRepeat: true),
            new RepeatInfo(EndRepeat: 2),
            null);

        ImmutableArray<MeasureVisit> visits = RepeatUnfolder.Unfold(score);

        Assert.Equal([0, 1, 0, 1, 2], visits.Select(static visit => visit.MeasureIndex));
        Assert.Equal([Fraction.Zero, Fraction.One, new Fraction(2, 1), new Fraction(3, 1), new Fraction(4, 1)],
            visits.Select(static visit => visit.PlaybackPosition));
    }

    [Fact]
    public void UnfoldsFirstAndSecondEndingsOnTheirMatchingPasses()
    {
        Score score = ScoreWith(
            new RepeatInfo(StartRepeat: true),
            null,
            new RepeatInfo(EndRepeat: 2, Endings: [1]),
            new RepeatInfo(Endings: [2]),
            null);

        ImmutableArray<MeasureVisit> visits = RepeatUnfolder.Unfold(score);

        Assert.Equal([0, 1, 2, 0, 1, 3, 4], visits.Select(static visit => visit.MeasureIndex));
    }

    [Fact]
    public void ResetsAnInnerRepeatForEachOuterPass()
    {
        Score score = ScoreWith(
            new RepeatInfo(StartRepeat: true),
            new RepeatInfo(StartRepeat: true),
            new RepeatInfo(EndRepeat: 2),
            new RepeatInfo(EndRepeat: 2),
            null);

        ImmutableArray<MeasureVisit> visits = RepeatUnfolder.Unfold(score);

        Assert.Equal([0, 1, 2, 1, 2, 3, 0, 1, 2, 1, 2, 3, 4],
            visits.Select(static visit => visit.MeasureIndex));
    }

    [Fact]
    public void DaCapoAlFineStopsAtFineOnlyAfterReturningToTheStart()
    {
        Score score = ScoreWith(
            null,
            new RepeatInfo(Jump: RepeatJump.Fine),
            new RepeatInfo(Jump: RepeatJump.DaCapoAlFine));

        ImmutableArray<MeasureVisit> visits = RepeatUnfolder.Unfold(score);

        Assert.Equal([0, 1, 2, 0, 1], visits.Select(static visit => visit.MeasureIndex));
    }

    [Fact]
    public void DalSegnoAlCodaJumpsFromToCodaAfterReturningToSegno()
    {
        Score score = ScoreWith(
            null,
            new RepeatInfo(Target: RepeatTarget.Segno),
            new RepeatInfo(Jump: RepeatJump.ToCoda),
            null,
            new RepeatInfo(Jump: RepeatJump.DalSegnoAlCoda),
            new RepeatInfo(Target: RepeatTarget.Coda),
            null);

        ImmutableArray<MeasureVisit> visits = RepeatUnfolder.Unfold(score);

        Assert.Equal([0, 1, 2, 3, 4, 1, 2, 5, 6],
            visits.Select(static visit => visit.MeasureIndex));
    }

    [Fact]
    public void JumpsDisableRepeatsAndSelectOnlyTheLastEnding()
    {
        Score score = ScoreWith(
            null,
            new RepeatInfo(StartRepeat: true),
            new RepeatInfo(EndRepeat: 2, Endings: [1]),
            new RepeatInfo(Endings: [2]),
            new RepeatInfo(Jump: RepeatJump.DaCapo));

        ImmutableArray<MeasureVisit> visits = RepeatUnfolder.Unfold(score);

        Assert.Equal([0, 1, 2, 1, 3, 4, 0, 1, 3, 4],
            visits.Select(static visit => visit.MeasureIndex));
    }

    [Fact]
    public void RejectsAnUnboundedRepeatExpansion()
    {
        Score score = ScoreWith(
            new RepeatInfo(StartRepeat: true),
            new RepeatInfo(EndRepeat: 1_000_000));

        Assert.Throws<InvalidOperationException>(() => RepeatUnfolder.Unfold(score));
    }

    private static Score ScoreWith(params RepeatInfo?[] repeats)
    {
        ImmutableArray<Measure> measures = [.. repeats.Select((repeat, index) =>
            new Measure(index + 1, new TimeSignature(4, 4), Repeat: repeat))];
        return new Score(new ScoreMetadata("Repeats", ""),
            [new Instrument("Piano", [new Staff("Treble")])], measures,
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty);
    }
}

public sealed class RepeatInterpretationTests
{
    [Fact]
    public void NotesAndTempoMarksArePlacedAtEachVisitedMeasureAndTiesStopAtJumps()
    {
        EventId tied = new(Guid.NewGuid());
        EventId next = new(Guid.NewGuid());
        EventId last = new(Guid.NewGuid());
        Chord first = new(tied, Fraction.Zero, new Duration(NoteValue.Whole, 0),
            [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Chord second = new(next, Fraction.Zero, new Duration(NoteValue.Whole, 0),
            [new Note(new Pitch(Step.C, 0, 4), TiedToNext: true)], StemDirection.Auto);
        Chord third = new(last, Fraction.Zero, new Duration(NoteValue.Whole, 0),
            [new Note(new Pitch(Step.D, 0, 4))], StemDirection.Auto);
        Score score = new(new ScoreMetadata("Repeats", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, new TimeSignature(4, 4), Repeat: new RepeatInfo(StartRepeat: true)),
             new Measure(2, new TimeSignature(4, 4), Repeat: new RepeatInfo(EndRepeat: 2)),
             new Measure(3, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [first])]))
                .Add(new StaffMeasureKey(0, 1), new StaffMeasure([new Voice(1, [second])]))
                .Add(new StaffMeasureKey(0, 2), new StaffMeasure([new Voice(1, [third])])),
            [new TempoAttachment(tied, new Duration(NoteValue.Quarter, 0), 60),
             new TempoAttachment(last, new Duration(NoteValue.Quarter, 0), 120)]);

        Interpretation result = Interpreter.Interpret(score);

        Assert.Equal([Fraction.Zero, Fraction.One, new Fraction(2, 1), new Fraction(3, 1), new Fraction(4, 1)],
            result.Notes.Select(static note => note.Start));
        Assert.Equal([Fraction.One, Fraction.One, Fraction.One, Fraction.One, Fraction.One],
            result.Notes.Select(static note => note.NotatedLength));
        Assert.Equal(60, result.Tempo.QuarterNotesPerMinuteAt(Fraction.Zero));
        Assert.Equal(120, result.Tempo.QuarterNotesPerMinuteAt(new Fraction(4, 1)));
    }

    [Fact]
    public void OctaveLinesAreCopiedForEveryRepeatVisit()
    {
        EventId noteId = new(Guid.NewGuid());
        Chord chord = new(noteId, Fraction.Zero, new Duration(NoteValue.Whole, 0),
            [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Score score = new(new ScoreMetadata("Repeats", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, new TimeSignature(4, 4), Repeat: new RepeatInfo(StartRepeat: true)),
             new Measure(2, new TimeSignature(4, 4), Repeat: new RepeatInfo(EndRepeat: 2))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [chord])]))
                .Add(new StaffMeasureKey(0, 1), new StaffMeasure([new Voice(1, [new Rest(
                    new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Whole, 0))])])),
            default,
            [new Spanner(noteId, noteId, SpannerKind.OctaveUp)]);

        Interpretation result = Interpreter.Interpret(score);

        Assert.Equal([72, 72], result.Notes.Select(static note => note.Midi));
        Assert.Equal([Fraction.Zero, new Fraction(2, 1)], result.Notes.Select(static note => note.Start));
    }
}
