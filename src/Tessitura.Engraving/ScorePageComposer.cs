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
    private readonly object _annotationCacheGate = new();
    private readonly object _skylineCacheGate = new();
    private ImmutableArray<Attachment> _cachedAnnotationList;
    private Dictionary<EventId, List<Attachment>>? _cachedAttachmentIndex;
    private ImmutableArray<LyricAnchor> _cachedLyricAnchors;
    private int _cachedLyricAttachmentCount;
    private ImmutableArray<Spanner> _cachedSpannerDefinitions;
    private Dictionary<EventId, SpannerLocation>? _cachedSpannerLocations;
    private int _cachedSpannerEndpointCount;
    private bool _cachedHasCrossStaffSpanners;
    private Score? _cachedSkylineSource;
    private Score? _cachedSkylineDisplayScore;
    private ScoreLayoutResult? _cachedSkylineLayout;
    private ImmutableArray<int> _cachedSkylinePartIndices;
    private PitchDisplayMode _cachedSkylinePitchMode;
    private StaffSkyline[][]? _cachedSkylineExtents;
    private int _skylineSystemMeasureCount;

    /// <summary>Creates a score-page composer for one music font and engraving style.</summary>
    /// <param name="metadata">The selected SMuFL font metrics and glyph map.</param>
    /// <param name="style">The current engraving style.</param>
    public ScorePageComposer(SmuflMetadata metadata, Style style)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _style = style ?? throw new ArgumentNullException(nameof(style));
    }

    /// <summary>Gets how many systems have had their skyline measured by this composer instance.</summary>
    public int SkylineSystemMeasureCount => Volatile.Read(ref _skylineSystemMeasureCount);

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
        if (score.AttachmentList.Length > 0 || score.SpannerList.Length > 0)
        {
            // Keep text-rich compact scores legible; large ensembles use measured skylines instead of a fixed reserve.
            staffPitch += staffCount <= 4 ? 22 : 3;
        }

        double maximumStaffSpace = (PageHeightPoints - 2 * VerticalMarginPoints) /
            (staffCount * staffPitch + StaffGapSpaces);
        return Math.Min(RequestedStaffSpacePoints, maximumStaffSpace);
    }

    /// <summary>Gets the music width available to the system breaker for the score.</summary>
    /// <param name="score">The immutable score snapshot.</param>
    /// <param name="pitchDisplayMode">Whether headers should use written or concert key signatures.</param>
    /// <returns>The system's usable width in staff spaces after its header.</returns>
    public double GetAvailableWidth(Score score, PitchDisplayMode pitchDisplayMode = PitchDisplayMode.Written)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (score.Measures.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A score must contain at least one measure.", nameof(score));
        }

        double staffSpace = GetStaffSpacePoints(score);
        double headerWidth = 0;
        int headerStaffCount = CountStaves(score);
        Dictionary<HeaderCacheKey, double> headerWidths = [];
        for (int measureIndex = 0; measureIndex < score.Measures.Length; measureIndex++)
        {
            for (int staffIndex = 0; staffIndex < headerStaffCount; staffIndex++)
            {
                string clefGlyph = ClefGlyphName(GetStaffClef(score, staffIndex));
                KeySignature keySignature = ScorePitchView.GetKeySignature(score, staffIndex,
                    measureIndex, pitchDisplayMode);
                TimeSignature timeSignature = score.Measures[measureIndex].TimeSignature;
                HeaderCacheKey key = new(clefGlyph, keySignature, timeSignature);
                if (!headerWidths.TryGetValue(key, out double currentWidth))
                {
                    currentWidth = _horizontalSpacer.BuildHeader(_metadata, clefGlyph,
                        keySignature, timeSignature, _style).MusicStartX;
                    headerWidths.Add(key, currentWidth);
                }

                headerWidth = Math.Max(headerWidth, currentWidth);
            }
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
    /// <param name="pitchDisplayMode">Whether to display the score in written or concert pitch.</param>
    /// <returns>The display page and its score-system location.</returns>
    public ScorePageComposition Compose(Score score, ScoreLayoutResult layout, int measureIndex,
        EngravingCursor? cursor = null, CancellationToken cancellationToken = default,
        PitchDisplayMode pitchDisplayMode = PitchDisplayMode.Written)
    {
        ArgumentNullException.ThrowIfNull(score);
        Score displayScore = ScorePitchView.Project(score, pitchDisplayMode);
        return ComposeCore(displayScore, layout, measureIndex, cursor, cancellationToken,
            ImmutableArray<MultiMeasureRestGroup>.Empty, pitchDisplayMode,
            cacheSource: score);
    }

    /// <summary>Composes every system into one tall page without page breaks.</summary>
    /// <param name="score">The immutable score snapshot.</param>
    /// <param name="layout">The measured and system-broken score.</param>
    /// <param name="measureIndex">The measure whose system should receive the cursor.</param>
    /// <param name="cursor">The optional musical cursor to place in the continuous score.</param>
    /// <param name="cancellationToken">Cancels composition without returning partial primitives.</param>
    /// <param name="pitchDisplayMode">Whether to display the score in written or concert pitch.</param>
    /// <returns>A continuous display list containing every score system.</returns>
    public ScorePageComposition ComposeContinuous(Score score, ScoreLayoutResult layout, int measureIndex,
        EngravingCursor? cursor = null, CancellationToken cancellationToken = default,
        PitchDisplayMode pitchDisplayMode = PitchDisplayMode.Written)
    {
        ArgumentNullException.ThrowIfNull(score);
        Score displayScore = ScorePitchView.Project(score, pitchDisplayMode);
        return ComposeCore(displayScore, layout, measureIndex, cursor, cancellationToken,
            ImmutableArray<MultiMeasureRestGroup>.Empty, pitchDisplayMode, continuous: true,
            cacheSource: score);
    }

    /// <summary>Composes a linked instrument part and groups its consecutive full-measure rests.</summary>
    /// <param name="sourceScore">The latest master score snapshot.</param>
    /// <param name="part">The linked part view to compose.</param>
    /// <param name="layout">A layout calculated for the projected part score.</param>
    /// <param name="measureIndex">The part measure whose system should be composed.</param>
    /// <param name="pitchDisplayMode">Whether the part is written or shown at concert pitch.</param>
    /// <param name="cursor">The optional musical cursor to place on the page.</param>
    /// <param name="cancellationToken">Cancels composition without returning partial primitives.</param>
    /// <returns>The part's display page and system location.</returns>
    public ScorePageComposition ComposePart(Score sourceScore, ScorePartView part,
        ScoreLayoutResult layout, int measureIndex,
        PitchDisplayMode pitchDisplayMode = PitchDisplayMode.Written, EngravingCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceScore);
        ArgumentNullException.ThrowIfNull(part);
        Score partScore = ScorePartProjector.Project(sourceScore, part, pitchDisplayMode);
        if (partScore.Instruments.Length != 1)
        {
            throw new ArgumentException("A part layout must contain exactly one instrument.", nameof(part));
        }

        return ComposeCore(partScore, layout, measureIndex, cursor, cancellationToken,
            MultiMeasureRestGrouper.FindGroups(partScore), pitchDisplayMode,
            cacheSource: sourceScore, cachePartIndices: part.InstrumentIndices);
    }

    private ScorePageComposition ComposeCore(Score score, ScoreLayoutResult layout, int measureIndex,
        EngravingCursor? cursor, CancellationToken cancellationToken,
        ImmutableArray<MultiMeasureRestGroup> multiMeasureRestGroups, PitchDisplayMode pitchDisplayMode,
        bool continuous = false, Score? cacheSource = null,
        ImmutableArray<int> cachePartIndices = default)
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
        double pageHeight = continuous
            ? (PageHeightPoints + 2 * VerticalMarginPoints) * Math.Max(1, layout.Systems.Length) / staffSpace
            : PageHeightPoints / staffSpace;
        double leftMargin = HorizontalMarginPoints / staffSpace;
        double topMargin = VerticalMarginPoints / staffSpace;
        (Dictionary<EventId, List<Attachment>> attachmentIndex,
            ImmutableArray<LyricAnchor> lyricAnchors) = GetAnnotationData(score);
        TimeSignature firstMeter = score.Measures[system.Range.StartIndex].TimeSignature;
        int staffCount = CountStaves(score);
        SystemHeaderLayout header = BuildSystemHeader(score, system.Range.StartIndex, staffCount, pitchDisplayMode);
        ImmutableArray<DrawingPrimitive>.Builder primitives = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        StaffElementPlacer placer = new(_metadata, _style);
        EventId staffLineId = new(Guid.Empty);
        VerticalLayoutResult verticalLayout = BuildVerticalLayout(layout.Systems,
            staffCount, pageHeight, topMargin, null);
        if (score.AttachmentList.Length > 0 || score.SpannerList.Length > 0)
        {
            // Marks push their neighbours apart: measure how far each system's notation really reaches above and
            // below every staff, then space the staves and systems against those skylines.
            try
            {
                verticalLayout = BuildVerticalLayout(layout.Systems, staffCount, pageHeight, topMargin,
                    GetMeasureExtents(score, cacheSource ?? score, layout, verticalLayout, staffCount, placer,
                        leftMargin, attachmentIndex, lyricAnchors, cancellationToken, pitchDisplayMode,
                        cachePartIndices));
            }
            catch (InvalidOperationException)
            {
                // A system with all its marks would not fit on a page: keep the compact spacing rather than fail.
            }
        }

        int pageNumber = verticalLayout.Systems[systemIndex].PageNumber;
        SystemState state = new(attachmentIndex, lyricAnchors);

        for (int pageSystemIndex = 0; pageSystemIndex < layout.Systems.Length; pageSystemIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SystemVerticalPlacement placement = verticalLayout.Systems[pageSystemIndex];
            if (placement.PageNumber != pageNumber)
            {
                continue;
            }

            DrawSystem(primitives, score, layout, pageSystemIndex, placement, staffCount, placer, state,
                leftMargin, cancellationToken, multiMeasureRestGroups, pitchDisplayMode);
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

    // Draws one system (all its staves, marks and spanners) into the list, returning nothing: the caller owns the list.
    private void DrawSystem(ImmutableArray<DrawingPrimitive>.Builder primitives, Score score, ScoreLayoutResult layout,
        int pageSystemIndex, SystemVerticalPlacement placement, int staffCount, StaffElementPlacer placer, SystemState state,
        double leftMargin, CancellationToken cancellationToken,
        ImmutableArray<MultiMeasureRestGroup> multiMeasureRestGroups, PitchDisplayMode pitchDisplayMode,
        int? staffFilter = null)
    {
        EventId staffLineId = new(Guid.Empty);
        SystemLine pageSystem = layout.Systems[pageSystemIndex];
        ImmutableArray<double> measureWidths = GetDisplayMeasureWidths(score, layout, pageSystem);
        ImmutableArray<MultiMeasureRestSegment> restSegments = BuildMultiMeasureRestSegments(
            multiMeasureRestGroups, pageSystem.Range);
        int[] restSegmentByLocalMeasure = new int[pageSystem.Range.Count];
        Array.Fill(restSegmentByLocalMeasure, -1);
        for (int segmentIndex = 0; segmentIndex < restSegments.Length; segmentIndex++)
        {
            MultiMeasureRestSegment segment = restSegments[segmentIndex];
            for (int offset = 0; offset < segment.MeasureCount; offset++)
            {
                restSegmentByLocalMeasure[segment.StartLocalMeasure + offset] = segmentIndex;
            }
        }

        SystemHeaderLayout pageHeader = BuildSystemHeader(score, pageSystem.Range.StartIndex, staffCount,
            pitchDisplayMode);
        double musicStartX = leftMargin + pageHeader.MusicStartX;
        double systemWidth = Sum(measureWidths);
        double musicEndX = musicStartX + systemWidth;
        state.Geometry.Clear();
        state.EventX.Clear();
        state.Annotations.Clear();
        state.LyricTexts.Clear();
        int systemFirstPrimitive = primitives.Count;
        for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
        {
            if (staffFilter.HasValue && staffFilter.Value != staffIndex)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            double staffTop = placement.StaffTops[staffIndex];
            int staffStart = primitives.Count;
            primitives.AddRange(placer.PlaceStaffLines(staffLineId, leftMargin,
                musicEndX, staffTop));
            Clef staffClef = GetStaffClef(score, staffIndex);
            SystemHeaderLayout staffHeader = _horizontalSpacer.BuildHeader(_metadata,
                ClefGlyphName(staffClef), ScorePitchView.GetKeySignature(score, staffIndex,
                    pageSystem.Range.StartIndex, pitchDisplayMode),
                score.Measures[pageSystem.Range.StartIndex].TimeSignature, _style);
            AddHeader(primitives, staffHeader, leftMargin, staffTop, staffClef,
                ScorePitchView.GetKeySignature(score, staffIndex, pageSystem.Range.StartIndex, pitchDisplayMode));
            AddMeasureBoundary(primitives, score, staffLineId,
                pageSystem.Range.StartIndex, musicStartX - _style.MinimumRhythmicGap,
                staffTop, _style.StaffLineThickness);

            double measureStartX = musicStartX;
            for (int localMeasure = 0; localMeasure < pageSystem.Range.Count; localMeasure++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int scoreMeasureIndex = pageSystem.Range.StartIndex + localMeasure;
                double measureWidth = measureWidths[localMeasure];
                int restSegmentIndex = restSegmentByLocalMeasure[localMeasure];
                if (restSegmentIndex >= 0)
                {
                    MultiMeasureRestSegment segment = restSegments[restSegmentIndex];
                    if (localMeasure == segment.StartLocalMeasure)
                    {
                        DrawMultiMeasureRest(primitives, segment, measureWidths,
                            measureStartX, staffTop);
                    }
                }
                else if (score.Content.TryGetValue(new StaffMeasureKey(staffIndex, scoreMeasureIndex),
                    out StaffMeasure? staffMeasure))
                {
                    AddStaffMeasure(primitives, placer, score, staffMeasure,
                        scoreMeasureIndex, staffClef, measureStartX, measureWidth, staffTop,
                        state, staffIndex, cancellationToken,
                        ScorePitchView.GetKeySignature(score, staffIndex, scoreMeasureIndex, pitchDisplayMode));
                }

                measureStartX += measureWidth;
                bool restGroupContinues = restSegmentIndex >= 0 &&
                    localMeasure < restSegments[restSegmentIndex].StartLocalMeasure +
                    restSegments[restSegmentIndex].MeasureCount - 1;
                if (!restGroupContinues)
                {
                    AddMeasureBoundary(primitives, score, staffLineId,
                        scoreMeasureIndex + 1, measureStartX, staffTop,
                        _style.StaffLineThickness);
                }
            }

            for (int tagged = staffStart; tagged < primitives.Count; tagged++)
            {
                state.StaffByPrimitive[primitives[tagged]] = staffIndex;
            }
        }

        AddLines(primitives, score, state, musicStartX, musicEndX);
        ResolveAnnotations(primitives, state, systemFirstPrimitive);
        DrawLyricConnectors(primitives, score, pageSystem, placement, state, musicStartX, musicEndX);
        AddSlurs(primitives, score, state, systemFirstPrimitive, musicStartX, musicEndX);
        if (!staffFilter.HasValue || staffFilter.Value == 0)
        {
            DrawRepeatAnnotations(primitives, score, pageSystem, placement,
                measureWidths, musicStartX, staffCount);
        }
    }

    private static ImmutableArray<MultiMeasureRestSegment> BuildMultiMeasureRestSegments(
        ImmutableArray<MultiMeasureRestGroup> groups, SystemLineMeasureRange range)
    {
        ImmutableArray<MultiMeasureRestSegment>.Builder segments =
            ImmutableArray.CreateBuilder<MultiMeasureRestSegment>();
        int systemEnd = range.StartIndex + range.Count;
        foreach (MultiMeasureRestGroup group in groups)
        {
            int groupEnd = group.StartMeasure + group.MeasureCount;
            int segmentStart = Math.Max(group.StartMeasure, range.StartIndex);
            int segmentEnd = Math.Min(groupEnd, systemEnd);
            int segmentCount = segmentEnd - segmentStart;
            if (segmentCount >= 2)
            {
                segments.Add(new MultiMeasureRestSegment(segmentStart - range.StartIndex,
                    segmentCount, group.MeasureCount, segmentStart == group.StartMeasure));
            }
        }

        return segments.ToImmutable();
    }

    private void DrawMultiMeasureRest(ImmutableArray<DrawingPrimitive>.Builder primitives,
        MultiMeasureRestSegment segment, ImmutableArray<double> measureWidths, double startX, double staffTop)
    {
        double width = 0;
        for (int index = 0; index < segment.MeasureCount; index++)
        {
            width += measureWidths[segment.StartLocalMeasure + index];
        }

        double left = startX + 0.45;
        double right = startX + width - 0.45;
        double centerY = staffTop + 2;
        double thickness = Math.Max(0.3, _style.StaffLineThickness * 3);
        // Behind Bars, Rests > Multi-bar rests: a centered count and continuous bar represent the silent measures.
        AddLine(primitives, new ElementId(Guid.Empty), left, centerY, right, centerY, thickness);
        AddLine(primitives, new ElementId(Guid.Empty), left, centerY - 0.35, left, centerY + 0.35, thickness);
        AddLine(primitives, new ElementId(Guid.Empty), right, centerY - 0.35, right, centerY + 0.35, thickness);
        if (segment.ShowCount)
        {
            string label = segment.TotalMeasureCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            double labelX = startX + (width - label.Length * 0.65) / 2;
            AddText(primitives, new ElementId(Guid.Empty), label, labelX, staffTop + 1.25, 1.2);
        }
    }

    private void DrawRepeatAnnotations(ImmutableArray<DrawingPrimitive>.Builder primitives,
        Score score, SystemLine system, SystemVerticalPlacement placement,
        ImmutableArray<double> measureWidths, double musicStartX, int staffCount)
    {
        if (staffCount == 0)
        {
            return;
        }

        double staffTop = placement.StaffTops[0];
        double x = musicStartX;
        for (int local = 0; local < system.Range.Count;)
        {
            int measureIndex = system.Range.StartIndex + local;
            RepeatInfo? repeat = score.Measures[measureIndex].Repeat;
            double measureEnd = x + measureWidths[local];
            if (repeat is not null)
            {
                if (repeat.Target is RepeatTarget.Segno or RepeatTarget.Coda)
                {
                    string glyph = repeat.Target == RepeatTarget.Segno ? "segno" : "coda";
                    AddGlyph(primitives, new ElementId(Guid.Empty), glyph,
                        x + 0.2, staffTop - 0.4);
                }

                string jumpLabel = RepeatJumpLabel(repeat.Jump);
                if (jumpLabel.Length > 0)
                {
                    AddText(primitives, new ElementId(Guid.Empty), jumpLabel,
                        Math.Max(x + 0.2, measureEnd - jumpLabel.Length * 1.2), staffTop - 0.4, 1.8);
                }
            }

            if (repeat is not null && !repeat.Endings.IsEmpty)
            {
                ImmutableArray<int> endingNumbers = repeat.Endings;
                int groupEnd = local + 1;
                double groupEndX = measureEnd;
                while (groupEnd < system.Range.Count)
                {
                    RepeatInfo? next = score.Measures[system.Range.StartIndex + groupEnd].Repeat;
                    if (next is null || !next.Endings.AsSpan().SequenceEqual(endingNumbers.AsSpan()))
                    {
                        break;
                    }

                    groupEndX += measureWidths[groupEnd];
                    groupEnd++;
                }

                // SMuFL engravingDefaults.repeatEndingLineThickness sets the volta-bracket stroke.
                double bracketThickness = _metadata.GetEngravingDefault("repeatEndingLineThickness");
                double bracketY = staffTop - 1.1;
                AddLine(primitives, new ElementId(Guid.Empty), x, bracketY,
                    groupEndX, bracketY, bracketThickness);
                AddLine(primitives, new ElementId(Guid.Empty), x, bracketY,
                    x, staffTop - 0.25, bracketThickness);
                AddLine(primitives, new ElementId(Guid.Empty), groupEndX, bracketY,
                    groupEndX, staffTop - 0.25, bracketThickness);
                System.Text.StringBuilder labelBuilder = new();
                for (int endingIndex = 0; endingIndex < endingNumbers.Length; endingIndex++)
                {
                    if (endingIndex > 0)
                    {
                        labelBuilder.Append(", ");
                    }

                    labelBuilder.Append(endingNumbers[endingIndex]);
                    labelBuilder.Append('.');
                }

                string label = labelBuilder.ToString();
                AddText(primitives, new ElementId(Guid.Empty), label,
                    x + 0.2, staffTop - 1.25, 1.8);
                x = groupEndX;
                local = groupEnd;
                continue;
            }

            x = measureEnd;
            local++;
        }
    }

    private void DrawLyricConnectors(ImmutableArray<DrawingPrimitive>.Builder primitives,
        Score score, SystemLine system, SystemVerticalPlacement placement, SystemState state,
        double musicStartX, double musicEndX)
    {
        // Behind Bars, Text > Lyrics: hyphens join syllables and extender lines sustain a syllable.
        ImmutableArray<LyricAnchor> anchors = state.LyricAnchors;

        int systemStart = system.Range.StartIndex;
        int systemEnd = systemStart + system.Range.Count;
        // SMuFL engravingDefaults.lyricLineThickness controls lyric extender strokes.
        double extenderThickness = _metadata.GetEngravingDefault("lyricLineThickness");
        for (int index = 0; index < anchors.Length; index++)
        {
            LyricAnchor anchor = anchors[index];
            LyricAttachment lyric = anchor.Lyric;
            if ((lyric.Syllabic is LyricSyllabic.Begin or LyricSyllabic.Middle) &&
                state.LyricTexts.TryGetValue(new LyricKey(anchor.Event, lyric.Verse), out DisplayLists.Text? currentText))
            {
                LyricAnchor? nextSyllable = FindLyricAnchor(anchors, index + 1, anchor, lyric.Verse,
                    static candidate => !string.IsNullOrEmpty(candidate.Text));
                double hyphenX;
                if (nextSyllable is LyricAnchor next &&
                    state.LyricTexts.TryGetValue(new LyricKey(next.Event, lyric.Verse), out DisplayLists.Text? nextText))
                {
                    hyphenX = (currentText.Bounds.X + currentText.Bounds.Width + nextText.Bounds.X) / 2 - 0.35;
                }
                else
                {
                    hyphenX = musicEndX - 0.8;
                }

                AddText(primitives, new ElementId(anchor.Event.Value), "-", hyphenX,
                    currentText.Origin.Y, 2.2);
                state.StaffByPrimitive[primitives[^1]] = anchor.Staff;
            }

            if (lyric.Extender != LyricExtender.Start)
            {
                continue;
            }

            LyricAnchor? stop = FindLyricAnchor(anchors, index + 1, anchor, lyric.Verse,
                static candidate => candidate.Extender == LyricExtender.Stop);
            if (stop is not LyricAnchor end || anchor.MeasureIndex >= systemEnd || end.MeasureIndex < systemStart)
            {
                continue;
            }

            bool hasStart = anchor.MeasureIndex >= systemStart;
            bool hasEnd = end.MeasureIndex < systemEnd;
            double lineStart;
            double baseline;
            if (hasStart && state.LyricTexts.TryGetValue(new LyricKey(anchor.Event, lyric.Verse), out DisplayLists.Text? startText))
            {
                lineStart = startText.Bounds.X + startText.Bounds.Width + 0.25;
                baseline = startText.Origin.Y;
            }
            else
            {
                lineStart = musicStartX;
                baseline = placement.StaffTops[anchor.Staff] + 10.5 + (lyric.Verse - 1) * 3.2;
            }

            double lineEnd = hasEnd && state.EventX.TryGetValue(end.Event, out double endX)
                ? endX - 0.25 : musicEndX;
            double lineY = baseline + 0.65;
            if (lineEnd - lineStart < 0.35)
            {
                continue;
            }

            DisplayLine extenderLine = new(new ElementId(anchor.Event.Value),
                new DisplayBox(lineStart, lineY - extenderThickness / 2,
                    lineEnd - lineStart, extenderThickness),
                new DisplayPoint(lineStart, lineY), new DisplayPoint(lineEnd, lineY), extenderThickness);
            primitives.Add(extenderLine);
            state.StaffByPrimitive[extenderLine] = anchor.Staff;
        }
    }

    private static LyricAnchor? FindLyricAnchor(ImmutableArray<LyricAnchor> anchors, int start,
        LyricAnchor source, int verse, Func<LyricAttachment, bool> predicate)
    {
        for (int index = start; index < anchors.Length; index++)
        {
            LyricAnchor candidate = anchors[index];
            if (candidate.Staff == source.Staff && candidate.Voice == source.Voice &&
                candidate.Lyric.Verse == verse && predicate(candidate.Lyric))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string RepeatJumpLabel(RepeatJump jump) => jump switch
    {
        RepeatJump.DaCapo => "D.C.",
        RepeatJump.DaCapoAlFine => "D.C. al Fine",
        RepeatJump.DaCapoAlCoda => "D.C. al Coda",
        RepeatJump.DalSegno => "D.S.",
        RepeatJump.DalSegnoAlFine => "D.S. al Fine",
        RepeatJump.DalSegnoAlCoda => "D.S. al Coda",
        RepeatJump.ToCoda => "To Coda",
        RepeatJump.Fine => "Fine",
        _ => string.Empty,
    };

    private static void AddLine(ImmutableArray<DrawingPrimitive>.Builder primitives,
        ElementId id, double startX, double startY, double endX, double endY, double thickness)
    {
        DisplayBox bounds = new(Math.Min(startX, endX) - thickness / 2,
            Math.Min(startY, endY) - thickness / 2,
            Math.Abs(endX - startX) + thickness,
            Math.Abs(endY - startY) + thickness);
        primitives.Add(new DisplayLine(id, bounds, new DisplayPoint(startX, startY),
            new DisplayPoint(endX, endY), thickness));
    }

    private SystemHeaderLayout BuildSystemHeader(Score score, int measureIndex, int staffCount,
        PitchDisplayMode pitchDisplayMode)
    {
        // All staves in a system share one music start, so use the widest header.
        SystemHeaderLayout widest = default!;
        for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
        {
            SystemHeaderLayout current = _horizontalSpacer.BuildHeader(_metadata,
                ClefGlyphName(GetStaffClef(score, staffIndex)),
                ScorePitchView.GetKeySignature(score, staffIndex, measureIndex, pitchDisplayMode),
                score.Measures[measureIndex].TimeSignature, _style);
            if (widest is null || current.MusicStartX > widest.MusicStartX)
            {
                widest = current;
            }
        }

        return widest;
    }

    private static Clef GetStaffClef(Score score, int staffIndex)
    {
        int remaining = staffIndex;
        foreach (Instrument instrument in score.Instruments)
        {
            if (remaining < instrument.Staves.Length)
            {
                return instrument.Staves[remaining].InitialClef;
            }

            remaining -= instrument.Staves.Length;
        }

        return Clef.Treble;
    }

    private static string ClefGlyphName(Clef clef) => clef switch
    {
        Clef.Treble => "gClef",
        Clef.Bass => "fClef",
        Clef.Alto or Clef.Tenor => "cClef",
        _ => throw new ArgumentOutOfRangeException(nameof(clef)),
    };

    private void AddHeader(ImmutableArray<DrawingPrimitive>.Builder primitives,
        SystemHeaderLayout header, double leftMargin, double staffTop, Clef clef,
        KeySignature key)
    {
        int keyIndex = 0;
        foreach (HeaderSymbol symbol in header.Symbols)
        {
            double y = symbol.Part switch
            {
                // SMuFL clef origins sit on the line each clef names.
                HeaderPart.Clef => clef switch
                {
                    Clef.Treble => staffTop + 3.5,
                    Clef.Bass => staffTop + 1,
                    Clef.Alto => staffTop + 2,
                    _ => staffTop + 1,
                },
                HeaderPart.KeySignature =>
                    staffTop + 4 - KeySignaturePositions.Get(key.Fifths, keyIndex++, clef) * 0.5,
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
        Clef clef, double measureStartX, double measureWidth, double staffTop,
        SystemState articulations, int staffIndex, CancellationToken cancellationToken,
        KeySignature keySignature)
    {
        articulations.CurrentStaff = staffIndex;
        articulations.CurrentMeasureIndex = measureIndex;
        AccidentalMark[] marks = ResolveAccidentals(score.Measures[measureIndex], keySignature,
            measureIndex, staffMeasure,
            out (int Voice, EventId Event, int Note)[] order);
        PackColumns(articulations, staffMeasure, score.Measures[measureIndex].TimeSignature.Length, measureStartX, measureWidth,
            order, marks);
        bool manyVoices = staffMeasure.Voices.Length > 1;
        double headWidth = _metadata.GetBoundingBox("noteheadBlack").NorthEast.X;
        double barLength = (double)score.Measures[measureIndex].TimeSignature.Length.Num /
            score.Measures[measureIndex].TimeSignature.Length.Den;
        foreach (Voice voice in staffMeasure.Voices)
        {
            articulations.CurrentVoiceNumber = voice.Number;
            // Behind Bars, Multiple Voices: odd voices take up-stems, even voices down-stems.
            StemDirection? voiceStem = manyVoices
                ? (voice.Number % 2 == 1 ? StemDirection.Up : StemDirection.Down)
                : null;
            double restShift = !manyVoices ? 0 : voice.Number switch { 1 => -2, 2 => 2, 3 => -4, _ => 4 };
            cancellationToken.ThrowIfCancellationRequested();
            if (voice.Events.Length == 1 && voice.Events[0] is Rest fullBarRest &&
                fullBarRest.Onset == Fraction.Zero &&
                fullBarRest.Length == score.Measures[measureIndex].TimeSignature.Length)
            {
                // Behind Bars, Rests > Whole-bar rests: center the whole-rest symbol between the barlines.
                double centerX = measureStartX + measureWidth / 2;
                SmuflBoundingBox box = _metadata.GetBoundingBox("restWhole");
                double originX = centerX - (box.SouthWest.X + box.NorthEast.X) / 2;
                articulations.EventX[fullBarRest.Id] = centerX;
                AddAnnotations(primitives, fullBarRest.Id, centerX, staffTop, articulations);
                primitives.AddRange(placer.PlaceRest(fullBarRest.Id,
                    new Duration(NoteValue.Whole, 0), originX, staffTop, restShift));
                continue;
            }

            foreach (MusicEvent topLevel in voice.Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (topLevel is TupletGroup group)
                {
                    AddTuplet(primitives, placer, group, voice, staffMeasure, clef, measureStartX, measureWidth,
                        staffTop, barLength, order, marks, manyVoices, headWidth, voiceStem, restShift, articulations);
                    continue;
                }

                AddLeaf(primitives, placer, topLevel, topLevel.Onset, voice, staffMeasure, clef, measureStartX, measureWidth,
                    staffTop, barLength, order, marks, manyVoices, headWidth, voiceStem, restShift, articulations);
            }
        }
    }

    private void AddLeaf(ImmutableArray<DrawingPrimitive>.Builder primitives, StaffElementPlacer placer, MusicEvent leaf,
        Fraction position, Voice voice, StaffMeasure staffMeasure, Clef clef, double measureStartX, double measureWidth,
        double staffTop, double barLength, (int Voice, EventId Event, int Note)[] order, AccidentalMark[] marks,
        bool manyVoices, double headWidth, StemDirection? voiceStem, double restShift,
        SystemState articulations)
    {
        double x = articulations.ColumnX.TryGetValue(position, out double packed)
            ? packed
            : measureStartX + measureWidth * ((double)position.Num / position.Den) / barLength;
        articulations.EventX[leaf.Id] = x;
        AddAnnotations(primitives, leaf.Id, x, staffTop, articulations);
        if (leaf is Rest rest)
        {
            primitives.AddRange(placer.PlaceRest(rest.Id, rest.Duration, x, staffTop, restShift));
            return;
        }

        Chord chord = (Chord)leaf;
        RecordGeometry(articulations, chord, x + headWidth / 2, staffTop, clef, voiceStem);
        if (chord.Notes.Length > 1)
        {
            (Pitch Pitch, AccidentalMark Accidental)[] notes = new (Pitch, AccidentalMark)[chord.Notes.Length];
            for (int noteIndex = 0; noteIndex < notes.Length; noteIndex++)
            {
                AccidentalMark accidental = AccidentalMark.None;
                for (int i = 0; i < order.Length; i++)
                {
                    if (order[i].Voice == voice.Number && order[i].Event == chord.Id && order[i].Note == noteIndex)
                    {
                        accidental = marks[i];
                        break;
                    }
                }

                notes[noteIndex] = (chord.Notes[noteIndex].Pitch, accidental);
            }

            double shift = manyVoices && CollidesWithLowerVoice(staffMeasure, voice, chord, chord.Notes[0], clef) ? headWidth : 0;
            primitives.AddRange(placer.PlaceChord(chord.Id, notes, chord.Duration, x + shift, staffTop, clef, voiceStem));
            AddArticulations(primitives, placer, chord, x + shift + headWidth / 2, staffTop, clef, voiceStem, articulations);
            return;
        }

        for (int noteIndex = 0; noteIndex < chord.Notes.Length; noteIndex++)
        {
            Note note = chord.Notes[noteIndex];
            AccidentalMark accidental = AccidentalMark.None;
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i].Voice == voice.Number && order[i].Event == chord.Id && order[i].Note == noteIndex)
                {
                    accidental = marks[i];
                    break;
                }
            }

            double chordOffset = chord.Notes.Length <= 1 ? 0 : noteIndex * 0.75;
            if (manyVoices && CollidesWithLowerVoice(staffMeasure, voice, chord, note, clef))
            {
                chordOffset += headWidth; // Behind Bars, Multiple Voices: displace the clashing head
            }

            primitives.AddRange(placer.PlaceNote(chord.Id, note.Pitch,
                chord.Duration, accidental, x + chordOffset, staffTop, clef, voiceStem));
        }
    
        AddArticulations(primitives, placer, chord, x + headWidth / 2, staffTop, clef, voiceStem, articulations);
    }

    // Behind Bars, Spacing: proportional spacing is a starting point; a column never sits closer to the previous one
    // than their extents allow, so accidental columns, displaced heads and dots do not run into their neighbours.
    private void PackColumns(SystemState state, StaffMeasure staffMeasure, Fraction barLength, double measureStartX, double measureWidth,
        (int Voice, EventId Event, int Note)[] order, AccidentalMark[] marks)
    {
        state.ColumnX.Clear();
        SortedDictionary<Fraction, (double Left, double Right)> extents = [];
        double headWidth = _metadata.GetBoundingBox("noteheadBlack").NorthEast.X;
        foreach (Voice voice in staffMeasure.Voices)
        {
            foreach ((MusicEvent leaf, Fraction onset, _) in voice.Events.Flatten())
            {
                double left = 0;
                double right = headWidth;
                if (leaf is Chord chord)
                {
                    // Every visible accidental (naturals included) may need a column of its own.
                    double accidentalColumns = 0;
                    for (int i = 0; i < order.Length; i++)
                    {
                        if (order[i].Event == chord.Id && marks[i] != AccidentalMark.None)
                        {
                            accidentalColumns += _style.MinimumAccidentalGap + 1.0;
                        }
                    }

                    left = accidentalColumns;
                    int[] diatonic = [.. chord.Notes.Select(n => n.Pitch.Octave * 7 + (int)n.Pitch.Step).Order()];
                    for (int i = 1; i < diatonic.Length; i++)
                    {
                        if (diatonic[i] - diatonic[i - 1] == 1)
                        {
                            left += headWidth;
                            right += headWidth;
                            break;
                        }
                    }

                    right += chord.Duration.Dots * 0.8;
                }

                extents[onset] = extents.TryGetValue(onset, out (double Left, double Right) known)
                    ? (Math.Max(known.Left, left), Math.Max(known.Right, right))
                    : (left, right);
            }
        }

        double previousRight = double.NegativeInfinity;
        double x = measureStartX;
        bool first = true;
        foreach ((Fraction onset, (double left, double right)) in extents)
        {
            double proportional = measureStartX + measureWidth * ((double)onset.Num / onset.Den) / ((double)barLength.Num / barLength.Den);
            x = first ? Math.Max(proportional, measureStartX + left) : Math.Max(proportional, previousRight + left + _style.MinimumAccidentalGap);
            state.ColumnX[onset] = x;
            previousRight = x + right;
            first = false;
        }
    }

    private static void RecordGeometry(SystemState state, Chord chord, double centerX, double staffTop, Clef clef, StemDirection? voiceStem)
    {
        int lowest = int.MaxValue;
        int highest = int.MinValue;
        foreach (Note note in chord.Notes)
        {
            int position = StaffPitchPosition.Get(note.Pitch, clef);
            lowest = Math.Min(lowest, position);
            highest = Math.Max(highest, position);
        }

        bool stemUp = chord.Duration.Value != NoteValue.Whole && StaffElementPlacer.ChooseStem(lowest, highest, voiceStem) == StemDirection.Up;
        state.Geometry[chord.Id] = new EventGeometry(centerX, staffTop + 4 - highest * 0.5 - 0.5, staffTop + 4 - lowest * 0.5 + 0.5,
            stemUp, state.CurrentStaff, state.CurrentVoiceNumber, state.CurrentMeasureIndex, staffTop);
    }

    private static Dictionary<EventId, List<Attachment>> BuildAttachmentIndex(Score score)
    {
        Dictionary<EventId, List<Attachment>> index = [];
        foreach (Attachment attachment in score.AttachmentList)
        {
            if (!index.TryGetValue(attachment.Target, out List<Attachment>? list))
            {
                list = [];
                index[attachment.Target] = list;
            }

            list.Add(attachment);
        }

        return index;
    }

    private (Dictionary<EventId, List<Attachment>> Attachments, ImmutableArray<LyricAnchor> LyricAnchors)
        GetAnnotationData(Score score)
    {
        lock (_annotationCacheGate)
        {
            if (_cachedAttachmentIndex is null || _cachedAnnotationList != score.AttachmentList)
            {
                _cachedAnnotationList = score.AttachmentList;
                _cachedAttachmentIndex = BuildAttachmentIndex(score);
                _cachedLyricAnchors = BuildLyricAnchors(score, _cachedAttachmentIndex);
                _cachedLyricAttachmentCount = CountLyricAttachments(score.AttachmentList);
            }
            else if (!AreLyricAnchorsValid(score, _cachedLyricAnchors,
                _cachedLyricAttachmentCount))
            {
                _cachedLyricAnchors = BuildLyricAnchors(score, _cachedAttachmentIndex);
            }

            return (_cachedAttachmentIndex, _cachedLyricAnchors);
        }
    }

    private static int CountLyricAttachments(ImmutableArray<Attachment> attachments)
    {
        int count = 0;
        foreach (Attachment attachment in attachments)
        {
            if (attachment is LyricAttachment)
            {
                count++;
            }
        }

        return count;
    }

    private static bool AreLyricAnchorsValid(Score score, ImmutableArray<LyricAnchor> anchors,
        int lyricAttachmentCount)
    {
        if (anchors.Length != lyricAttachmentCount)
        {
            return false;
        }

        foreach (LyricAnchor anchor in anchors)
        {
            if (!score.Content.TryGetValue(new StaffMeasureKey(anchor.Staff, anchor.MeasureIndex),
                out StaffMeasure? staffMeasure))
            {
                return false;
            }

            bool foundVoice = false;
            foreach (Voice voice in staffMeasure.Voices)
            {
                if (voice.Number != anchor.Voice)
                {
                    continue;
                }

                foundVoice = true;
                if (!ContainsEvent(voice.Events, anchor.Event))
                {
                    return false;
                }

                break;
            }

            if (!foundVoice)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsEvent(ImmutableArray<MusicEvent> events, EventId eventId)
    {
        foreach (MusicEvent musicEvent in events)
        {
            if (musicEvent.Id == eventId || musicEvent is TupletGroup group &&
                ContainsEvent(group.Children, eventId))
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<LyricAnchor> BuildLyricAnchors(Score score,
        Dictionary<EventId, List<Attachment>> attachments)
    {
        if (score.AttachmentList.IsEmpty)
        {
            return ImmutableArray<LyricAnchor>.Empty;
        }

        bool hasLyrics = false;
        foreach (Attachment attachment in score.AttachmentList)
        {
            if (attachment is LyricAttachment)
            {
                hasLyrics = true;
                break;
            }
        }

        if (!hasLyrics)
        {
            return ImmutableArray<LyricAnchor>.Empty;
        }

        ImmutableArray<LyricAnchor>.Builder anchors = ImmutableArray.CreateBuilder<LyricAnchor>();
        int staffCount = CountStaves(score);
        for (int measureIndex = 0; measureIndex < score.Measures.Length; measureIndex++)
        {
            for (int staff = 0; staff < staffCount; staff++)
            {
                if (!score.Content.TryGetValue(new StaffMeasureKey(staff, measureIndex), out StaffMeasure? measure))
                {
                    continue;
                }

                foreach (Voice voice in measure.Voices)
                {
                    foreach (MusicEvent musicEvent in voice.Events)
                    {
                        AppendLyricAnchors(musicEvent, measureIndex, staff, voice.Number,
                            attachments, anchors);
                    }
                }
            }
        }

        return anchors.ToImmutable();
    }

    private static void AppendLyricAnchors(MusicEvent musicEvent, int measureIndex, int staffIndex,
        int voiceNumber, Dictionary<EventId, List<Attachment>> attachments,
        ImmutableArray<LyricAnchor>.Builder anchors)
    {
        if (musicEvent is TupletGroup group)
        {
            foreach (MusicEvent child in group.Children)
            {
                AppendLyricAnchors(child, measureIndex, staffIndex, voiceNumber, attachments, anchors);
            }

            return;
        }

        if (!attachments.TryGetValue(musicEvent.Id, out List<Attachment>? attached))
        {
            return;
        }

        foreach (Attachment attachment in attached)
        {
            if (attachment is LyricAttachment lyric)
            {
                anchors.Add(new LyricAnchor(musicEvent.Id, measureIndex, staffIndex, voiceNumber, lyric));
            }
        }
    }

    // Behind Bars, Slurs: the curve goes on the notehead side, opposite the stems, and above when stems differ;
    // it starts and ends beside the heads, rises with the span and clears every head, stem and mark under it.
    private void AddSlurs(ImmutableArray<DrawingPrimitive>.Builder primitives, Score score, SystemState state,
        int firstPrimitive, double musicStartX, double musicEndX)
    {
        foreach (Spanner spanner in score.SpannerList)
        {
            if (spanner.Kind != SpannerKind.Slur)
            {
                continue;
            }

            bool hasStart = state.Geometry.TryGetValue(spanner.Start, out EventGeometry start);
            bool hasEnd = state.Geometry.TryGetValue(spanner.End, out EventGeometry end);
            if (!hasStart && !hasEnd)
            {
                continue;
            }

            // A slur that continues into another system is drawn open at the system edge.
            if (!hasStart)
            {
                start = end with { CenterX = musicStartX, StemUp = end.StemUp };
            }

            if (!hasEnd)
            {
                end = start with { CenterX = musicEndX - 0.5, StemUp = start.StemUp };
            }

            bool below = start.StemUp && end.StemUp;
            double x0 = start.CenterX + (hasStart ? 0.2 : 0);
            double x3 = end.CenterX - (hasEnd ? 0.2 : 0);
            if (x3 - x0 < 1.0)
            {
                continue;
            }

            double y0 = below ? start.BottomY + 0.1 : start.TopY - 0.1;
            double y3 = below ? end.BottomY + 0.1 : end.TopY - 0.1;
            double sign = below ? 1 : -1;
            double height = Math.Clamp(0.12 * (x3 - x0) + 0.6, 0.8, 4.0);
            List<DisplayBox> obstacles = [];
            for (int i = firstPrimitive; i < primitives.Count; i++)
            {
                DrawingPrimitive primitive = primitives[i];
                if (primitive.ElementId.Value == Guid.Empty)
                {
                    continue;
                }

                bool horizontal = primitive is DisplayLine line && line.Start.Y == line.End.Y;
                if (horizontal || primitive is DisplayLists.Path)
                {
                    continue;
                }

                DisplayBox box = primitive.Bounds;
                if (box.X + box.Width > x0 + 0.6 && box.X < x3 - 0.6 &&
                    Math.Abs(box.Y - (y0 + y3) / 2) < 14)
                {
                    obstacles.Add(box);
                }
            }

            // Raise the arch until no sampled point of the curve touches an obstacle (0.3 space of air).
            for (int attempt = 0; attempt < 60; attempt++)
            {
                if (!Touches(x0, y0, x3, y3, height, sign, obstacles))
                {
                    break;
                }

                height += 0.25;
            }

            primitives.Add(BuildSlur(new ElementId(spanner.Start.Value), x0, y0, x3, y3, height * sign));
        }
    }

    // Hairpins and pedal lines sit below the staff, octave lines above it; each is drawn open at a system edge when it
    // continues into another system. Behind Bars, Dynamics > Hairpins; Octave lines; Pedal marks.
    private void AddLines(ImmutableArray<DrawingPrimitive>.Builder output, Score score, SystemState state,
        double musicStartX, double musicEndX)
    {
        foreach (Spanner spanner in score.SpannerList)
        {
            if (spanner.Kind == SpannerKind.Slur)
            {
                continue;
            }

            bool hasStart = state.Geometry.TryGetValue(spanner.Start, out EventGeometry start);
            bool hasEnd = state.Geometry.TryGetValue(spanner.End, out EventGeometry end);
            if (!hasStart && !hasEnd)
            {
                continue;
            }

            if (!hasStart)
            {
                start = end with { CenterX = musicStartX };
            }

            if (!hasEnd)
            {
                end = start with { CenterX = musicEndX - 0.5 };
            }

            double x0 = start.CenterX - (hasStart ? 0.6 : 0);
            double x1 = end.CenterX + (hasEnd ? 0.6 : 0);
            if (x1 - x0 < 1.0)
            {
                continue;
            }

            ElementId id = new(spanner.Start.Value);
            double thickness = _style.StaffLineThickness * 1.2;
            ImmutableArray<DrawingPrimitive>.Builder primitives = ImmutableArray.CreateBuilder<DrawingPrimitive>();
            switch (spanner.Kind)
            {
                case SpannerKind.Crescendo:
                case SpannerKind.Diminuendo:
                {
                    double y = start.StaffTop + 7.5;
                    double open = 0.6;
                    bool crescendo = spanner.Kind == SpannerKind.Crescendo;
                    double left = crescendo ? 0 : open;
                    double right = crescendo ? open : 0;
                    primitives.Add(HairpinLine(id, x0, y - left, x1, y - right, thickness));
                    primitives.Add(HairpinLine(id, x0, y + left, x1, y + right, thickness));
                    break;
                }

                case SpannerKind.OctaveUp:
                case SpannerKind.OctaveDown:
                {
                    bool up = spanner.Kind == SpannerKind.OctaveUp;
                    double y = up ? start.StaffTop - 3.0 : start.StaffTop + 8.5;
                    string label = up ? "ottavaAlta" : "ottavaBassa";
                    if (hasStart)
                    {
                        AddGlyph(primitives, id, label, x0, y);
                    }

                    double dashStart = x0 + (hasStart ? _metadata.GetBoundingBox(label).NorthEast.X + 0.4 : 0);
                    for (double x = dashStart; x < x1 - 0.2; x += 1.6)
                    {
                        double dashEnd = Math.Min(x + 1.0, x1);
                        primitives.Add(HairpinLine(id, x, y - (up ? 0.5 : 0), dashEnd, y - (up ? 0.5 : 0), thickness));
                    }

                    if (hasEnd)
                    {
                        // The hook points toward the staff.
                        primitives.Add(HairpinLine(id, x1, y - (up ? 0.5 : 0), x1, y - (up ? 0.5 : 0) + (up ? 0.9 : -0.9), thickness));
                    }

                    break;
                }

                case SpannerKind.Pedal:
                {
                    double y = start.StaffTop + 8.5;
                    if (hasStart)
                    {
                        AddGlyph(primitives, id, "keyboardPedalPed", x0, y);
                    }

                    if (hasEnd)
                    {
                        AddGlyph(primitives, id, "keyboardPedalUp", x1 - 0.5, y);
                    }

                    break;
                }
            }

            state.Annotations.Add(new AnnotationGroup(primitives.ToImmutable(), spanner.Kind == SpannerKind.OctaveUp,
                spanner.Kind == SpannerKind.OctaveUp || spanner.Kind == SpannerKind.OctaveDown ? 3 : 1, start.StaffIndex));
        }
    }

    private static DisplayLine HairpinLine(ElementId id, double x1, double y1, double x2, double y2, double thickness) =>
        new(id, new DisplayBox(Math.Min(x1, x2), Math.Min(y1, y2) - thickness / 2, Math.Abs(x2 - x1), Math.Abs(y2 - y1) + thickness),
            new DisplayPoint(x1, y1), new DisplayPoint(x2, y2), thickness);

    // Places the movable marks against the skyline of everything already drawn: each group starts where its
    // rule puts it and moves outward in half-space steps until it touches no head, stem, accidental, articulation
    // or earlier mark. Groups nearest the staff are placed first so stacking runs from the staff outward.
    private static void ResolveAnnotations(ImmutableArray<DrawingPrimitive>.Builder primitives, SystemState state, int firstPrimitive)
    {
        // Obstacles are kept per staff: a staff's marks only have to clear that staff's own notation, and the
        // vertical layout then keeps neighbouring staves far enough apart.
        Dictionary<int, AnnotationObstacleIndex> obstacles = [];
        for (int i = firstPrimitive; i < primitives.Count; i++)
        {
            DrawingPrimitive primitive = primitives[i];
            if (primitive.ElementId.Value == Guid.Empty || (primitive is DisplayLine line && line.Start.Y == line.End.Y) ||
                !state.StaffByPrimitive.TryGetValue(primitive, out int staff))
            {
                continue;
            }

            if (!obstacles.TryGetValue(staff, out AnnotationObstacleIndex? index))
            {
                index = new AnnotationObstacleIndex();
                obstacles[staff] = index;
            }

            index.Add(primitive.Bounds);
        }

        foreach (AnnotationGroup group in state.Annotations.OrderBy(g => g.StaffIndex).ThenBy(g => g.Above).ThenBy(g => g.Priority))
        {
            if (!obstacles.TryGetValue(group.StaffIndex, out AnnotationObstacleIndex? own))
            {
                own = new AnnotationObstacleIndex();
                obstacles[group.StaffIndex] = own;
            }

            DisplayBox box = Union(group.Items);
            double shift = 0;
            for (int step = 0; step < 60 && own.Collides(box, shift); step++)
            {
                shift += group.Above ? -0.5 : 0.5;
            }

            foreach (DrawingPrimitive item in group.Items)
            {
                DrawingPrimitive placed = Translate(item, shift);
                primitives.Add(placed);
                own.Add(placed.Bounds);
                state.StaffByPrimitive[placed] = group.StaffIndex;
                if (group.Lyric is LyricKey lyricKey && placed is DisplayLists.Text lyricText)
                {
                    state.LyricTexts[lyricKey] = lyricText;
                }
            }
        }
    }

    private static DisplayBox Union(ImmutableArray<DrawingPrimitive> items)
    {
        double left = double.MaxValue;
        double top = double.MaxValue;
        double right = double.MinValue;
        double bottom = double.MinValue;
        foreach (DrawingPrimitive item in items)
        {
            left = Math.Min(left, item.Bounds.X);
            top = Math.Min(top, item.Bounds.Y);
            right = Math.Max(right, item.Bounds.X + item.Bounds.Width);
            bottom = Math.Max(bottom, item.Bounds.Y + item.Bounds.Height);
        }

        return new DisplayBox(left, top, right - left, bottom - top);
    }

    private static DrawingPrimitive Translate(DrawingPrimitive primitive, double dy)
    {
        if (dy == 0)
        {
            return primitive;
        }

        DisplayBox bounds = primitive.Bounds with { Y = primitive.Bounds.Y + dy };
        return primitive switch
        {
            DisplayGlyph glyph => glyph with { Bounds = bounds, Origin = new DisplayPoint(glyph.Origin.X, glyph.Origin.Y + dy) },
            DisplayLine line => line with
            {
                Bounds = bounds,
                Start = new DisplayPoint(line.Start.X, line.Start.Y + dy),
                End = new DisplayPoint(line.End.X, line.End.Y + dy),
            },
            DisplayLists.Text text => text with { Bounds = bounds, Origin = new DisplayPoint(text.Origin.X, text.Origin.Y + dy) },
            _ => primitive,
        };
    }

    private static double CurveY(double x0, double y0, double x3, double y3, double height, double sign, double t, out double x)
    {
        // Cubic Bézier with control points at a third and two thirds of the span, raised by 4/3 of the arch height.
        double c1x = x0 + (x3 - x0) / 3;
        double c2x = x0 + 2 * (x3 - x0) / 3;
        double rise = 4.0 / 3.0 * height * sign;
        double c1y = y0 + (y3 - y0) / 3 + rise;
        double c2y = y0 + 2 * (y3 - y0) / 3 + rise;
        double u = 1 - t;
        x = u * u * u * x0 + 3 * u * u * t * c1x + 3 * u * t * t * c2x + t * t * t * x3;
        return u * u * u * y0 + 3 * u * u * t * c1y + 3 * u * t * t * c2y + t * t * t * y3;
    }

    private static bool Touches(double x0, double y0, double x3, double y3, double height, double sign, List<DisplayBox> obstacles)
    {
        for (int step = 1; step < 32; step++)
        {
            double y = CurveY(x0, y0, x3, y3, height, sign, step / 32.0, out double x);
            foreach (DisplayBox box in obstacles)
            {
                if (x >= box.X - 0.2 && x <= box.X + box.Width + 0.2 && y >= box.Y - 0.3 && y <= box.Y + box.Height + 0.3)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // A filled crescent: the outer edge rises the full arch height, the inner edge a little less, so the curve is
    // thickest in the middle and tapers to points at both ends.
    private static DisplayLists.Path BuildSlur(ElementId id, double x0, double y0, double x3, double y3, double signedHeight)
    {
        double sign = Math.Sign(signedHeight);
        double height = Math.Abs(signedHeight);
        double thickness = 0.22;
        double outer = 4.0 / 3.0 * height;
        double inner = 4.0 / 3.0 * Math.Max(0, height - thickness);
        double c1x = x0 + (x3 - x0) / 3;
        double c2x = x0 + 2 * (x3 - x0) / 3;
        double c1y = y0 + (y3 - y0) / 3;
        double c2y = y0 + 2 * (y3 - y0) / 3;
        ImmutableArray<PathCommand> commands =
        [
            new(PathVerb.MoveTo, new DisplayPoint(x0, y0), default, default),
            new(PathVerb.CubicTo, new DisplayPoint(c1x, c1y + sign * outer), new DisplayPoint(c2x, c2y + sign * outer), new DisplayPoint(x3, y3)),
            new(PathVerb.CubicTo, new DisplayPoint(c2x, c2y + sign * inner), new DisplayPoint(c1x, c1y + sign * inner), new DisplayPoint(x0, y0)),
            new(PathVerb.Close, default, default, default),
        ];
        double top = Math.Min(y0, y3) - (sign < 0 ? height : 0);
        double bottom = Math.Max(y0, y3) + (sign > 0 ? height : 0);
        return new DisplayLists.Path(id, new DisplayBox(x0, top, x3 - x0, bottom - top), commands, 0);
    }

    // Dynamics sit below the staff, tempo marks and chord symbols above it, expression text below the dynamics.
    private void AddAnnotations(ImmutableArray<DrawingPrimitive>.Builder output, EventId eventId, double x,
        double staffTop, SystemState attachments)
    {
        if (!attachments.Attachments.TryGetValue(eventId, out List<Attachment>? list))
        {
            return;
        }

        ElementId id = new(eventId.Value);
        double centerX = x + _metadata.GetBoundingBox("noteheadBlack").NorthEast.X / 2;
        foreach (Attachment attachment in list)
        {
            ImmutableArray<DrawingPrimitive>.Builder primitives = ImmutableArray.CreateBuilder<DrawingPrimitive>();
            switch (attachment)
            {
                case DynamicAttachment dynamic:
                    string name = dynamic.Level switch
                    {
                        DynamicLevel.Ppp => "dynamicPPP",
                        DynamicLevel.Pp => "dynamicPP",
                        DynamicLevel.P => "dynamicPiano",
                        DynamicLevel.Mp => "dynamicMP",
                        DynamicLevel.Mf => "dynamicMF",
                        DynamicLevel.F => "dynamicForte",
                        DynamicLevel.Ff => "dynamicFF",
                        _ => "dynamicFFF",
                    };
                    SmuflBoundingBox box = _metadata.GetBoundingBox(name);
                    AddGlyph(primitives, id, name, centerX - (box.NorthEast.X - box.SouthWest.X) / 2 - box.SouthWest.X, staffTop + 7.5);
                    break;
                case TempoAttachment tempo:
                    string beat = tempo.Beat.Value switch
                    {
                        NoteValue.Whole => "metNoteWhole",
                        NoteValue.Half => "metNoteHalfUp",
                        NoteValue.Eighth => "metNote8thUp",
                        NoteValue.Sixteenth => "metNote16thUp",
                        _ => "metNoteQuarterUp",
                    };
                    AddGlyph(primitives, id, beat, x, staffTop - 3.5);
                    double afterBeat = x + _metadata.GetBoundingBox(beat).NorthEast.X + (tempo.Beat.Dots > 0 ? 0.6 : 0) + 0.3;
                    if (tempo.Beat.Dots > 0)
                    {
                        AddGlyph(primitives, id, "metAugmentationDot", x + _metadata.GetBoundingBox(beat).NorthEast.X + 0.15, staffTop - 3.5);
                    }

                    string label = $"= {tempo.Bpm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
                    AddText(primitives, id, label, afterBeat, staffTop - 3.5, 2.6);
                    break;
                case ChordSymbolAttachment chord:
                    AddText(primitives, id, chord.Display, x, staffTop - 2.2, 2.8);
                    break;
                case LyricAttachment lyric when !string.IsNullOrEmpty(lyric.Text):
                    // Behind Bars, Text > Lyrics: place each verse below the vocal staff in its own row.
                    AddText(primitives, id, lyric.Text, centerX,
                        staffTop + 10.5 + (lyric.Verse - 1) * 3.2, 2.2);
                    break;
                case TextAttachment text:
                    AddText(primitives, id, text.Text, x, staffTop + 10.5, 2.6);
                    break;
            }

            if (primitives.Count > 0)
            {
                // Marks near the staff come first: dynamics, then tempo and chord symbols, then text.
                (bool above, int priority) = attachment switch
                {
                    DynamicAttachment => (false, 0),
                    TempoAttachment => (true, 1),
                    ChordSymbolAttachment => (true, 0),
                    LyricAttachment => (false, 2),
                    _ => (false, 2),
                };
                LyricKey? lyricKey = attachment is LyricAttachment lyric
                    ? new LyricKey(lyric.Target, lyric.Verse) : null;
                attachments.Annotations.Add(new AnnotationGroup(primitives.ToImmutable(), above, priority,
                    attachments.CurrentStaff, lyricKey));
            }
        }
    }

    private static void AddText(ImmutableArray<DrawingPrimitive>.Builder primitives, ElementId id, string text, double x,
        double baselineY, double size)
    {
        // The width is estimated from the character count; exact metrics arrive with the text shaper in F4.8.
        double width = text.Length * size * 0.55;
        primitives.Add(new DisplayLists.Text(id, new DisplayBox(x, baselineY - size * 0.8, width, size),
            text, new DisplayPoint(x, baselineY), size));
    }

    private static void AddArticulations(ImmutableArray<DrawingPrimitive>.Builder primitives, StaffElementPlacer placer,
        Chord chord, double centerX, double staffTop, Clef clef, StemDirection? voiceStem,
        SystemState articulations)
    {
        if (!articulations.Attachments.TryGetValue(chord.Id, out List<Attachment>? attached))
        {
            return;
        }

        List<ArticulationKind> kinds = [.. attached.OfType<ArticulationAttachment>().Select(a => a.Kind)];
        if (kinds.Count == 0)
        {
            return;
        }

        int lowest = int.MaxValue;
        int highest = int.MinValue;
        foreach (Note note in chord.Notes)
        {
            int position = StaffPitchPosition.Get(note.Pitch, clef);
            lowest = Math.Min(lowest, position);
            highest = Math.Max(highest, position);
        }

        StemDirection stem = StaffElementPlacer.ChooseStem(lowest, highest, voiceStem);
        primitives.AddRange(placer.PlaceArticulations(chord.Id, kinds, centerX, staffTop, lowest, highest, stem,
            chord.Duration.Value != NoteValue.Whole));
    }

    // Draws the members at their sounding onsets, then the bracket and number above the group.
    // Behind Bars, Tuplets: a bracket with the ratio number spans the group unless the notes are beamed.
    private void AddTuplet(ImmutableArray<DrawingPrimitive>.Builder primitives, StaffElementPlacer placer, TupletGroup group,
        Voice voice, StaffMeasure staffMeasure, Clef clef, double measureStartX, double measureWidth, double staffTop,
        double barLength, (int Voice, EventId Event, int Note)[] order, AccidentalMark[] marks, bool manyVoices,
        double headWidth, StemDirection? voiceStem, double restShift,
        SystemState articulations)
    {
        double left = double.MaxValue;
        double right = double.MinValue;
        foreach ((MusicEvent leaf, Fraction onset, _) in new[] { (MusicEvent)group }.Flatten())
        {
            AddLeaf(primitives, placer, leaf, onset, voice, staffMeasure, clef, measureStartX, measureWidth, staffTop,
                barLength, order, marks, manyVoices, headWidth, voiceStem, restShift, articulations);
            double x = articulations.ColumnX.TryGetValue(onset, out double packedX)
                ? packedX
                : measureStartX + measureWidth * ((double)onset.Num / onset.Den) / barLength;
            left = Math.Min(left, x);
            right = Math.Max(right, x + 1.2);
        }

        bool below = voiceStem == StemDirection.Down;
        double y = below ? staffTop + 6 : staffTop - 2;
        double hook = below ? -0.8 : 0.8;
        ElementId id = new(group.Id.Value);
        string number = group.Actual.ToString(System.Globalization.CultureInfo.InvariantCulture);
        double numberWidth = 0;
        foreach (char digit in number)
        {
            numberWidth += _metadata.GetBoundingBox($"tuplet{digit}").NorthEast.X;
        }

        double middle = (left + right) / 2;
        double gapLeft = middle - numberWidth / 2 - 0.4;
        double gapRight = middle + numberWidth / 2 + 0.4;
        double thickness = _style.StaffLineThickness * 1.5;
        primitives.Add(BracketLine(id, left, y, gapLeft, y, thickness));
        primitives.Add(BracketLine(id, gapRight, y, right, y, thickness));
        primitives.Add(BracketLine(id, left, y, left, y + hook, thickness));
        primitives.Add(BracketLine(id, right, y, right, y + hook, thickness));
        double digitX = middle - numberWidth / 2;
        foreach (char digit in number)
        {
            string name = $"tuplet{digit}";
            AddGlyph(primitives, id, name, digitX, y + (below ? 0.5 : 0.5));
            digitX += _metadata.GetBoundingBox(name).NorthEast.X;
        }
    }

    private readonly record struct EventGeometry(double CenterX, double TopY, double BottomY,
        bool StemUp, int StaffIndex, int VoiceNumber, int MeasureIndex, double StaffTop);

    private readonly record struct LyricKey(EventId Event, int Verse);

    private readonly record struct LyricAnchor(EventId Event, int MeasureIndex, int Staff,
        int Voice, LyricAttachment Lyric);

    private sealed class SystemState(Dictionary<EventId, List<Attachment>> attachments,
        ImmutableArray<LyricAnchor> lyricAnchors)
    {
        public Dictionary<EventId, List<Attachment>> Attachments { get; } = attachments;

        public ImmutableArray<LyricAnchor> LyricAnchors { get; } = lyricAnchors;

        public Dictionary<EventId, EventGeometry> Geometry { get; } = [];

        public Dictionary<EventId, double> EventX { get; } = [];

        public Dictionary<LyricKey, DisplayLists.Text> LyricTexts { get; } = [];

        public int CurrentStaff { get; set; }

        public int CurrentVoiceNumber { get; set; }

        public int CurrentMeasureIndex { get; set; }

        // Horizontal position of each rhythmic column of the staff measure being drawn.
        public Dictionary<Fraction, double> ColumnX { get; } = [];

        public List<AnnotationGroup> Annotations { get; } = [];

        // Which staff each drawn primitive belongs to, so marks and skylines are resolved staff by staff.
        public Dictionary<DrawingPrimitive, int> StaffByPrimitive { get; } = new(ReferenceEqualityComparer.Instance);
    }

    private sealed record AnnotationGroup(ImmutableArray<DrawingPrimitive> Items, bool Above,
        int Priority, int StaffIndex, LyricKey? Lyric = null);

    private static DisplayLine BracketLine(ElementId id, double x1, double y1, double x2, double y2, double thickness) =>
        new(id, new DisplayBox(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1) + thickness, Math.Abs(y2 - y1) + thickness),
            new DisplayPoint(x1, y1), new DisplayPoint(x2, y2), thickness);

    // A head of a higher-numbered voice clashes when a lower voice has a head within a step at the same
    // onset; a unison of equal note values shares one head instead.
    private static bool CollidesWithLowerVoice(StaffMeasure measure, Voice voice, Chord chord, Note note, Clef clef)
    {
        int position = StaffPitchPosition.Get(note.Pitch, clef);
        foreach (Voice other in measure.Voices)
        {
            if (other.Number >= voice.Number)
            {
                continue;
            }

            foreach (MusicEvent musicEvent in other.Events)
            {
                if (musicEvent is not Chord otherChord || musicEvent.Onset != chord.Onset)
                {
                    continue;
                }

                foreach (Note otherNote in otherChord.Notes)
                {
                    int distance = Math.Abs(StaffPitchPosition.Get(otherNote.Pitch, clef) - position);
                    bool sameHead = distance == 0 && HeadKind(otherChord.Duration) == HeadKind(chord.Duration);
                    if (distance <= 1 && !sameHead)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static int HeadKind(Duration duration) => duration.Value switch
    {
        NoteValue.Whole => 0,
        NoteValue.Half => 1,
        _ => 2,
    };

    private static AccidentalMark[] ResolveAccidentals(Measure measure, KeySignature keySignature,
        int measureIndex,
        StaffMeasure staffMeasure, out (int Voice, EventId Event, int Note)[] order)
    {
        // Accidentals follow the sounding order of the measure across voices
        // (Behind Bars, Accidentals and Key Signatures > Using accidentals).
        List<(int Voice, Chord Chord, Fraction Onset, Chord? Previous)> chords = [];
        foreach (Voice voice in staffMeasure.Voices)
        {
            Chord? previous = null;
            foreach ((MusicEvent leaf, Fraction onset, _) in voice.Events.Flatten())
            {
                if (leaf is Chord chord)
                {
                    chords.Add((voice.Number, chord, onset, previous));
                    previous = chord;
                }
                else
                {
                    previous = null;
                }
            }
        }

        chords.Sort((a, b) => a.Onset.CompareTo(b.Onset));
        int count = 0;
        foreach ((int _, Chord chord, Fraction _, Chord? _) in chords)
        {
            count += chord.Notes.Length;
        }

        order = new (int, EventId, int)[count];
        AccidentalInput[] inputs = new AccidentalInput[count];
        int slot = 0;
        foreach ((int voice, Chord chord, _, Chord? previous) in chords)
        {
            for (int noteIndex = 0; noteIndex < chord.Notes.Length; noteIndex++)
            {
                Pitch pitch = chord.Notes[noteIndex].Pitch;
                bool tiedFromPrevious = false;
                if (previous is not null)
                {
                    foreach (Note candidate in previous.Notes)
                    {
                        if (candidate.TiedToNext && candidate.Pitch == pitch)
                        {
                            tiedFromPrevious = true;
                        }
                    }
                }

                order[slot] = (voice, chord.Id, noteIndex);
                inputs[slot] = new AccidentalInput(measureIndex, pitch, tiedFromPrevious, keySignature);
                slot++;
            }
        }

        ImmutableArray<AccidentalMark> resolved = new AccidentalResolver().Resolve(inputs);
        AccidentalMark[] marks = new AccidentalMark[count];
        resolved.CopyTo(marks);
        return marks;
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

    private void AddMeasureBoundary(ImmutableArray<DrawingPrimitive>.Builder primitives,
        Score score, EventId id, int boundaryIndex, double x, double staffTop, double thickness)
    {
        bool leftRepeat = boundaryIndex < score.Measures.Length &&
            score.Measures[boundaryIndex].Repeat?.StartRepeat == true;
        bool rightRepeat = boundaryIndex > 0 &&
            score.Measures[boundaryIndex - 1].Repeat?.EndRepeat is not null;
        if (!leftRepeat && !rightRepeat)
        {
            AddBarline(primitives, id, x, staffTop, thickness);
            return;
        }

        // SMuFL repeat glyphs span the four-line staff and take their origin on the bottom line.
        // See SMuFL tables > Repeats and barlines.
        string glyph = (leftRepeat, rightRepeat) switch
        {
            (true, true) => "repeatRightLeft",
            (true, false) => "repeatLeft",
            (false, true) => "repeatRight",
            _ => throw new InvalidOperationException("A repeat boundary needs at least one repeat side."),
        };
        AddGlyph(primitives, new ElementId(id.Value), glyph, x, staffTop + StaffHeightSpaces);
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

    private StaffSkyline[][] GetMeasureExtents(Score score, Score cacheSource,
        ScoreLayoutResult layout, VerticalLayoutResult provisional, int staffCount,
        StaffElementPlacer placer, double leftMargin, Dictionary<EventId, List<Attachment>> attachments,
        ImmutableArray<LyricAnchor> lyricAnchors, CancellationToken cancellationToken,
        PitchDisplayMode pitchDisplayMode, ImmutableArray<int> partIndices)
    {
        lock (_skylineCacheGate)
        {
            if (ReferenceEquals(cacheSource, _cachedSkylineSource) &&
                pitchDisplayMode == _cachedSkylinePitchMode &&
                PartIndicesEqual(partIndices, _cachedSkylinePartIndices) &&
                _cachedSkylineExtents is not null &&
                LayoutsEquivalent(layout, _cachedSkylineLayout))
            {
                return _cachedSkylineExtents;
            }

            bool canReuseSystems = _cachedSkylineExtents is not null &&
                _cachedSkylineSource is not null && _cachedSkylineDisplayScore is not null &&
                _cachedSkylineLayout is not null && pitchDisplayMode == _cachedSkylinePitchMode &&
                PartIndicesEqual(partIndices, _cachedSkylinePartIndices) &&
                cacheSource.Instruments == _cachedSkylineSource.Instruments &&
                cacheSource.AttachmentList == _cachedSkylineSource.AttachmentList &&
                cacheSource.SpannerList == _cachedSkylineSource.SpannerList &&
                CountStaves(_cachedSkylineDisplayScore) == staffCount;
            StaffSkyline[][] extents = new StaffSkyline[layout.Systems.Length][];
            bool crossStaffSpannersChecked = false;
            bool hasCrossStaffSpanners = false;
            for (int systemIndex = 0; systemIndex < layout.Systems.Length; systemIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SystemLine system = layout.Systems[systemIndex];
                if (canReuseSystems && TryFindEquivalentSystem(system, _cachedSkylineLayout!.Systems,
                    out int cachedSystemIndex))
                {
                    StaffSkyline[] cachedStaffExtents = _cachedSkylineExtents![cachedSystemIndex];
                    if (SystemContentEquivalent(score, _cachedSkylineDisplayScore!, system.Range))
                    {
                        extents[systemIndex] = cachedStaffExtents;
                    }
                    else if (SystemMeasuresEquivalent(score, _cachedSkylineDisplayScore!, system.Range))
                    {
                        if (!crossStaffSpannersChecked)
                        {
                            hasCrossStaffSpanners = HasCrossStaffSpanners(score);
                            crossStaffSpannersChecked = true;
                        }

                        if (!hasCrossStaffSpanners)
                        {
                            StaffSkyline[] staffExtents = new StaffSkyline[staffCount];
                            int changedStaffCount = 0;
                            for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
                            {
                                bool unchanged = StaffContentEquivalent(score,
                                    _cachedSkylineDisplayScore!, system.Range, staffIndex);
                                if (unchanged)
                                {
                                    staffExtents[staffIndex] = cachedStaffExtents[staffIndex];
                                }
                                else
                                {
                                    changedStaffCount++;
                                }
                            }

                            if (changedStaffCount > 0 && changedStaffCount * 2 < staffCount)
                            {
                                for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
                                {
                                    if (!StaffContentEquivalent(score, _cachedSkylineDisplayScore!,
                                        system.Range, staffIndex))
                                    {
                                        staffExtents[staffIndex] = MeasureStaffExtents(score, layout,
                                            systemIndex, provisional.Systems[systemIndex], staffCount,
                                            placer, leftMargin, attachments, lyricAnchors,
                                            cancellationToken, pitchDisplayMode, staffIndex);
                                    }
                                }

                                extents[systemIndex] = staffExtents;
                                Interlocked.Increment(ref _skylineSystemMeasureCount);
                            }
                            else
                            {
                                extents[systemIndex] = MeasureSystemExtents(score, layout,
                                    systemIndex, provisional.Systems[systemIndex], staffCount,
                                    placer, leftMargin, attachments, lyricAnchors,
                                    cancellationToken, pitchDisplayMode);
                                Interlocked.Increment(ref _skylineSystemMeasureCount);
                            }
                        }
                        else
                        {
                            extents[systemIndex] = MeasureSystemExtents(score, layout,
                                systemIndex, provisional.Systems[systemIndex], staffCount,
                                placer, leftMargin, attachments, lyricAnchors,
                                cancellationToken, pitchDisplayMode);
                            Interlocked.Increment(ref _skylineSystemMeasureCount);
                        }
                    }
                    else
                    {
                        extents[systemIndex] = MeasureSystemExtents(score, layout,
                            systemIndex, provisional.Systems[systemIndex], staffCount,
                            placer, leftMargin, attachments, lyricAnchors,
                            cancellationToken, pitchDisplayMode);
                        Interlocked.Increment(ref _skylineSystemMeasureCount);
                    }
                }
                else
                {
                    extents[systemIndex] = MeasureSystemExtents(score, layout, systemIndex,
                        provisional.Systems[systemIndex], staffCount, placer, leftMargin,
                        attachments, lyricAnchors, cancellationToken, pitchDisplayMode);
                    Interlocked.Increment(ref _skylineSystemMeasureCount);
                }
            }

            _cachedSkylineSource = cacheSource;
            _cachedSkylineDisplayScore = score;
            _cachedSkylineLayout = layout;
            _cachedSkylinePartIndices = partIndices;
            _cachedSkylinePitchMode = pitchDisplayMode;
            _cachedSkylineExtents = extents;
            return extents;
        }
    }

    private StaffSkyline[] MeasureSystemExtents(Score score, ScoreLayoutResult layout,
        int systemIndex, SystemVerticalPlacement placement, int staffCount, StaffElementPlacer placer,
        double leftMargin, Dictionary<EventId, List<Attachment>> attachments,
        ImmutableArray<LyricAnchor> lyricAnchors, CancellationToken cancellationToken,
        PitchDisplayMode pitchDisplayMode)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ImmutableArray<DrawingPrimitive>.Builder drawn = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        SystemState scratch = new(attachments, lyricAnchors);
        DrawSystem(drawn, score, layout, systemIndex, placement, staffCount, placer, scratch,
            leftMargin, cancellationToken, ImmutableArray<MultiMeasureRestGroup>.Empty, pitchDisplayMode);
        double[] top = new double[staffCount];
        double[] bottom = new double[staffCount];
        Array.Fill(top, 0.5);
        Array.Fill(bottom, 0.5);
        foreach (DrawingPrimitive primitive in drawn)
        {
            // Staff lines, headers and barlines belong to the staves themselves.
            if (primitive.ElementId.Value == Guid.Empty || primitive is DisplayLists.Path)
            {
                continue;
            }

            if (!scratch.StaffByPrimitive.TryGetValue(primitive, out int nearest))
            {
                continue;
            }

            top[nearest] = Math.Max(top[nearest], placement.StaffTops[nearest] - primitive.Bounds.Y);
            bottom[nearest] = Math.Max(bottom[nearest], primitive.Bounds.Y + primitive.Bounds.Height -
                (placement.StaffTops[nearest] + 4));
        }

        StaffSkyline[] extent = new StaffSkyline[staffCount];
        for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
        {
            extent[staffIndex] = new StaffSkyline(top[staffIndex], bottom[staffIndex]);
        }

        return extent;
    }

    private StaffSkyline MeasureStaffExtents(Score score, ScoreLayoutResult layout,
        int systemIndex, SystemVerticalPlacement placement, int staffCount, StaffElementPlacer placer,
        double leftMargin, Dictionary<EventId, List<Attachment>> attachments,
        ImmutableArray<LyricAnchor> lyricAnchors, CancellationToken cancellationToken,
        PitchDisplayMode pitchDisplayMode, int staffIndex)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ImmutableArray<DrawingPrimitive>.Builder drawn = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        SystemState scratch = new(attachments, lyricAnchors);
        DrawSystem(drawn, score, layout, systemIndex, placement, staffCount, placer, scratch,
            leftMargin, cancellationToken, ImmutableArray<MultiMeasureRestGroup>.Empty,
            pitchDisplayMode, staffIndex);
        double top = 0.5;
        double bottom = 0.5;
        foreach (DrawingPrimitive primitive in drawn)
        {
            if (primitive.ElementId.Value == Guid.Empty || primitive is DisplayLists.Path ||
                !scratch.StaffByPrimitive.TryGetValue(primitive, out int nearest) ||
                nearest != staffIndex)
            {
                continue;
            }

            top = Math.Max(top, placement.StaffTops[staffIndex] - primitive.Bounds.Y);
            bottom = Math.Max(bottom, primitive.Bounds.Y + primitive.Bounds.Height -
                (placement.StaffTops[staffIndex] + 4));
        }

        return new StaffSkyline(top, bottom);
    }

    private static bool LayoutsEquivalent(ScoreLayoutResult current, ScoreLayoutResult? cached)
    {
        if (cached is null || current.Systems.Length != cached.Systems.Length)
        {
            return false;
        }

        for (int index = 0; index < current.Systems.Length; index++)
        {
            if (!SystemsEquivalent(current.Systems[index], cached.Systems[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFindEquivalentSystem(SystemLine system,
        ImmutableArray<SystemLine> cachedSystems, out int cachedSystemIndex)
    {
        for (int index = 0; index < cachedSystems.Length; index++)
        {
            if (SystemsEquivalent(system, cachedSystems[index]))
            {
                cachedSystemIndex = index;
                return true;
            }
        }

        cachedSystemIndex = -1;
        return false;
    }

    private static bool SystemsEquivalent(SystemLine left, SystemLine right)
    {
        if (left.Range != right.Range || left.NaturalWidth != right.NaturalWidth ||
            left.PlacedWidth != right.PlacedWidth || left.IsLast != right.IsLast ||
            left.MeasureWidths.Length != right.MeasureWidths.Length)
        {
            return false;
        }

        for (int index = 0; index < left.MeasureWidths.Length; index++)
        {
            if (left.MeasureWidths[index] != right.MeasureWidths[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool SystemContentEquivalent(Score current, Score cached,
        SystemLineMeasureRange range)
    {
        if (range.StartIndex < 0 || range.Count < 0 ||
            range.StartIndex + range.Count > current.Measures.Length ||
            range.StartIndex + range.Count > cached.Measures.Length ||
            current.Measures.Length != cached.Measures.Length)
        {
            return false;
        }

        int staffCount = CountStaves(current);
        if (!SystemMeasuresEquivalent(current, cached, range))
        {
            return false;
        }

        for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
        {
            if (!StaffContentEquivalent(current, cached, range, staffIndex))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SystemMeasuresEquivalent(Score current, Score cached,
        SystemLineMeasureRange range)
    {
        if (range.StartIndex < 0 || range.Count < 0 ||
            range.StartIndex + range.Count > current.Measures.Length ||
            range.StartIndex + range.Count > cached.Measures.Length ||
            current.Measures.Length != cached.Measures.Length)
        {
            return false;
        }

        for (int measureIndex = range.StartIndex; measureIndex < range.StartIndex + range.Count;
            measureIndex++)
        {
            if (current.Measures[measureIndex] != cached.Measures[measureIndex])
            {
                return false;
            }
        }

        return true;
    }

    private static bool StaffContentEquivalent(Score current, Score cached,
        SystemLineMeasureRange range, int staffIndex)
    {
        if (range.StartIndex < 0 || range.Count < 0 ||
            range.StartIndex + range.Count > current.Measures.Length ||
            range.StartIndex + range.Count > cached.Measures.Length ||
            current.Measures.Length != cached.Measures.Length)
        {
            return false;
        }

        for (int measureIndex = range.StartIndex; measureIndex < range.StartIndex + range.Count;
            measureIndex++)
        {
            StaffMeasureKey key = new(staffIndex, measureIndex);
            bool hasCurrent = current.Content.TryGetValue(key, out StaffMeasure? currentContent);
            bool hasCached = cached.Content.TryGetValue(key, out StaffMeasure? cachedContent);
            if (hasCurrent != hasCached ||
                (hasCurrent && !ReferenceEquals(currentContent, cachedContent)))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasCrossStaffSpanners(Score score)
    {
        if (score.SpannerList.IsEmpty)
        {
            _cachedSpannerDefinitions = score.SpannerList;
            _cachedSpannerLocations = [];
            _cachedSpannerEndpointCount = 0;
            _cachedHasCrossStaffSpanners = false;
            return false;
        }

        if (_cachedSpannerLocations is not null &&
            _cachedSpannerDefinitions == score.SpannerList &&
            _cachedSpannerLocations.Count == _cachedSpannerEndpointCount &&
            CachedSpannerLocationsAreValid(score, _cachedSpannerLocations))
        {
            return _cachedHasCrossStaffSpanners;
        }

        HashSet<EventId> targets = [];
        foreach (Spanner spanner in score.SpannerList)
        {
            targets.Add(spanner.Start);
            targets.Add(spanner.End);
        }

        Dictionary<EventId, SpannerLocation> eventLocations = new(targets.Count);
        foreach ((StaffMeasureKey key, StaffMeasure measure) in score.Content)
        {
            foreach (Voice voice in measure.Voices)
            {
                AddEventLocations(voice.Events, key, voice.Number, targets, eventLocations);
            }
        }

        bool hasCrossStaffSpanners = false;
        foreach (Spanner spanner in score.SpannerList)
        {
            if (!eventLocations.TryGetValue(spanner.Start, out SpannerLocation start) ||
                !eventLocations.TryGetValue(spanner.End, out SpannerLocation end) ||
                start.Key.StaffIndex != end.Key.StaffIndex)
            {
                hasCrossStaffSpanners = true;
                break;
            }
        }

        _cachedSpannerDefinitions = score.SpannerList;
        _cachedSpannerLocations = eventLocations;
        _cachedSpannerEndpointCount = targets.Count;
        _cachedHasCrossStaffSpanners = hasCrossStaffSpanners;
        return hasCrossStaffSpanners;
    }

    private static bool CachedSpannerLocationsAreValid(Score score,
        Dictionary<EventId, SpannerLocation> locations)
    {
        foreach ((EventId eventId, SpannerLocation location) in locations)
        {
            if (!score.Content.TryGetValue(location.Key, out StaffMeasure? staffMeasure))
            {
                return false;
            }

            bool found = false;
            foreach (Voice voice in staffMeasure.Voices)
            {
                if (voice.Number == location.VoiceNumber && ContainsEvent(voice.Events, eventId))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddEventLocations(ImmutableArray<MusicEvent> events, StaffMeasureKey key,
        int voiceNumber, HashSet<EventId> targets, Dictionary<EventId, SpannerLocation> locations)
    {
        foreach (MusicEvent musicEvent in events)
        {
            if (targets.Contains(musicEvent.Id))
            {
                locations[musicEvent.Id] = new SpannerLocation(key, voiceNumber);
            }

            if (musicEvent is TupletGroup group)
            {
                AddEventLocations(group.Children, key, voiceNumber, targets, locations);
            }
        }
    }

    private static bool PartIndicesEqual(ImmutableArray<int> left, ImmutableArray<int> right)
    {
        if (left.IsDefaultOrEmpty || right.IsDefaultOrEmpty)
        {
            return left.IsDefaultOrEmpty && right.IsDefaultOrEmpty;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static VerticalLayoutResult BuildVerticalLayout(ImmutableArray<SystemLine> systems,
        int staffCount, double pageHeight, double pageMargin, StaffSkyline[][]? extents)
    {
        VerticalSystem[] verticalSystems = new VerticalSystem[systems.Length];
        for (int systemIndex = 0; systemIndex < systems.Length; systemIndex++)
        {
            ImmutableArray<StaffSkyline>.Builder staves = ImmutableArray.CreateBuilder<StaffSkyline>(staffCount);
            for (int index = 0; index < staffCount; index++)
            {
                staves.Add(extents is null ? new StaffSkyline(0.5, 0.5) : extents[systemIndex][index]);
            }

            verticalSystems[systemIndex] = new VerticalSystem(staves.MoveToImmutable(),
                MinimumStaffGap: 1.5,
                SkylineClearance: 1,
                LeadingSpace: 1,
                TrailingSpace: 1);
        }

        return new VerticalPageLayouter().Layout(verticalSystems, pageHeight,
            pageMargin, pageMargin, systemGap: 2);
    }

    private readonly record struct MultiMeasureRestSegment(int StartLocalMeasure,
        int MeasureCount, int TotalMeasureCount, bool ShowCount);

    private readonly record struct HeaderCacheKey(string ClefGlyph, KeySignature KeySignature,
        TimeSignature TimeSignature);

    private readonly record struct SpannerLocation(StaffMeasureKey Key, int VoiceNumber);

    private sealed class AnnotationObstacleIndex
    {
        private const double BucketWidth = 4;
        private const double Clearance = 0.15;
        private readonly Dictionary<int, List<DisplayBox>> _buckets = [];

        public void Add(DisplayBox box)
        {
            int first = BucketAt(box.X);
            int last = BucketAt(box.X + box.Width);
            for (int bucket = first; bucket <= last; bucket++)
            {
                if (!_buckets.TryGetValue(bucket, out List<DisplayBox>? items))
                {
                    items = [];
                    _buckets.Add(bucket, items);
                }

                items.Add(box);
            }
        }

        public bool Collides(DisplayBox box, double shift)
        {
            int first = BucketAt(box.X - Clearance);
            int last = BucketAt(box.X + box.Width + Clearance);
            for (int bucket = first; bucket <= last; bucket++)
            {
                if (!_buckets.TryGetValue(bucket, out List<DisplayBox>? items))
                {
                    continue;
                }

                foreach (DisplayBox other in items)
                {
                    if (box.X < other.X + other.Width + Clearance &&
                        other.X < box.X + box.Width + Clearance &&
                        box.Y + shift < other.Y + other.Height + Clearance &&
                        other.Y < box.Y + shift + box.Height + Clearance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int BucketAt(double x) => (int)Math.Floor(x / BucketWidth);
    }
}
