using System.Collections.Immutable;
using Tessitura.Core;
using Xunit;

namespace Tessitura.Core.Tests;

public sealed class ScoreModelTests
{
    [Fact]
    public void FourQuarterNotesFillFourFourMeasure()
    {
        Measure measure = new(1, new TimeSignature(4, 4));
        StaffMeasure content = WithQuarterNotes(4);

        Assert.True(ScoreValidator.IsMeasureValid(measure, content));
    }

    [Fact]
    public void FiveQuarterNotesOverfillFourFourMeasure()
    {
        Measure measure = new(1, new TimeSignature(4, 4));
        StaffMeasure content = WithQuarterNotes(5);

        Assert.False(ScoreValidator.IsMeasureValid(measure, content));
    }

    [Fact]
    public void LyricModelRequiresPositiveVersesTextAndKnownMarkValues()
    {
        EventId target = new(Guid.NewGuid());
        Assert.Throws<ArgumentOutOfRangeException>(() => new LyricAttachment(target, 0, "la"));
        Assert.Throws<ArgumentException>(() => new LyricAttachment(target, 1, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LyricAttachment(target, 1, "la", (LyricSyllabic)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LyricAttachment(target, 1, "la", Extender: (LyricExtender)99));
        Assert.Equal(string.Empty, new LyricAttachment(target, 1, "", Extender: LyricExtender.Stop).Text);
    }

    [Fact]
    public void GapInVoiceFailsEvenWhenDurationsSumToMeasureLength()
    {
        Measure measure = new(1, new TimeSignature(4, 4));
        ImmutableArray<MusicEvent> events =
        [
            QuarterChord(0),
            QuarterChord(2),
            QuarterChord(3),
            QuarterChord(4),
        ];
        StaffMeasure content = new([new Voice(1, events)]);

        Assert.False(ScoreValidator.IsMeasureValid(measure, content));
    }

    [Fact]
    public void ScoreSharesImmutableCollections()
    {
        Measure measure = new(1, new TimeSignature(4, 4));
        StaffMeasure content = WithQuarterNotes(4);
        Score score = new(
            new ScoreMetadata("Test", "Composer"),
            [new Instrument("Piano", [new Staff("Right hand")])],
            [measure],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), content));

        Assert.True(ScoreValidator.IsMeasureValid(score.Measures[0], score.Content[new StaffMeasureKey(0, 0)]));
    }

    [Fact]
    public void RepeatInfoRejectsInvalidPassCountsEndingsAndNavigationMarks()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepeatInfo(EndRepeat: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepeatInfo(Endings: [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepeatInfo(Endings: [1, 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepeatInfo(Target: (RepeatTarget)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepeatInfo(Jump: (RepeatJump)99));
        Assert.Empty(new RepeatInfo().Endings);
    }

    private static StaffMeasure WithQuarterNotes(int count)
    {
        ImmutableArray<MusicEvent>.Builder events = ImmutableArray.CreateBuilder<MusicEvent>();
        for (int index = 0; index < count; index++)
        {
            events.Add(QuarterChord(index));
        }

        return new StaffMeasure([new Voice(1, events.ToImmutable())]);
    }

    private static Chord QuarterChord(int index) => new(
        new EventId(Guid.NewGuid()),
        new Fraction(index, 4),
        new Duration(NoteValue.Quarter, 0),
        [new Note(new Pitch(Step.C, 0, 4))],
        StemDirection.Auto);
}
