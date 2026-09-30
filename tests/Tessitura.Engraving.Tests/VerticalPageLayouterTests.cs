using System.Collections.Immutable;
using Tessitura.Engraving;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class VerticalPageLayouterTests
{
    [Fact]
    public void SeparatesStavesByTheirSkylinesAndRequiredClearance()
    {
        VerticalSystem system = TwoStaffSystem(bottomExtent: 2, topExtent: 1,
            minimumStaffGap: 3, skylineClearance: 0.5);

        SystemVerticalPlacement placement = Assert.Single(
            new VerticalPageLayouter().Layout([system], 30, 2, 2, 2).Systems);

        double topStaff = placement.StaffTops[0];
        double lowerStaff = placement.StaffTops[1];
        Assert.Equal(7.5, lowerStaff - topStaff, 6);
        Assert.True(lowerStaff - 1 - (topStaff + 4 + 2) >= 0.5);
    }

    [Fact]
    public void TenPageScoreKeepsEveryPairOfStaffSkylinesSeparated()
    {
        VerticalSystem[] systems = Enumerable.Range(0, 10)
            .Select(_ => TwoStaffSystem(1.25, 1.5, 3, 0.5))
            .ToArray();

        VerticalLayoutResult layout = new VerticalPageLayouter()
            .Layout(systems, pageHeight: 18, topMargin: 1, bottomMargin: 1, systemGap: 2);

        Assert.Equal(10, layout.PageCount);
        Assert.Equal(10, layout.Systems.Length);
        for (int index = 0; index < layout.Systems.Length; index++)
        {
            SystemVerticalPlacement placement = layout.Systems[index];
            Assert.Equal(index + 1, placement.PageNumber);
            StaffSkyline upper = systems[index].Staves[0];
            StaffSkyline lower = systems[index].Staves[1];
            double visibleGap = placement.StaffTops[1] - lower.TopExtent -
                (placement.StaffTops[0] + 4 + upper.BottomExtent);
            Assert.True(visibleGap >= systems[index].SkylineClearance);
        }
    }

    [Fact]
    public void PacksMultipleSystemsOntoOnePageWhenTheyFit()
    {
        VerticalSystem[] systems =
        [
            TwoStaffSystem(0.5, 0.5, 2, 0.5),
            TwoStaffSystem(0.5, 0.5, 2, 0.5),
        ];

        VerticalLayoutResult layout = new VerticalPageLayouter()
            .Layout(systems, pageHeight: 34, topMargin: 2, bottomMargin: 2, systemGap: 2);

        Assert.Equal(1, layout.PageCount);
        Assert.Equal(2, layout.Systems.Length);
        Assert.True(layout.Systems[1].Top >=
            layout.Systems[0].Top + layout.Systems[0].Height + 2);
    }

    [Fact]
    public void RejectsSystemTallerThanUsablePageHeight()
    {
        VerticalSystem system = new(
            [new StaffSkyline(0, 0), new StaffSkyline(0, 0), new StaffSkyline(0, 0)],
            MinimumStaffGap: 4, SkylineClearance: 0.5, LeadingSpace: 2, TrailingSpace: 2);

        Assert.Throws<InvalidOperationException>(() => new VerticalPageLayouter()
            .Layout([system], pageHeight: 12, topMargin: 1, bottomMargin: 1, systemGap: 1));
    }

    private static VerticalSystem TwoStaffSystem(double bottomExtent, double topExtent,
        double minimumStaffGap, double skylineClearance) => new(
            ImmutableArray.Create(new StaffSkyline(0.5, bottomExtent),
                new StaffSkyline(topExtent, 0.5)),
            minimumStaffGap, skylineClearance, LeadingSpace: 1, TrailingSpace: 1);
}
