using System.Collections.Immutable;

namespace Tessitura.Engraving;

/// <summary>Describes the minimum and ideal width of one measure and its stretch weight.</summary>
/// <param name="MinimumWidth">The collision-safe minimum width in staff spaces.</param>
/// <param name="IdealWidth">The preferred width in staff spaces.</param>
/// <param name="Elasticity">The measure's relative share of additional width.</param>
public readonly record struct SystemBreakMeasure(
    double MinimumWidth, double IdealWidth, double Elasticity);

/// <summary>Identifies a consecutive range of measures assigned to one system.</summary>
/// <param name="StartIndex">The zero-based first measure.</param>
/// <param name="Count">The number of measures in the system.</param>
public readonly record struct SystemLineMeasureRange(int StartIndex, int Count);

/// <summary>Contains one justified system and the final width of each measure.</summary>
/// <param name="Range">The measures assigned to this system.</param>
/// <param name="NaturalWidth">The sum of ideal measure widths before justification.</param>
/// <param name="PlacedWidth">The sum of measure widths after justification.</param>
/// <param name="MeasureWidths">The final width of each measure in order.</param>
/// <param name="IsLast">Whether this is the final, ragged system.</param>
public sealed record SystemLine(
    SystemLineMeasureRange Range,
    double NaturalWidth,
    double PlacedWidth,
    ImmutableArray<double> MeasureWidths,
    bool IsLast);

/// <summary>Chooses system breaks by minimizing line badness and justifies by elasticity.</summary>
public sealed class SystemBreaker
{
    // The 80% floor follows the F1.11 acceptance criterion in docs/plan.md;
    // the complementary stretch limit prevents a non-final system being forced wider.
    private const double MinimumNaturalFill = 0.8;
    private const double MaximumStretch = 0.2;
    // Knuth-Plass style break selection is specified in the project definition,
    // Engraving > Stages > System breaks. Tessitura limits compression to 12%.
    private const double MaximumCompression = 0.12;
    private const double WidthEpsilon = 1e-9;
    private const double NonFinalBadnessWeight = 100;
    private const double FinalBadnessWeight = 20;

    /// <summary>Finds a minimum-badness set of system breaks and assigns measure widths.</summary>
    /// <param name="measures">Measures in score order with minimum, ideal, and elastic widths.</param>
    /// <param name="availableWidth">The system width after margins, in staff spaces.</param>
    /// <returns>The ordered systems. The last system remains ragged.</returns>
    public ImmutableArray<SystemLine> Layout(ReadOnlySpan<SystemBreakMeasure> measures,
        double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(availableWidth));
        }

        if (measures.IsEmpty)
        {
            return ImmutableArray<SystemLine>.Empty;
        }

        double[] idealPrefix = new double[measures.Length + 1];
        double[] minimumPrefix = new double[measures.Length + 1];
        double[] elasticityPrefix = new double[measures.Length + 1];
        double[] shrinkPrefix = new double[measures.Length + 1];
        for (int index = 0; index < measures.Length; index++)
        {
            SystemBreakMeasure measure = measures[index];
            if (!double.IsFinite(measure.MinimumWidth) || measure.MinimumWidth <= 0 ||
                !double.IsFinite(measure.IdealWidth) || measure.IdealWidth < measure.MinimumWidth ||
                !double.IsFinite(measure.Elasticity) || measure.Elasticity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(measures),
                    "Measure widths and elasticity must be finite and positive, with ideal at least minimum.");
            }

            idealPrefix[index + 1] = idealPrefix[index] + measure.IdealWidth;
            minimumPrefix[index + 1] = minimumPrefix[index] + measure.MinimumWidth;
            elasticityPrefix[index + 1] = elasticityPrefix[index] + measure.Elasticity;
            shrinkPrefix[index + 1] = shrinkPrefix[index] + measure.IdealWidth - measure.MinimumWidth;
        }

        int count = measures.Length;
        double[] bestCost = new double[count + 1];
        int[] nextBreak = new int[count + 1];
        Array.Fill(bestCost, double.PositiveInfinity);
        Array.Fill(nextBreak, -1);
        bestCost[count] = 0;

        for (int start = count - 1; start >= 0; start--)
        {
            for (int end = start + 1; end <= count; end++)
            {
                bool isLast = end == count;
                double idealWidth = idealPrefix[end] - idealPrefix[start];
                double minimumWidth = minimumPrefix[end] - minimumPrefix[start];
                if (minimumWidth > availableWidth + WidthEpsilon ||
                    !isLast && idealWidth < availableWidth * MinimumNaturalFill ||
                    double.IsPositiveInfinity(bestCost[end]))
                {
                    continue;
                }

                double difference = Math.Abs(availableWidth - idealWidth);
                if (!isLast && idealWidth < availableWidth &&
                    difference > availableWidth * MaximumStretch)
                {
                    continue;
                }

                if (idealWidth > availableWidth &&
                    (idealWidth - availableWidth > shrinkPrefix[end] - shrinkPrefix[start] + WidthEpsilon ||
                     (idealWidth - availableWidth) / idealWidth > MaximumCompression))
                {
                    continue;
                }

                double ratio = difference / availableWidth;
                double weight = isLast ? FinalBadnessWeight : NonFinalBadnessWeight;
                double cost = weight * ratio * ratio * ratio + bestCost[end];
                if (cost < bestCost[start] ||
                    cost == bestCost[start] && end - start > nextBreak[start] - start)
                {
                    bestCost[start] = cost;
                    nextBreak[start] = end;
                }
            }
        }

        if (nextBreak[0] < 0)
        {
            throw new InvalidOperationException("No valid system breaks fit the available width.");
        }

        ImmutableArray<SystemLine>.Builder systems = ImmutableArray.CreateBuilder<SystemLine>();
        int lineStart = 0;
        while (lineStart < count)
        {
            int lineEnd = nextBreak[lineStart];
            bool isLast = lineEnd == count;
            double idealWidth = idealPrefix[lineEnd] - idealPrefix[lineStart];
            double placedWidth = idealWidth < availableWidth && !isLast
                ? availableWidth
                : idealWidth;
            ImmutableArray<double>.Builder widths =
                ImmutableArray.CreateBuilder<double>(lineEnd - lineStart);

            if (idealWidth < availableWidth && !isLast)
            {
                double extra = availableWidth - idealWidth;
                double lineElasticity = elasticityPrefix[lineEnd] - elasticityPrefix[lineStart];
                for (int index = lineStart; index < lineEnd; index++)
                {
                    double share = extra * measures[index].Elasticity / lineElasticity;
                    widths.Add(measures[index].IdealWidth + share);
                }
            }
            else if (idealWidth > availableWidth)
            {
                double deficit = idealWidth - availableWidth;
                double lineShrink = shrinkPrefix[lineEnd] - shrinkPrefix[lineStart];
                for (int index = lineStart; index < lineEnd; index++)
                {
                    SystemBreakMeasure measure = measures[index];
                    double capacity = measure.IdealWidth - measure.MinimumWidth;
                    widths.Add(measure.IdealWidth - deficit * capacity / lineShrink);
                }

                placedWidth = availableWidth;
            }
            else
            {
                for (int index = lineStart; index < lineEnd; index++)
                {
                    widths.Add(measures[index].IdealWidth);
                }
            }

            systems.Add(new SystemLine(new SystemLineMeasureRange(lineStart, lineEnd - lineStart),
                idealWidth, placedWidth, widths.MoveToImmutable(), isLast));
            lineStart = lineEnd;
        }

        return systems.ToImmutable();
    }
}
