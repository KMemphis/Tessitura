using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>Adds a spanner between two events; an identical one is not added twice.</summary>
/// <param name="Spanner">The spanner.</param>
public sealed record AddSpannerCommand(Spanner Spanner) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Add spanner";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (Spanner.Start == Spanner.End)
        {
            throw new ArgumentException("A spanner needs two different events.", nameof(Spanner));
        }

        ImmutableArray<Spanner> list = score.SpannerList;
        return list.Contains(Spanner) ? score : score with { Spanners = list.Add(Spanner) };
    }
}

/// <summary>Removes the spanners of a kind that start or end at an event.</summary>
/// <param name="Event">The event.</param>
/// <param name="Kind">The kind of spanner to remove.</param>
public sealed record RemoveSpannersCommand(EventId Event, SpannerKind Kind) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Remove spanners";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(score);
        return score with { Spanners = [.. score.SpannerList.Where(s => s.Kind != Kind || (s.Start != Event && s.End != Event))] };
    }
}
