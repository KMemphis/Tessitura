using Tessitura.Core;

namespace Tessitura.Playback.Performance;

/// <summary>Converts score positions to seconds through piecewise-constant tempos.</summary>
public sealed class TempoMap
{
    private readonly (Fraction Position, double Bpm, double SecondsAtStart)[] _segments;

    /// <summary>Creates a tempo map.</summary>
    /// <param name="defaultTempo">The tempo at position zero unless a mark says otherwise.</param>
    /// <param name="marks">The tempo changes; later marks at the same position win.</param>
    public TempoMap(double defaultTempo, IEnumerable<TempoMark> marks)
    {
        ArgumentNullException.ThrowIfNull(marks);
        Validate(defaultTempo);
        List<TempoMark> ordered = [];
        foreach (TempoMark mark in marks)
        {
            Validate(mark.QuarterNotesPerMinute);
            if (mark.Position < Fraction.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(marks), "A tempo mark cannot precede the score.");
            }

            ordered.Add(mark);
        }

        // Stable sort so that, at equal positions, the last mark supplied is the last applied.
        List<TempoMark> sorted = [.. ordered.Select((m, i) => (m, i)).OrderBy(p => p.m.Position).ThenBy(p => p.i).Select(p => p.m)];
        List<(Fraction, double, double)> segments = [(Fraction.Zero, defaultTempo, 0)];
        foreach (TempoMark mark in sorted)
        {
            (Fraction lastPosition, double lastBpm, double lastSeconds) = segments[^1];
            if (mark.Position == lastPosition)
            {
                segments[^1] = (lastPosition, mark.QuarterNotesPerMinute, lastSeconds);
                continue;
            }

            double seconds = lastSeconds + Quarters(mark.Position - lastPosition) * 60.0 / lastBpm;
            segments.Add((mark.Position, mark.QuarterNotesPerMinute, seconds));
        }

        _segments = [.. segments];
    }

    /// <summary>Gets the tempo in force at a position, in quarter notes per minute.</summary>
    /// <param name="position">The absolute position in whole-note units.</param>
    /// <returns>The tempo.</returns>
    public double QuarterNotesPerMinuteAt(Fraction position) => _segments[SegmentAt(position)].Bpm;

    /// <summary>Converts a position to seconds from the start of the score.</summary>
    /// <param name="position">The absolute position in whole-note units.</param>
    /// <returns>The elapsed time in seconds.</returns>
    public double SecondsAt(Fraction position)
    {
        if (position < Fraction.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        (Fraction start, double bpm, double seconds) = _segments[SegmentAt(position)];
        return seconds + Quarters(position - start) * 60.0 / bpm;
    }

    /// <summary>Converts elapsed seconds to a score position, rounded to 1/4096 of a whole note.</summary>
    /// <param name="seconds">The elapsed time, not negative.</param>
    /// <returns>The absolute position in whole-note units.</returns>
    public Fraction PositionAt(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        int index = 0;
        for (int i = 1; i < _segments.Length && _segments[i].SecondsAtStart <= seconds; i++)
        {
            index = i;
        }

        (Fraction start, double bpm, double at) = _segments[index];
        double quarters = (seconds - at) * bpm / 60.0;
        long units = (long)Math.Round(quarters * 1024); // 1/4096 whole = 1/1024 quarter
        return start + new Fraction(units, 4096);
    }

    private int SegmentAt(Fraction position)
    {
        int index = 0;
        for (int i = 1; i < _segments.Length && _segments[i].Position <= position; i++)
        {
            index = i;
        }

        return index;
    }

    // Musical time stays exact until the single conversion to real time.
    private static double Quarters(Fraction wholeNotes)
    {
        Fraction quarters = wholeNotes * new Fraction(4, 1);
        return (double)quarters.Num / quarters.Den;
    }

    private static void Validate(double tempo)
    {
        if (!double.IsFinite(tempo) || tempo <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tempo), "A tempo must be a positive finite number.");
        }
    }
}
