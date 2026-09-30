using Tessitura.Engraving;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class SystemBreakerTests
{
    [Fact]
    public void DynamicProgramSelectsBalancedFullSystems()
    {
        SystemBreakMeasure[] measures = Enumerable.Repeat(
            new SystemBreakMeasure(8, 12, 1), 5).ToArray();

        SystemLine[] systems = new SystemBreaker().Layout(measures, 36).ToArray();

        Assert.Equal(2, systems.Length);
        Assert.Equal(new SystemLineMeasureRange(0, 3), systems[0].Range);
        Assert.Equal(new SystemLineMeasureRange(3, 2), systems[1].Range);
        Assert.Equal(36, systems[0].PlacedWidth, 6);
    }

    [Fact]
    public void NonFinalLineAtEightyPercentIsJustifiedAndFinalLineStaysRagged()
    {
        SystemBreakMeasure[] measures =
        [
            new(7.9, 8, 1), new(7.9, 8, 1), new(7.9, 8, 1),
            new(7.9, 8, 1), new(7.9, 8, 1),
        ];

        SystemLine[] systems = new SystemBreaker().Layout(measures, 30).ToArray();

        Assert.Equal(3, systems[0].Range.Count);
        Assert.Equal(24, systems[0].NaturalWidth, 6);
        Assert.Equal(30, systems[0].PlacedWidth, 6);
        Assert.Equal(16, systems[1].NaturalWidth, 6);
        Assert.Equal(16, systems[1].PlacedWidth, 6);
    }

    [Fact]
    public void DistributesRemainingWidthAccordingToMeasureElasticity()
    {
        SystemBreakMeasure[] measures =
        [
            new(3, 4, 1), new(3, 4, 3), new(5.5, 5.5, 1),
        ];

        SystemLine[] systems = new SystemBreaker().Layout(measures, 10).ToArray();

        Assert.Equal(2, systems[0].Range.Count);
        Assert.Equal(4.5, systems[0].MeasureWidths[0], 6);
        Assert.Equal(5.5, systems[0].MeasureWidths[1], 6);
        Assert.Equal(10, systems[0].PlacedWidth, 6);
    }

    [Fact]
    public void CompressesOverfullSystemWithoutCrossingMinimumWidths()
    {
        SystemBreakMeasure[] measures =
        [
            new(5, 5.68, 1), new(5, 5.68, 1), new(3, 4, 1),
        ];

        SystemLine[] systems = new SystemBreaker().Layout(measures, 10).ToArray();

        Assert.Equal(2, systems[0].Range.Count);
        Assert.Equal(11.36, systems[0].NaturalWidth, 6);
        Assert.Equal(5, systems[0].MeasureWidths[0], 6);
        Assert.Equal(5, systems[0].MeasureWidths[1], 6);
    }

    [Fact]
    public void ThrowsWhenOneMeasureCannotFitTheAvailableWidth()
    {
        SystemBreakMeasure[] measures = [new(11, 12, 1)];

        Assert.Throws<InvalidOperationException>(() =>
            new SystemBreaker().Layout(measures, 10));
    }

    [Fact]
    public void ThrowsWhenACompleteScoreWouldNeedExcessiveCompression()
    {
        SystemBreakMeasure[] measures =
        [
            new(4.5, 6, 1), new(4.5, 6, 1),
        ];

        Assert.Throws<InvalidOperationException>(() =>
            new SystemBreaker().Layout(measures, 10));
    }
}
