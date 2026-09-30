using System.Collections.Immutable;

namespace Tessitura.Engraving;

/// <summary>Describes how far notation extends above and below one staff.</summary>
/// <param name="TopExtent">The skyline height above the top staff line.</param>
/// <param name="BottomExtent">The skyline depth below the bottom staff line.</param>
public readonly record struct StaffSkyline(double TopExtent, double BottomExtent);

/// <summary>Describes a system's staff skylines and vertical spacing settings.</summary>
/// <param name="Staves">The staves in top-to-bottom order.</param>
/// <param name="MinimumStaffGap">The minimum gap from a staff's bottom line to the next top line.</param>
/// <param name="SkylineClearance">The required clearance between adjacent skyline extents.</param>
/// <param name="LeadingSpace">The vertical space before the first staff.</param>
/// <param name="TrailingSpace">The vertical space after the final staff.</param>
public readonly record struct VerticalSystem(
    ImmutableArray<StaffSkyline> Staves,
    double MinimumStaffGap,
    double SkylineClearance,
    double LeadingSpace,
    double TrailingSpace);

/// <summary>Contains one system's page position and top-line position for each staff.</summary>
/// <param name="SystemIndex">The zero-based score system index.</param>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="Top">The system's top coordinate on the page.</param>
/// <param name="Height">The complete height, including skylines and margins.</param>
/// <param name="StaffTops">The top-line Y coordinate of each staff.</param>
public sealed record SystemVerticalPlacement(
    int SystemIndex,
    int PageNumber,
    double Top,
    double Height,
    ImmutableArray<double> StaffTops);

/// <summary>Contains page count and ordered vertical placements for all systems.</summary>
/// <param name="PageCount">The number of pages needed.</param>
/// <param name="Systems">The system placements in score order.</param>
public sealed record VerticalLayoutResult(
    int PageCount,
    ImmutableArray<SystemVerticalPlacement> Systems);

/// <summary>Places staff systems against skylines and fills pages without vertical overlap.</summary>
public sealed class VerticalPageLayouter
{
    private const double StaffHeight = 4;
    private const double CoordinateEpsilon = 1e-9;

    /// <summary>Assigns vertical staff positions and paginates systems from top to bottom.</summary>
    /// <param name="systems">Systems in score order.</param>
    /// <param name="pageHeight">The page height in staff spaces.</param>
    /// <param name="topMargin">The usable area's top margin.</param>
    /// <param name="bottomMargin">The usable area's bottom margin.</param>
    /// <param name="systemGap">The minimum gap between adjacent systems.</param>
    /// <returns>Page count and the position of each system and staff.</returns>
    public VerticalLayoutResult Layout(ReadOnlySpan<VerticalSystem> systems,
        double pageHeight, double topMargin, double bottomMargin, double systemGap)
    {
        if (!double.IsFinite(pageHeight) || pageHeight <= 0 ||
            !double.IsFinite(topMargin) || topMargin < 0 ||
            !double.IsFinite(bottomMargin) || bottomMargin < 0 ||
            !double.IsFinite(systemGap) || systemGap < 0 ||
            topMargin + bottomMargin >= pageHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(pageHeight));
        }

        if (systems.IsEmpty)
        {
            return new VerticalLayoutResult(0, ImmutableArray<SystemVerticalPlacement>.Empty);
        }

        double availableHeight = pageHeight - topMargin - bottomMargin;
        ImmutableArray<SystemVerticalPlacement>.Builder placements =
            ImmutableArray.CreateBuilder<SystemVerticalPlacement>(systems.Length);
        int pageNumber = 1;
        double y = topMargin;
        bool pageHasSystem = false;

        for (int systemIndex = 0; systemIndex < systems.Length; systemIndex++)
        {
            VerticalSystem system = systems[systemIndex];
            double[] staffOffsets = GetStaffOffsets(system);
            double height = GetSystemHeight(system, staffOffsets);
            if (height > availableHeight + CoordinateEpsilon)
            {
                throw new InvalidOperationException(
                    $"System {systemIndex} is taller than the usable page height.");
            }

            double proposedTop = pageHasSystem ? y + systemGap : y;
            if (pageHasSystem && proposedTop + height > pageHeight - bottomMargin + CoordinateEpsilon)
            {
                pageNumber++;
                y = topMargin;
                pageHasSystem = false;
                proposedTop = y;
            }

            if (pageHasSystem)
            {
                y += systemGap;
            }

            ImmutableArray<double>.Builder staffTops =
                ImmutableArray.CreateBuilder<double>(staffOffsets.Length);
            for (int index = 0; index < staffOffsets.Length; index++)
            {
                staffTops.Add(proposedTop + staffOffsets[index]);
            }

            placements.Add(new SystemVerticalPlacement(systemIndex, pageNumber,
                proposedTop, height, staffTops.MoveToImmutable()));
            y = proposedTop + height;
            pageHasSystem = true;
        }

        return new VerticalLayoutResult(pageNumber, placements.MoveToImmutable());
    }

    private static double[] GetStaffOffsets(VerticalSystem system)
    {
        if (system.Staves.IsDefaultOrEmpty ||
            !double.IsFinite(system.MinimumStaffGap) || system.MinimumStaffGap < 0 ||
            !double.IsFinite(system.SkylineClearance) || system.SkylineClearance < 0 ||
            !double.IsFinite(system.LeadingSpace) || system.LeadingSpace < 0 ||
            !double.IsFinite(system.TrailingSpace) || system.TrailingSpace < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(system));
        }

        double[] offsets = new double[system.Staves.Length];
        StaffSkyline first = system.Staves[0];
        ValidateSkyline(first);
        offsets[0] = system.LeadingSpace + first.TopExtent;
        for (int index = 1; index < system.Staves.Length; index++)
        {
            StaffSkyline previous = system.Staves[index - 1];
            StaffSkyline current = system.Staves[index];
            ValidateSkyline(current);

            // The basic skyline uses top/bottom extents around the five staff
            // lines. The clearance is a spacing rule from the engraving style.
            double skylineGap = previous.BottomExtent + current.TopExtent +
                system.SkylineClearance;
            double staffGap = Math.Max(system.MinimumStaffGap, skylineGap);
            offsets[index] = offsets[index - 1] + StaffHeight + staffGap;
        }

        return offsets;
    }

    private static double GetSystemHeight(VerticalSystem system, double[] staffOffsets)
    {
        StaffSkyline last = system.Staves[^1];
        return staffOffsets[^1] + StaffHeight + last.BottomExtent + system.TrailingSpace;
    }

    private static void ValidateSkyline(StaffSkyline skyline)
    {
        if (!double.IsFinite(skyline.TopExtent) || skyline.TopExtent < 0 ||
            !double.IsFinite(skyline.BottomExtent) || skyline.BottomExtent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(skyline));
        }
    }
}
