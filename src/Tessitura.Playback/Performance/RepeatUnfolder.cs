using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Playback.Performance;

/// <summary>Describes one visit to a source measure in the expanded performance timeline.</summary>
/// <param name="MeasureIndex">The zero-based measure in the written score.</param>
/// <param name="Pass">The repeat pass used to select an alternative ending.</param>
/// <param name="PlaybackPosition">The exact start in whole-note units on the expanded timeline.</param>
/// <param name="StartsSegment">Whether a repeat or navigation jump begins a new playback segment here.</param>
public readonly record struct MeasureVisit(
    int MeasureIndex,
    int Pass,
    Fraction PlaybackPosition,
    bool StartsSegment);

/// <summary>Expands repeat barlines, alternative endings, and navigation jumps into measure visits.</summary>
public static class RepeatUnfolder
{
    /// <summary>Creates the ordered measure visits for a score's performance.</summary>
    /// <param name="score">The immutable score snapshot.</param>
    /// <returns>Every measure visit with its exact playback start and segment boundary.</returns>
    public static ImmutableArray<MeasureVisit> Unfold(Score score)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (score.Measures.IsDefaultOrEmpty)
        {
            return ImmutableArray<MeasureVisit>.Empty;
        }

        RepeatSpan[] spans = FindRepeatSpans(score.Measures);
        List<RepeatSpan>[] startsAt = new List<RepeatSpan>[score.Measures.Length];
        RepeatSpan?[] endsAt = new RepeatSpan?[score.Measures.Length];
        foreach (RepeatSpan span in spans)
        {
            (startsAt[span.Start] ??= []).Add(span);
            endsAt[span.End] = span;
        }

        int[] endingGroupMax = FindEndingGroupMaxima(score.Measures);
        int segno = FindTarget(score.Measures, RepeatTarget.Segno);
        int coda = FindTarget(score.Measures, RepeatTarget.Coda);
        List<RepeatFrame> frames = [];
        ImmutableArray<MeasureVisit>.Builder visits = ImmutableArray.CreateBuilder<MeasureVisit>();
        Fraction playbackPosition = Fraction.Zero;
        int measureIndex = 0;
        int completedPass = 1;
        bool jumped = false;
        bool fineArmed = false;
        bool codaArmed = false;
        bool codaJumped = false;
        bool startsSegment = true;
        int stepLimit = (int)Math.Min(100_000L, Math.Max(4_096L, (long)score.Measures.Length * 256));
        int steps = 0;

        while (measureIndex < score.Measures.Length)
        {
            if (++steps > stepLimit)
            {
                throw new InvalidOperationException("Repeat navigation exceeded the safe expansion limit.");
            }

            if (!jumped && startsAt[measureIndex] is { } newSpans)
            {
                foreach (RepeatSpan span in newSpans)
                {
                    bool alreadyActive = false;
                    foreach (RepeatFrame frame in frames)
                    {
                        if (frame.Span == span)
                        {
                            alreadyActive = true;
                            break;
                        }
                    }

                    if (!alreadyActive)
                    {
                        frames.Add(new RepeatFrame(span, 1));
                    }
                }
            }

            int pass = frames.Count > 0 ? frames[^1].Pass : completedPass;
            RepeatInfo? repeat = score.Measures[measureIndex].Repeat;
            ImmutableArray<int> endings = repeat?.Endings ?? ImmutableArray<int>.Empty;
            bool shouldPlay = endings.IsEmpty || (jumped
                ? endings.Contains(endingGroupMax[measureIndex])
                : endings.Contains(pass));
            if (shouldPlay)
            {
                visits.Add(new MeasureVisit(measureIndex, pass, playbackPosition, startsSegment));
                playbackPosition += score.Measures[measureIndex].TimeSignature.Length;
                startsSegment = false;
            }
            else if (!endings.IsEmpty)
            {
                // A skipped ending cannot carry a tie into the alternative that follows it.
                startsSegment = true;
            }

            RepeatSpan? endingSpan = endsAt[measureIndex];
            if (!jumped && endingSpan is not null && TryFindFrame(frames, endingSpan, out int frameIndex))
            {
                RepeatFrame frame = frames[frameIndex];
                if (frame.Pass < frame.Span.PassCount)
                {
                    frames[frameIndex] = frame with { Pass = frame.Pass + 1 };
                    measureIndex = frame.Span.Start;
                    startsSegment = true;
                    continue;
                }

                frames.RemoveAt(frameIndex);
                completedPass = frame.Pass;
            }

            RepeatJump jump = shouldPlay ? repeat?.Jump ?? RepeatJump.None : RepeatJump.None;
            if (jump == RepeatJump.Fine && fineArmed)
            {
                break;
            }

            if (!jumped && TryGetJumpDestination(jump, segno, out int destination,
                    out bool armFine, out bool armCoda))
            {
                jumped = true;
                fineArmed = armFine;
                codaArmed = armCoda;
                frames.Clear();
                measureIndex = destination;
                startsSegment = true;
                continue;
            }

            if (jump == RepeatJump.ToCoda && codaArmed && !codaJumped && coda >= 0)
            {
                codaJumped = true;
                frames.Clear();
                measureIndex = coda;
                startsSegment = true;
                continue;
            }

            measureIndex++;
        }

        return visits.ToImmutable();
    }

    private static RepeatSpan[] FindRepeatSpans(ImmutableArray<Measure> measures)
    {
        List<int> starts = [];
        List<RepeatSpan> spans = [];
        for (int index = 0; index < measures.Length; index++)
        {
            RepeatInfo? repeat = measures[index].Repeat;
            if (repeat?.StartRepeat == true)
            {
                starts.Add(index);
            }

            if (repeat?.EndRepeat is int passCount)
            {
                int start = starts.Count > 0 ? starts[^1] : 0;
                if (starts.Count > 0)
                {
                    starts.RemoveAt(starts.Count - 1);
                }

                spans.Add(new RepeatSpan(start, index, passCount));
            }
        }

        return [.. spans];
    }

    private static int[] FindEndingGroupMaxima(ImmutableArray<Measure> measures)
    {
        int[] maxima = new int[measures.Length];
        int index = 0;
        while (index < measures.Length)
        {
            ImmutableArray<int> endings = measures[index].Repeat?.Endings ?? ImmutableArray<int>.Empty;
            if (endings.IsEmpty)
            {
                index++;
                continue;
            }

            int first = index;
            int maximum = 0;
            while (index < measures.Length)
            {
                endings = measures[index].Repeat?.Endings ?? ImmutableArray<int>.Empty;
                if (endings.IsEmpty)
                {
                    break;
                }

                foreach (int ending in endings)
                {
                    maximum = Math.Max(maximum, ending);
                }

                index++;
            }

            for (int groupIndex = first; groupIndex < index; groupIndex++)
            {
                maxima[groupIndex] = maximum;
            }
        }

        return maxima;
    }

    private static int FindTarget(ImmutableArray<Measure> measures, RepeatTarget target)
    {
        for (int index = 0; index < measures.Length; index++)
        {
            if (measures[index].Repeat?.Target == target)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryFindFrame(List<RepeatFrame> frames, RepeatSpan span, out int index)
    {
        for (index = frames.Count - 1; index >= 0; index--)
        {
            if (frames[index].Span == span)
            {
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static bool TryGetJumpDestination(RepeatJump jump, int segno,
        out int destination, out bool armFine, out bool armCoda)
    {
        armFine = false;
        armCoda = false;
        destination = jump switch
        {
            RepeatJump.DaCapo or RepeatJump.DaCapoAlFine or RepeatJump.DaCapoAlCoda => 0,
            RepeatJump.DalSegno or RepeatJump.DalSegnoAlFine or RepeatJump.DalSegnoAlCoda => segno >= 0 ? segno : 0,
            _ => -1,
        };
        armFine = jump is RepeatJump.DaCapoAlFine or RepeatJump.DalSegnoAlFine;
        armCoda = jump is RepeatJump.DaCapoAlCoda or RepeatJump.DalSegnoAlCoda;
        return destination >= 0;
    }

    private sealed record RepeatSpan(int Start, int End, int PassCount);

    private sealed record RepeatFrame(RepeatSpan Span, int Pass);
}
