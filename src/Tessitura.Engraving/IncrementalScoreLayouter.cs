using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Smufl;

namespace Tessitura.Engraving;

/// <summary>Contains cached measure widths and the system breaks for one score snapshot.</summary>
/// <param name="MeasureWidths">The measured widths in global measure order.</param>
/// <param name="Systems">The system ranges and their justified widths.</param>
/// <param name="RecomputedMeasureCount">The number of measure widths calculated for this update.</param>
/// <param name="ReflowedSystemCount">The number of systems recalculated before a stable break was found.</param>
public sealed record ScoreLayoutResult(
    ImmutableArray<SystemBreakMeasure> MeasureWidths,
    ImmutableArray<SystemLine> Systems,
    int RecomputedMeasureCount,
    int ReflowedSystemCount);

/// <summary>Caches measure widths and reflows systems after score edits.</summary>
public sealed class IncrementalScoreLayouter
{
    private readonly MeasureWidthCalculator _measureWidthCalculator;
    private readonly SystemBreaker _systemBreaker = new();
    private SystemBreakMeasure[] _cachedWidths = [];
    private Style? _style;
    private double _availableWidth;

    /// <summary>Creates a score layouter using one SMuFL font's metrics.</summary>
    /// <param name="metadata">The selected font's glyph bounding boxes.</param>
    public IncrementalScoreLayouter(SmuflMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _measureWidthCalculator = new MeasureWidthCalculator(metadata);
    }

    /// <summary>Gets the latest successfully published layout, if one exists.</summary>
    public ScoreLayoutResult? Current { get; private set; }

    /// <summary>Measures every bar and chooses system breaks for a complete score.</summary>
    /// <param name="score">The immutable score snapshot.</param>
    /// <param name="style">The engraving style used to measure the score.</param>
    /// <param name="availableWidth">The usable width of each system, in staff spaces.</param>
    /// <param name="cancellationToken">Cancels the calculation without publishing a partial result.</param>
    /// <returns>The full layout and its per-measure width cache.</returns>
    public ScoreLayoutResult Layout(Score score, Style style, double availableWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(style);
        ValidateAvailableWidth(availableWidth);
        cancellationToken.ThrowIfCancellationRequested();

        SystemBreakMeasure[] candidateWidths = new SystemBreakMeasure[score.Measures.Length];
        for (int measureIndex = 0; measureIndex < candidateWidths.Length; measureIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidateWidths[measureIndex] = _measureWidthCalculator.Calculate(
                score, measureIndex, style, cancellationToken);
        }

        ImmutableArray<SystemLine> systems = _systemBreaker.Layout(
            candidateWidths, availableWidth, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ScoreLayoutResult result = new(ImmutableArray.CreateRange(candidateWidths), systems,
            candidateWidths.Length, systems.Length);

        _cachedWidths = candidateWidths;
        _style = style;
        _availableWidth = availableWidth;
        Current = result;
        return result;
    }

    /// <summary>Recalculates one edited measure and reflows until an old break is stable.</summary>
    /// <param name="score">The updated immutable score snapshot, with edits confined to the specified measure.</param>
    /// <param name="measureIndex">The zero-based measure containing the edit.</param>
    /// <param name="cancellationToken">Cancels the calculation without publishing a partial result.</param>
    /// <returns>The updated layout, reusing cached widths and systems where possible.</returns>
    /// <exception cref="InvalidOperationException">A full layout has not been calculated.</exception>
    public ScoreLayoutResult UpdateMeasure(Score score, int measureIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);
        cancellationToken.ThrowIfCancellationRequested();
        if (Current is null || _style is null)
        {
            throw new InvalidOperationException("Calculate a full layout before updating one measure.");
        }

        if (score.Measures.Length != _cachedWidths.Length)
        {
            throw new ArgumentException("An incremental update must keep the measure count unchanged.",
                nameof(score));
        }

        if (measureIndex < 0 || measureIndex >= _cachedWidths.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(measureIndex));
        }

        SystemBreakMeasure[] candidateWidths = (SystemBreakMeasure[])_cachedWidths.Clone();
        candidateWidths[measureIndex] = _measureWidthCalculator.Calculate(
            score, measureIndex, _style, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (candidateWidths[measureIndex] == _cachedWidths[measureIndex])
        {
            ScoreLayoutResult unchangedWidthResult = new(Current.MeasureWidths, Current.Systems,
                1, 0);
            Current = unchangedWidthResult;
            return unchangedWidthResult;
        }

        int affectedSystemIndex = FindSystemContaining(Current.Systems, measureIndex);
        int firstAffectedMeasure = Current.Systems[affectedSystemIndex].Range.StartIndex;
        ImmutableArray<SystemLine> localSystems = _systemBreaker.Layout(
            candidateWidths.AsSpan(firstAffectedMeasure), _availableWidth, cancellationToken);
        ImmutableArray<SystemLine>.Builder rebasedSystems =
            ImmutableArray.CreateBuilder<SystemLine>(localSystems.Length);
        foreach (SystemLine localSystem in localSystems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SystemLineMeasureRange localRange = localSystem.Range;
            rebasedSystems.Add(localSystem with
            {
                Range = new SystemLineMeasureRange(
                    firstAffectedMeasure + localRange.StartIndex, localRange.Count),
            });
        }

        ImmutableArray<SystemLine> newSuffix = rebasedSystems.MoveToImmutable();
        int newConvergenceIndex = FindConvergedSystem(newSuffix, Current.Systems,
            affectedSystemIndex, measureIndex, out int oldConvergenceIndex);
        ImmutableArray<SystemLine>.Builder mergedSystems = ImmutableArray.CreateBuilder<SystemLine>();
        for (int index = 0; index < affectedSystemIndex; index++)
        {
            mergedSystems.Add(Current.Systems[index]);
        }

        int newSystemsToKeep = newConvergenceIndex < 0 ? newSuffix.Length : newConvergenceIndex;
        for (int index = 0; index < newSystemsToKeep; index++)
        {
            mergedSystems.Add(newSuffix[index]);
        }

        int reflowedSystemCount = newSystemsToKeep;
        if (newConvergenceIndex >= 0)
        {
            for (int index = oldConvergenceIndex; index < Current.Systems.Length; index++)
            {
                mergedSystems.Add(Current.Systems[index]);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        ScoreLayoutResult result = new(ImmutableArray.CreateRange(candidateWidths),
            mergedSystems.ToImmutable(), 1, reflowedSystemCount);
        _cachedWidths = candidateWidths;
        Current = result;
        return result;
    }

    private static int FindSystemContaining(ImmutableArray<SystemLine> systems, int measureIndex)
    {
        for (int index = 0; index < systems.Length; index++)
        {
            SystemLineMeasureRange range = systems[index].Range;
            if (measureIndex >= range.StartIndex && measureIndex < range.StartIndex + range.Count)
            {
                return index;
            }
        }

        throw new InvalidOperationException("The current layout does not contain the edited measure.");
    }

    private static int FindConvergedSystem(ImmutableArray<SystemLine> newSystems,
        ImmutableArray<SystemLine> oldSystems, int affectedSystemIndex, int measureIndex,
        out int oldSystemIndex)
    {
        for (int newIndex = 0; newIndex < newSystems.Length; newIndex++)
        {
            SystemLineMeasureRange newRange = newSystems[newIndex].Range;
            if (newRange.StartIndex <= measureIndex)
            {
                continue;
            }

            for (int oldIndex = affectedSystemIndex + 1; oldIndex < oldSystems.Length; oldIndex++)
            {
                if (newRange == oldSystems[oldIndex].Range)
                {
                    oldSystemIndex = oldIndex;
                    return newIndex;
                }
            }
        }

        oldSystemIndex = -1;
        return -1;
    }

    private static void ValidateAvailableWidth(double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(availableWidth));
        }
    }
}
