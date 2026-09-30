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
