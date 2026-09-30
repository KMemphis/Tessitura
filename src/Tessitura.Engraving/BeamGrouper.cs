using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Engraving;

/// <summary>Describes one ordered rhythmic event for automatic beaming.</summary>
/// <param name="Onset">The exact onset within the measure.</param>
/// <param name="Length">The exact duration.</param>
/// <param name="IsRest">Whether the event is a rest.</param>
public readonly record struct BeamEvent(Fraction Onset, Fraction Length, bool IsRest);

/// <summary>Identifies a consecutive range of events joined by one primary beam.</summary>
/// <param name="StartIndex">The zero-based index of the first event.</param>
/// <param name="Count">The number of events in the group.</param>
public readonly record struct BeamGroup(int StartIndex, int Count);

/// <summary>Chooses stem directions and primary beam groups for common metres.</summary>
public sealed class BeamGrouper
{
    /// <summary>Chooses the single-note stem direction from the middle staff line.</summary>
    /// <param name="halfSpacesFromMiddle">The note position, positive above the middle line.</param>
    /// <returns>Up below the middle line; down on or above it.</returns>
    public static StemDirection ChooseStemDirection(int halfSpacesFromMiddle)
    {
        // Behind Bars, Ground Rules > Stems: notes on the middle line default down.
        return halfSpacesFromMiddle < 0 ? StemDirection.Up : StemDirection.Down;
    }

    /// <summary>Groups beamable events without crossing the metre's beat divisions.</summary>
    /// <param name="events">Events ordered by onset within one measure.</param>
    /// <param name="meter">The measure's time signature.</param>
    /// <returns>Groups of at least two consecutive beamable events.</returns>
    public ImmutableArray<BeamGroup> Group(ReadOnlySpan<BeamEvent> events, TimeSignature meter)
    {
        Fraction bucketLength = BeatGroupLength(meter);
        Fraction eighth = new(1, 8);
        ImmutableArray<BeamGroup>.Builder groups = ImmutableArray.CreateBuilder<BeamGroup>();
        int currentStart = -1;
        int currentCount = 0;
        long currentBucket = -1;
        Fraction previousEnd = Fraction.Zero;

        for (int index = 0; index < events.Length; index++)
        {
            BeamEvent item = events[index];
            Fraction end = item.Onset + item.Length;
            if (item.Onset < Fraction.Zero || item.Length <= Fraction.Zero ||
                end > meter.Length || item.Onset < previousEnd)
            {
                throw new ArgumentOutOfRangeException(nameof(events),
                    "Events must be nonoverlapping and contained in the measure.");
            }

            Fraction quotient = item.Onset / bucketLength;
            long bucket = quotient.Num / quotient.Den;
            Fraction bucketEnd = new Fraction(bucket + 1, 1) * bucketLength;
            bool beamable = !item.IsRest && item.Length <= eighth && end <= bucketEnd;
            bool continues = beamable && currentStart >= 0 &&
                bucket == currentBucket && item.Onset == previousEnd;
            if (!continues)
            {
                if (currentCount >= 2)
                {
                    groups.Add(new BeamGroup(currentStart, currentCount));
                }

                currentStart = beamable ? index : -1;
                currentCount = beamable ? 1 : 0;
                currentBucket = beamable ? bucket : -1;
            }
            else
            {
                currentCount++;
            }

            previousEnd = end;
        }

        if (currentCount >= 2)
        {
            groups.Add(new BeamGroup(currentStart, currentCount));
        }

        return groups.ToImmutable();
    }

    private static Fraction BeatGroupLength(TimeSignature meter)
    {
        // Behind Bars, Metre > Beaming according to the metre (p. 153):
        // show the beat; 4/4 may beam within each half-bar, never across its middle.
        return (meter.Numerator, meter.Denominator) switch
        {
            (2, 4) or (3, 4) => new Fraction(1, 4),
            (4, 4) or (2, 2) => new Fraction(1, 2),
            (6, 8) or (9, 8) => new Fraction(3, 8),
            _ => throw new ArgumentOutOfRangeException(nameof(meter),
                "Automatic beaming is not defined for this metre yet."),
        };
    }
}
