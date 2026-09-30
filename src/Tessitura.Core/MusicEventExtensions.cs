using System.Collections.Immutable;

namespace Tessitura.Core;

/// <summary>Helpers for working with events, including those nested in tuplet groups.</summary>
public static class MusicEventExtensions
{
    /// <summary>Returns a copy of an event with a different onset.</summary>
    /// <param name="musicEvent">The event.</param>
    /// <param name="onset">The new onset.</param>
    /// <returns>The moved copy.</returns>
    public static MusicEvent WithOnset(this MusicEvent musicEvent, Fraction onset)
    {
        ArgumentNullException.ThrowIfNull(musicEvent);
        return musicEvent switch
        {
            Chord chord => chord with { Onset = onset },
            Rest rest => rest with { Onset = onset },
            TupletGroup group => group with { Onset = onset },
            _ => throw new ArgumentException("Unknown event type.", nameof(musicEvent)),
        };
    }

    /// <summary>Lists the chords and rests of a voice with tuplets flattened: each leaf comes with its onset from the measure start and the time it sounds.</summary>
    /// <param name="events">The voice's events.</param>
    /// <returns>The leaves in order.</returns>
    public static ImmutableArray<(MusicEvent Event, Fraction Onset, Fraction Length)> Flatten(this IEnumerable<MusicEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        ImmutableArray<(MusicEvent, Fraction, Fraction)>.Builder result = ImmutableArray.CreateBuilder<(MusicEvent, Fraction, Fraction)>();
        foreach (MusicEvent musicEvent in events)
        {
            Add(result, musicEvent, Fraction.Zero, Fraction.One, Fraction.One);
        }

        return result.ToImmutable();
    }

    // Each group is a self-contained frame: member onsets count from the group start in the group's own sounding
    // time. A member's real onset is scaled by the ratios of the groups above the group that holds it, and a leaf's
    // real length is its notated length scaled by the ratios of all the groups that contain it.
    private static void Add(ImmutableArray<(MusicEvent, Fraction, Fraction)>.Builder result, MusicEvent musicEvent,
        Fraction parentStart, Fraction onsetScale, Fraction lengthScale)
    {
        Fraction start = parentStart + musicEvent.Onset * onsetScale;
        if (musicEvent is TupletGroup group)
        {
            foreach (MusicEvent child in group.Children)
            {
                Add(result, child, start, lengthScale, lengthScale * group.Ratio);
            }

            return;
        }

        result.Add((musicEvent, start, musicEvent.Length * lengthScale));
    }
}
