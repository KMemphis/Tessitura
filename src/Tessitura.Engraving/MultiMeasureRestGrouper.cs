using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Engraving;

/// <summary>Identifies a consecutive group of measures represented by one multi-measure rest.</summary>
/// <param name="StartMeasure">The zero-based index of the first silent measure.</param>
/// <param name="MeasureCount">The number of consecutive silent measures.</param>
public readonly record struct MultiMeasureRestGroup(int StartMeasure, int MeasureCount);

/// <summary>Finds groups of consecutive full-measure rests in a single-instrument part.</summary>
public static class MultiMeasureRestGrouper
{
    /// <summary>Finds eligible groups without crossing changes that need visible notation.</summary>
    /// <param name="score">A projected score containing one instrument.</param>
    /// <returns>Groups with at least two consecutive full-measure rests.</returns>
    public static ImmutableArray<MultiMeasureRestGroup> FindGroups(Score score)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (score.Instruments.Length != 1 || score.Measures.Length < 2)
        {
            return ImmutableArray<MultiMeasureRestGroup>.Empty;
        }

        int staffCount = score.Instruments[0].Staves.Length;
        if (staffCount == 0)
        {
            return ImmutableArray<MultiMeasureRestGroup>.Empty;
        }

        ImmutableArray<MultiMeasureRestGroup>.Builder groups = ImmutableArray.CreateBuilder<MultiMeasureRestGroup>();
        int runStart = -1;
        for (int measureIndex = 0; measureIndex < score.Measures.Length; measureIndex++)
        {
            bool eligible = score.Measures[measureIndex].Repeat is null;
            for (int staffIndex = 0; eligible && staffIndex < staffCount; staffIndex++)
            {
                eligible = IsFullMeasureRest(score, staffIndex, measureIndex);
            }

            if (eligible && runStart >= 0)
            {
                Measure previous = score.Measures[measureIndex - 1];
                Measure current = score.Measures[measureIndex];
                eligible = current.TimeSignature == previous.TimeSignature &&
                    current.KeySignature == previous.KeySignature;
            }

            if (!eligible)
            {
                AddGroup(groups, runStart, measureIndex - runStart);
                runStart = -1;
            }
            else if (runStart < 0)
            {
                runStart = measureIndex;
            }
        }

        AddGroup(groups, runStart, score.Measures.Length - runStart);
        return groups.ToImmutable();
    }

    private static bool IsFullMeasureRest(Score score, int staffIndex, int measureIndex)
    {
        if (!score.Content.TryGetValue(new StaffMeasureKey(staffIndex, measureIndex), out StaffMeasure? staffMeasure) ||
            staffMeasure.Voices.IsDefaultOrEmpty)
        {
            return false;
        }

        Fraction measureLength = score.Measures[measureIndex].TimeSignature.Length;
        foreach (Voice voice in staffMeasure.Voices)
        {
            if (voice.Events.Length != 1 || voice.Events[0] is not Rest rest ||
                rest.Onset != Fraction.Zero || rest.Length != measureLength || HasAnchoredMark(score, rest.Id))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAnchoredMark(Score score, EventId eventId)
    {
        foreach (Attachment attachment in score.AttachmentList)
        {
            if (attachment.Target == eventId)
            {
                return true;
            }
        }

        foreach (Spanner spanner in score.SpannerList)
        {
            if (spanner.Start == eventId || spanner.End == eventId)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddGroup(ImmutableArray<MultiMeasureRestGroup>.Builder groups,
        int startMeasure, int measureCount)
    {
        if (startMeasure >= 0 && measureCount >= 2)
        {
            groups.Add(new MultiMeasureRestGroup(startMeasure, measureCount));
        }
    }
}
