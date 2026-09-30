using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>Replaces all repeat and navigation markings on the measure in the edit context.</summary>
/// <param name="Repeat">The new markings, or null to clear them.</param>
public sealed record SetMeasureRepeatCommand(RepeatInfo? Repeat) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Change repeat markings";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (context.MeasureIndex >= score.Measures.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }

        ImmutableArray<Measure> measures = score.Measures.SetItem(
            context.MeasureIndex,
            score.Measures[context.MeasureIndex] with { Repeat = Repeat });
        return score with { Measures = measures };
    }
}
