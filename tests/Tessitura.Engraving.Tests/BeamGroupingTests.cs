using Tessitura.Core;
using Tessitura.Engraving;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class BeamGroupingTests
{
    [Theory]
    [InlineData(-2, StemDirection.Up)]
    [InlineData(-1, StemDirection.Up)]
    [InlineData(0, StemDirection.Down)]
    [InlineData(1, StemDirection.Down)]
    [InlineData(2, StemDirection.Down)]
    public void StemDirectionUsesMiddleLine(int halfSpacesFromMiddle, StemDirection expected) =>
        Assert.Equal(expected, BeamGrouper.ChooseStemDirection(halfSpacesFromMiddle));

    [Theory]
    [InlineData(2, 4, 4, 2, 2)]
    [InlineData(3, 4, 6, 2, 2, 2)]
    [InlineData(4, 4, 8, 4, 4)]
    [InlineData(2, 2, 8, 4, 4)]
    [InlineData(6, 8, 6, 3, 3)]
    [InlineData(9, 8, 9, 3, 3, 3)]
    public void GroupsEighthsByMeter(int numerator, int denominator,
        int eventCount, params int[] expectedSizes)
    {
        BeamEvent[] events = Eighths(eventCount);

        BeamGroup[] groups = new BeamGrouper().Group(events,
            new TimeSignature(numerator, denominator)).ToArray();

        Assert.Equal(expectedSizes, groups.Select(group => group.Count));
        int start = 0;
        foreach (BeamGroup group in groups)
        {
            Assert.Equal(start, group.StartIndex);
            start += group.Count;
        }
    }

    [Fact]
    public void RestsAndGapsBreakGroups()
    {
        BeamEvent[] events =
        [
            E(0), E(1), new(new Fraction(2, 8), new Fraction(1, 8), true),
            E(3), E(4), E(6), E(7),
        ];

        BeamGroup[] groups = new BeamGrouper().Group(events, new TimeSignature(4, 4)).ToArray();

        Assert.Equal([new BeamGroup(0, 2), new BeamGroup(5, 2)], groups);
    }

    [Fact]
    public void LongNoteAndHalfBarBoundaryBreakGroups()
    {
        BeamEvent[] events =
        [
            E(0), E(1), new(new Fraction(2, 8), new Fraction(1, 4), false),
            E(4), E(5), E(6), E(7),
        ];

        BeamGroup[] groups = new BeamGrouper().Group(events, new TimeSignature(4, 4)).ToArray();

        Assert.Equal([new BeamGroup(0, 2), new BeamGroup(3, 4)], groups);
    }

    private static BeamEvent[] Eighths(int count) =>
        Enumerable.Range(0, count).Select(E).ToArray();

    private static BeamEvent E(int eighthIndex) =>
        new(new Fraction(eighthIndex, 8), new Fraction(1, 8), false);
}
