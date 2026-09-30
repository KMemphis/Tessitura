using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Engraving.DisplayLists;
using Tessitura.Smufl;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;

namespace Tessitura.Engraving;

/// <summary>Contains one system composed as an A4 display-list page.</summary>
/// <param name="Page">The immutable page primitives in staff-space coordinates.</param>
/// <param name="StaffSpacePoints">The page-point size of one staff space.</param>
/// <param name="SystemIndex">The zero-based system index in the score.</param>
/// <param name="MeasureRange">The consecutive measures displayed by the system.</param>
public sealed record ScorePageComposition(
    Page Page,
    double StaffSpacePoints,
    int SystemIndex,
    SystemLineMeasureRange MeasureRange,
    DisplayPoint? CursorLocation);

/// <summary>Describes an exact musical cursor position for page composition.</summary>
/// <param name="StaffIndex">The zero-based staff index.</param>
/// <param name="Position">The exact score position in whole-note units.</param>
public readonly record struct EngravingCursor(int StaffIndex, Fraction Position);

/// <summary>Composes score events and layout into a page suitable for display.</summary>
public sealed class ScorePageComposer
{
    private const double PageWidthPoints = 595;
    private const double PageHeightPoints = 842;
    private const double HorizontalMarginPoints = 36;
    private const double VerticalMarginPoints = 36;
    private const double StaffHeightSpaces = 4;
    private const double StaffGapSpaces = 2.5;
    private const double RequestedStaffSpacePoints = 12;
    private readonly SmuflMetadata _metadata;
    private readonly Style _style;
    private readonly HorizontalSpacer _horizontalSpacer = new();

    /// <summary>Creates a score-page composer for one music font and engraving style.</summary>
    /// <param name="metadata">The selected SMuFL font metrics and glyph map.</param>
    /// <param name="style">The current engraving style.</param>
    public ScorePageComposer(SmuflMetadata metadata, Style style)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _style = style ?? throw new ArgumentNullException(nameof(style));
    }

    /// <summary>Gets the staff-space size that fits all staves on an A4 page.</summary>
    /// <param name="score">The immutable score snapshot.</param>
    /// <returns>The staff-space size in page points.</returns>
    public double GetStaffSpacePoints(Score score)
    {
        ArgumentNullException.ThrowIfNull(score);
        int staffCount = CountStaves(score);
        if (staffCount == 0)
        {
            throw new ArgumentException("A score must contain at least one staff.", nameof(score));
        }

        double staffPitch = StaffHeightSpaces + StaffGapSpaces;
        double maximumStaffSpace = (PageHeightPoints - 2 * VerticalMarginPoints) /
            (staffCount * staffPitch + StaffGapSpaces);
        return Math.Min(RequestedStaffSpacePoints, maximumStaffSpace);
    }

    /// <summary>Gets the music width available to the system breaker for the score.</summary>
    /// <param name="score">The immutable score snapshot.</param>
    /// <returns>The system's usable width in staff spaces after its header.</returns>
    public double GetAvailableWidth(Score score)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (score.Measures.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A score must contain at least one measure.", nameof(score));
        }

        double staffSpace = GetStaffSpacePoints(score);
        double headerWidth = 0;
        foreach (Measure measure in score.Measures)
        {
            double currentWidth = _horizontalSpacer.BuildHeader(_metadata, "gClef",
                new KeySignature(0), measure.TimeSignature, _style).MusicStartX;
            headerWidth = Math.Max(headerWidth, currentWidth);
        }

        double pageWidth = PageWidthPoints / staffSpace;
        double margin = HorizontalMarginPoints / staffSpace;
        double availableWidth = pageWidth - 2 * margin - headerWidth;
        if (availableWidth <= 0)
        {
            throw new InvalidOperationException("The score header leaves no room for music on the page.");
        }

        return availableWidth;
    }

    /// <summary>Composes the system containing one measure into an A4 page.</summary>
    /// <param name="score">The immutable score snapshot.</param>
    /// <param name="layout">The measured and system-broken score.</param>
    /// <param name="measureIndex">The measure whose system should be composed.</param>
    /// <param name="cursor">The optional musical cursor to place on the page.</param>
    /// <param name="cancellationToken">Cancels composition without returning partial primitives.</param>
    /// <returns>The display page and its score-system location.</returns>
    public ScorePageComposition Compose(Score score, ScoreLayoutResult layout, int measureIndex,
        EngravingCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(layout);
        cancellationToken.ThrowIfCancellationRequested();
        if (measureIndex < 0 || measureIndex >= score.Measures.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(measureIndex));
        }

        int systemIndex = FindSystem(layout.Systems, measureIndex);
        SystemLine system = layout.Systems[systemIndex];
        double staffSpace = GetStaffSpacePoints(score);
        double pageWidth = PageWidthPoints / staffSpace;
        double pageHeight = PageHeightPoints / staffSpace;
        double leftMargin = HorizontalMarginPoints / staffSpace;
        double topMargin = VerticalMarginPoints / staffSpace;
        TimeSignature firstMeter = score.Measures[system.Range.StartIndex].TimeSignature;
        SystemHeaderLayout header = _horizontalSpacer.BuildHeader(_metadata, "gClef",
            new KeySignature(0), firstMeter, _style);
        int staffCount = CountStaves(score);
        ImmutableArray<DrawingPrimitive>.Builder primitives = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        StaffElementPlacer placer = new(_metadata, _style);
        EventId staffLineId = new(Guid.Empty);
        VerticalLayoutResult verticalLayout = BuildVerticalLayout(layout.Systems,
            staffCount, pageHeight, topMargin);
        int pageNumber = verticalLayout.Systems[systemIndex].PageNumber;

        for (int pageSystemIndex = 0; pageSystemIndex < layout.Systems.Length; pageSystemIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SystemVerticalPlacement placement = verticalLayout.Systems[pageSystemIndex];
            if (placement.PageNumber != pageNumber)
            {
                continue;
            }

            SystemLine pageSystem = layout.Systems[pageSystemIndex];
            ImmutableArray<double> measureWidths = GetDisplayMeasureWidths(score, layout, pageSystem);
            SystemHeaderLayout pageHeader = _horizontalSpacer.BuildHeader(_metadata, "gClef",
                new KeySignature(0), score.Measures[pageSystem.Range.StartIndex].TimeSignature, _style);
            double musicStartX = leftMargin + pageHeader.MusicStartX;
            double systemWidth = Sum(measureWidths);
            double musicEndX = musicStartX + systemWidth;
            for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double staffTop = placement.StaffTops[staffIndex];
                primitives.AddRange(placer.PlaceStaffLines(staffLineId, leftMargin,
                    musicEndX, staffTop));
                AddHeader(primitives, pageHeader, leftMargin, staffTop);
                AddBarline(primitives, staffLineId,
                    musicStartX - _style.MinimumRhythmicGap, staffTop,
                    _style.StaffLineThickness);

                double measureStartX = musicStartX;
                for (int localMeasure = 0; localMeasure < pageSystem.Range.Count; localMeasure++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int scoreMeasureIndex = pageSystem.Range.StartIndex + localMeasure;
                    double measureWidth = measureWidths[localMeasure];
                    if (score.Content.TryGetValue(new StaffMeasureKey(staffIndex, scoreMeasureIndex),
                        out StaffMeasure? staffMeasure))
                    {
                        AddStaffMeasure(primitives, placer, score, staffMeasure,
                            scoreMeasureIndex, measureStartX, measureWidth, staffTop,
                            cancellationToken);
                    }

                    measureStartX += measureWidth;
                    AddBarline(primitives, staffLineId, measureStartX, staffTop,
                        _style.StaffLineThickness);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        Page page = new(pageNumber, pageWidth, pageHeight, primitives.ToImmutable());
        DisplayPoint? cursorLocation = cursor is EngravingCursor inputCursor
            ? LocateCursor(score, system, verticalLayout.Systems[systemIndex], inputCursor,
                leftMargin + header.MusicStartX,
                GetDisplayMeasureWidths(score, layout, system))
            : null;
        return new ScorePageComposition(page, staffSpace, systemIndex, system.Range, cursorLocation);
    }

    private void AddHeader(ImmutableArray<DrawingPrimitive>.Builder primitives,
        SystemHeaderLayout header, double leftMargin, double staffTop)
    {
        foreach (HeaderSymbol symbol in header.Symbols)
        {
            double y = symbol.Part switch
            {
                HeaderPart.Clef => staffTop + 3.5,
                HeaderPart.MeterNumerator => staffTop + 1.6,
                HeaderPart.MeterDenominator => staffTop + 3.6,
                _ => staffTop + 1.5,
            };
            AddGlyph(primitives, new ElementId(Guid.Empty), symbol.GlyphName,
                leftMargin + symbol.X, y);
        }
    }

    private void AddStaffMeasure(ImmutableArray<DrawingPrimitive>.Builder primitives,
        StaffElementPlacer placer, Score score, StaffMeasure staffMeasure, int measureIndex,
        double measureStartX, double measureWidth, double staffTop,
        CancellationToken cancellationToken)
    {
        foreach (Voice voice in staffMeasure.Voices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (MusicEvent musicEvent in voice.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double onset = (double)musicEvent.Onset.Num / musicEvent.Onset.Den;
                double barLength = (double)score.Measures[measureIndex].TimeSignature.Length.Num /
                    score.Measures[measureIndex].TimeSignature.Length.Den;
                double x = measureStartX + measureWidth * onset / barLength;
                if (musicEvent is Rest rest)
                {
                    primitives.AddRange(placer.PlaceRest(rest.Id, rest.Duration, x, staffTop));
                }
                else if (musicEvent is Chord chord)
                {
                    for (int noteIndex = 0; noteIndex < chord.Notes.Length; noteIndex++)
                    {
                        Note note = chord.Notes[noteIndex];
                        AccidentalMark accidental = note.Pitch.Alter switch
                        {
                            -2 => AccidentalMark.DoubleFlat,
                            -1 => AccidentalMark.Flat,
                            1 => AccidentalMark.Sharp,
                            2 => AccidentalMark.DoubleSharp,
                            _ => AccidentalMark.None,
                        };
                        double chordOffset = chord.Notes.Length <= 1 ? 0 : noteIndex * 0.75;
                        primitives.AddRange(placer.PlaceNote(chord.Id, note.Pitch,
                            chord.Duration, accidental, x + chordOffset, staffTop));
                    }
                }
            }
        }
    }

    private void AddGlyph(ImmutableArray<DrawingPrimitive>.Builder primitives,
        ElementId elementId, string glyphName, double x, double y)
    {
        SmuflBoundingBox box = _metadata.GetBoundingBox(glyphName);
        DisplayBox bounds = new(x + box.SouthWest.X, y - box.NorthEast.Y,
            box.NorthEast.X - box.SouthWest.X,
            box.NorthEast.Y - box.SouthWest.Y);
        primitives.Add(new DisplayGlyph(elementId, bounds,
            _metadata.GetGlyphCodepoint(glyphName), new DisplayPoint(x, y), 4));
    }

    private static void AddBarline(ImmutableArray<DrawingPrimitive>.Builder primitives,
        EventId id, double x, double staffTop, double thickness)
    {
        double top = staffTop;
        double bottom = staffTop + StaffHeightSpaces;
        primitives.Add(new DisplayLine(new ElementId(id.Value),
            new DisplayBox(x - thickness / 2, top, thickness, bottom - top),
            new DisplayPoint(x, top), new DisplayPoint(x, bottom), thickness));
    }

    private static int CountStaves(Score score)
    {
        int count = 0;
        foreach (Instrument instrument in score.Instruments)
        {
            count = checked(count + instrument.Staves.Length);
        }

        return count;
    }

    private static int FindSystem(ImmutableArray<SystemLine> systems, int measureIndex)
    {
        for (int index = 0; index < systems.Length; index++)
        {
            SystemLineMeasureRange range = systems[index].Range;
            if (measureIndex >= range.StartIndex && measureIndex < range.StartIndex + range.Count)
            {
                return index;
            }
        }

        throw new InvalidOperationException("The score layout does not contain the requested measure.");
    }

    private static DisplayPoint? LocateCursor(Score score, SystemLine system,
        SystemVerticalPlacement placement, EngravingCursor cursor, double musicStartX,
        ImmutableArray<double> measureWidths)
    {
        if (cursor.StaffIndex < 0 || cursor.StaffIndex >= CountStaves(score) ||
            cursor.Position < Fraction.Zero)
        {
            return null;
        }

        Fraction measureStart = Fraction.Zero;
        int cursorMeasureIndex = score.Measures.Length - 1;
        Fraction localPosition = Fraction.Zero;
        for (int index = 0; index < score.Measures.Length; index++)
        {
            Fraction measureLength = score.Measures[index].TimeSignature.Length;
            Fraction measureEnd = measureStart + measureLength;
            if (cursor.Position < measureEnd || index == score.Measures.Length - 1)
            {
                cursorMeasureIndex = index;
                localPosition = cursor.Position - measureStart;
                if (localPosition < Fraction.Zero)
                {
                    localPosition = Fraction.Zero;
                }
                else if (localPosition > measureLength)
                {
                    localPosition = measureLength;
                }

                break;
            }

            measureStart = measureEnd;
        }

        if (cursorMeasureIndex < system.Range.StartIndex ||
            cursorMeasureIndex >= system.Range.StartIndex + system.Range.Count)
        {
            return null;
        }

        int localMeasureIndex = cursorMeasureIndex - system.Range.StartIndex;
        double x = musicStartX;
        for (int index = 0; index < localMeasureIndex; index++)
        {
            x += measureWidths[index];
        }

        Fraction currentMeasureLength = score.Measures[cursorMeasureIndex].TimeSignature.Length;
        double position = (double)localPosition.Num / localPosition.Den;
        double measureLengthValue = (double)currentMeasureLength.Num / currentMeasureLength.Den;
        x += measureWidths[localMeasureIndex] * position / measureLengthValue;
        double y = placement.StaffTops[cursor.StaffIndex] + 2;
        return new DisplayPoint(x, y);
    }

    private ImmutableArray<double> GetDisplayMeasureWidths(Score score, ScoreLayoutResult layout,
        SystemLine system)
    {
        if (layout.Systems.Length != 1 || system.NaturalWidth >= GetAvailableWidth(score))
        {
            return system.MeasureWidths;
        }

        double elasticity = 0;
        for (int index = 0; index < system.Range.Count; index++)
        {
            elasticity += layout.MeasureWidths[system.Range.StartIndex + index].Elasticity;
        }

        if (elasticity <= 0)
        {
            return system.MeasureWidths;
        }

        double additionalWidth = GetAvailableWidth(score) - system.NaturalWidth;
        ImmutableArray<double>.Builder widths =
            ImmutableArray.CreateBuilder<double>(system.Range.Count);
        for (int index = 0; index < system.Range.Count; index++)
        {
            double measureElasticity = layout.MeasureWidths[system.Range.StartIndex + index].Elasticity;
            widths.Add(system.MeasureWidths[index] + additionalWidth * measureElasticity / elasticity);
        }

        return widths.MoveToImmutable();
    }

    private static double Sum(ImmutableArray<double> values)
    {
        double total = 0;
        for (int index = 0; index < values.Length; index++)
        {
            total += values[index];
        }

        return total;
    }

    private static VerticalLayoutResult BuildVerticalLayout(ImmutableArray<SystemLine> systems,
        int staffCount, double pageHeight, double pageMargin)
    {
        ImmutableArray<StaffSkyline>.Builder staves = ImmutableArray.CreateBuilder<StaffSkyline>(staffCount);
        for (int index = 0; index < staffCount; index++)
        {
            staves.Add(new StaffSkyline(0.5, 0.5));
        }

        VerticalSystem verticalSystem = new(staves.MoveToImmutable(),
            MinimumStaffGap: 1.5,
            SkylineClearance: 1,
            LeadingSpace: 1,
            TrailingSpace: 1);
        VerticalSystem[] verticalSystems = new VerticalSystem[systems.Length];
        Array.Fill(verticalSystems, verticalSystem);
        return new VerticalPageLayouter().Layout(verticalSystems, pageHeight,
            pageMargin, pageMargin, systemGap: 2);
    }
}
