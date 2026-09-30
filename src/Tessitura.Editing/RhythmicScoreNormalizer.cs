using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Editing;

internal static class RhythmicScoreNormalizer
{
    public static Score NormalizeVoice(Score score, EditContext context)
    {
        if (score.Measures.IsDefaultOrEmpty || context.MeasureIndex >= score.Measures.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(context));
        }

        (Fraction[] measureStarts, Fraction originalLength) = GetMeasureStarts(score.Measures);
        List<LocatedEvent> sourceEvents = CollectEvents(score, context, measureStarts);
        if (sourceEvents.Count == 0)
        {
            throw new KeyNotFoundException("The selected voice does not contain any events.");
        }

        Fraction requiredLength = GetRequiredLength(sourceEvents);
        score = AddMeasuresWhenNeeded(score, context, requiredLength, ref measureStarts, ref originalLength);

        List<MusicEvent>[] normalizedEvents = new List<MusicEvent>[score.Measures.Length];
        for (int index = 0; index < normalizedEvents.Length; index++)
        {
            normalizedEvents[index] = [];
        }

        Fraction cursor = Fraction.Zero;
        foreach (LocatedEvent locatedEvent in sourceEvents)
        {
            switch (locatedEvent.Event)
            {
                case Rest rest:
                {
                    Fraction restEnd = locatedEvent.Onset + rest.Duration.Length;
                    if (restEnd > originalLength)
                    {
                        restEnd = originalLength;
                    }

                    if (restEnd > cursor)
                    {
                        bool preferredIdUsed = false;
                        AppendRests(
                            cursor,
                            restEnd,
                            rest.Id,
                            ref preferredIdUsed,
                            score.Measures,
                            measureStarts,
                            normalizedEvents);
                        cursor = restEnd;
                    }

                    break;
                }
                case Chord chord:
                {
                    if (locatedEvent.Onset > cursor)
                    {
                        bool preferredIdUsed = false;
                        AppendRests(
                            cursor,
                            locatedEvent.Onset,
                            null,
                            ref preferredIdUsed,
                            score.Measures,
                            measureStarts,
                            normalizedEvents);
                        cursor = locatedEvent.Onset;
                    }

                    cursor = AppendChord(
                        chord,
                        cursor,
                        score.Measures,
                        measureStarts,
                        normalizedEvents);
                    break;
                }
                default:
                    throw new InvalidOperationException(
                        $"Rhythmic normalization does not support {locatedEvent.Event.GetType().Name} events.");
            }
        }

        if (cursor < originalLength)
        {
            bool preferredIdUsed = false;
            AppendRests(
                cursor,
                originalLength,
                null,
                ref preferredIdUsed,
                score.Measures,
                measureStarts,
                normalizedEvents);
        }
        else if (cursor > originalLength)
        {
            throw new InvalidOperationException("The edited voice extends beyond the score timeline.");
        }

        return ReplaceVoice(score, context, normalizedEvents);
    }

    private static List<LocatedEvent> CollectEvents(Score score, EditContext context, Fraction[] measureStarts)
    {
        List<LocatedEvent> events = [];
        for (int measureIndex = 0; measureIndex < score.Measures.Length; measureIndex++)
        {
            StaffMeasureKey key = new(context.StaffIndex, measureIndex);
            if (!score.Content.TryGetValue(key, out StaffMeasure? staffMeasure))
            {
                continue;
            }

            int voiceIndex = FindVoiceIndex(staffMeasure, context.VoiceNumber);
            if (voiceIndex < 0)
            {
                continue;
            }

            Voice voice = staffMeasure.Voices[voiceIndex];
            foreach (MusicEvent musicEvent in voice.Events)
            {
                events.Add(new LocatedEvent(measureStarts[measureIndex] + musicEvent.Onset, musicEvent));
            }
        }

        return events;
    }

    private static Fraction GetRequiredLength(List<LocatedEvent> sourceEvents)
    {
        Fraction cursor = Fraction.Zero;
        foreach (LocatedEvent locatedEvent in sourceEvents)
        {
            if (locatedEvent.Event is Rest rest)
            {
                Fraction restEnd = locatedEvent.Onset + rest.Duration.Length;
                if (restEnd > cursor)
                {
                    cursor = restEnd;
                }
            }
            else
            {
                Fraction start = locatedEvent.Onset > cursor ? locatedEvent.Onset : cursor;
                cursor = start + locatedEvent.Event.Duration.Length;
            }
        }

        return cursor;
    }

    private static Score AddMeasuresWhenNeeded(
        Score score,
        EditContext context,
        Fraction requiredLength,
        ref Fraction[] measureStarts,
        ref Fraction scoreLength)
    {
        if (requiredLength <= scoreLength)
        {
            return score;
        }

        int staffCount = GetStaffCount(score, context.StaffIndex + 1);
        List<int>[] voiceNumbers = GetVoiceNumbers(score, staffCount);
        ImmutableArray<Measure>.Builder measures = score.Measures.ToBuilder();
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content = score.Content.ToBuilder();

        while (scoreLength < requiredLength)
        {
            Measure previousMeasure = measures[^1];
            Measure newMeasure = new(previousMeasure.Number + 1, previousMeasure.TimeSignature);
            int newMeasureIndex = measures.Count;
            measures.Add(newMeasure);

            for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
            {
                ImmutableArray<Voice>.Builder voices = ImmutableArray.CreateBuilder<Voice>(voiceNumbers[staffIndex].Count);
                foreach (int voiceNumber in voiceNumbers[staffIndex])
                {
                    ImmutableArray<MusicEvent> rests = CreateFullMeasureRests(newMeasure.TimeSignature);
                    voices.Add(new Voice(voiceNumber, rests));
                }

                content.Add(
                    new StaffMeasureKey(staffIndex, newMeasureIndex),
                    new StaffMeasure(voices.MoveToImmutable()));
            }

            scoreLength += newMeasure.TimeSignature.Length;
        }

        score = score with { Measures = measures.ToImmutable(), Content = content.ToImmutable() };
        (measureStarts, scoreLength) = GetMeasureStarts(score.Measures);
        return score;
    }

    private static int GetStaffCount(Score score, int minimumCount)
    {
        int count = 0;
        foreach (Instrument instrument in score.Instruments)
        {
            count += instrument.Staves.Length;
        }

        foreach (KeyValuePair<StaffMeasureKey, StaffMeasure> entry in score.Content)
        {
            count = Math.Max(count, entry.Key.StaffIndex + 1);
        }

        return Math.Max(count, minimumCount);
    }

    private static List<int>[] GetVoiceNumbers(Score score, int staffCount)
    {
        List<int>[] voicesByStaff = new List<int>[staffCount];
        for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
        {
            voicesByStaff[staffIndex] = [];
        }

        foreach (KeyValuePair<StaffMeasureKey, StaffMeasure> entry in score.Content)
        {
            int staffIndex = entry.Key.StaffIndex;
            if (staffIndex < 0 || staffIndex >= staffCount)
            {
                continue;
            }

            foreach (Voice voice in entry.Value.Voices)
            {
                if (!voicesByStaff[staffIndex].Contains(voice.Number))
                {
                    voicesByStaff[staffIndex].Add(voice.Number);
                }
            }
        }

        foreach (List<int> voices in voicesByStaff)
        {
            if (voices.Count == 0)
            {
                voices.Add(1);
            }
        }

        return voicesByStaff;
    }

    internal static ImmutableArray<MusicEvent> CreateFullMeasureRests(TimeSignature timeSignature)
    {
        ImmutableArray<MusicEvent>.Builder rests = ImmutableArray.CreateBuilder<MusicEvent>();
        Fraction onset = Fraction.Zero;
        foreach (Duration duration in DecomposeDuration(timeSignature.Length))
        {
            rests.Add(new Rest(CreateEventId(), onset, duration));
            onset += duration.Length;
        }

        return rests.ToImmutable();
    }

    private static void AppendRests(
        Fraction start,
        Fraction end,
        EventId? preferredId,
        ref bool preferredIdUsed,
        ImmutableArray<Measure> measures,
        Fraction[] measureStarts,
        List<MusicEvent>[] output)
    {
        Fraction cursor = start;
        while (cursor < end)
        {
            int measureIndex = FindMeasureIndex(cursor, measures, measureStarts);
            if (measureIndex < 0)
            {
                throw new InvalidOperationException("A rest cannot be placed outside the score timeline.");
            }

            Fraction measureStart = measureStarts[measureIndex];
            TimeSignature timeSignature = measures[measureIndex].TimeSignature;
            Fraction measureEnd = measureStart + timeSignature.Length;
            Fraction rangeEnd = end < measureEnd ? end : measureEnd;

            if (cursor == measureStart && rangeEnd == measureEnd)
            {
                AppendRestPieces(
                    cursor,
                    rangeEnd,
                    measureIndex,
                    preferredId,
                    ref preferredIdUsed,
                    measureStart,
                    output);
                cursor = rangeEnd;
                continue;
            }

            Fraction beatLength = GetBeatLength(timeSignature);
            Fraction nextBeatBoundary = measureStart + beatLength;
            while (nextBeatBoundary <= cursor && nextBeatBoundary < measureEnd)
            {
                nextBeatBoundary += beatLength;
            }

            Fraction beatEnd = rangeEnd < nextBeatBoundary ? rangeEnd : nextBeatBoundary;
            AppendRestPieces(
                cursor,
                beatEnd,
                measureIndex,
                preferredId,
                ref preferredIdUsed,
                measureStart,
                output);
            cursor = beatEnd;
        }
    }

    private static Fraction GetBeatLength(TimeSignature timeSignature)
    {
        bool compound = timeSignature.Denominator == 8 &&
            timeSignature.Numerator >= 6 &&
            timeSignature.Numerator % 3 == 0;
        return new Fraction(compound ? 3 : 1, timeSignature.Denominator);
    }

    private static void AppendRestPieces(
        Fraction start,
        Fraction end,
        int measureIndex,
        EventId? preferredId,
        ref bool preferredIdUsed,
        Fraction measureStart,
        List<MusicEvent>[] output)
    {
        Fraction onset = start;
        foreach (Duration duration in DecomposeDuration(end - start))
        {
            EventId id;
            if (preferredId.HasValue && !preferredIdUsed)
            {
                id = preferredId.Value;
                preferredIdUsed = true;
            }
            else
            {
                id = CreateEventId();
            }

            output[measureIndex].Add(new Rest(id, onset - measureStart, duration));
            onset += duration.Length;
        }
    }

    private static Fraction AppendChord(
        Chord chord,
        Fraction start,
        ImmutableArray<Measure> measures,
        Fraction[] measureStarts,
        List<MusicEvent>[] output)
    {
        if (chord.Notes.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException("A chord must contain at least one note.");
        }

        List<ChordFragment> fragments = [];
        Fraction cursor = start;
        Fraction remaining = chord.Duration.Length;
        while (remaining > Fraction.Zero)
        {
            int measureIndex = FindMeasureIndex(cursor, measures, measureStarts);
            if (measureIndex < 0)
            {
                throw new InvalidOperationException("The edited chord extends beyond the score timeline.");
            }

            Fraction measureStart = measureStarts[measureIndex];
            Fraction measureEnd = measureStart + measures[measureIndex].TimeSignature.Length;
            Fraction segmentEnd = cursor + remaining;
            if (segmentEnd > measureEnd)
            {
                segmentEnd = measureEnd;
            }

            foreach (Duration duration in DecomposeDuration(segmentEnd - cursor))
            {
                fragments.Add(new ChordFragment(measureIndex, cursor - measureStart, duration));
                cursor += duration.Length;
                remaining -= duration.Length;
            }
        }

        for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
        {
            ChordFragment fragment = fragments[fragmentIndex];
            ImmutableArray<Note>.Builder notes = ImmutableArray.CreateBuilder<Note>(chord.Notes.Length);
            foreach (Note note in chord.Notes)
            {
                notes.Add(fragmentIndex < fragments.Count - 1
                    ? note with { TiedToNext = true }
                    : note);
            }

            EventId id = fragmentIndex == 0 ? chord.Id : CreateEventId();
            output[fragment.MeasureIndex].Add(
                new Chord(id, fragment.Onset, fragment.Duration, notes.MoveToImmutable(), chord.Stem));
        }

        return cursor;
    }

    private static List<Duration> DecomposeDuration(Fraction length)
    {
        if (length <= Fraction.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        ReadOnlySpan<NoteValue> noteValues =
        [
            NoteValue.Whole,
            NoteValue.Half,
            NoteValue.Quarter,
            NoteValue.Eighth,
            NoteValue.Sixteenth,
            NoteValue.ThirtySecond,
            NoteValue.SixtyFourth,
            NoteValue.HundredTwentyEighth,
        ];

        Fraction remaining = length;
        List<Duration> parts = [];
        while (remaining > Fraction.Zero)
        {
            Duration? best = null;
            Fraction bestLength = Fraction.Zero;
            foreach (NoteValue noteValue in noteValues)
            {
                int maximumDots = 62 - System.Numerics.BitOperations.Log2((uint)noteValue);
                int dotsToTry = Math.Min(maximumDots, 2);
                for (int dots = 0; dots <= dotsToTry; dots++)
                {
                    Duration candidate = new(noteValue, dots);
                    Fraction candidateLength = candidate.Length;
                    if (candidateLength <= remaining && candidateLength > bestLength)
                    {
                        best = candidate;
                        bestLength = candidateLength;
                    }
                }
            }

            if (best is null)
            {
                throw new InvalidOperationException(
                    $"The musical duration {length} cannot be rewritten using the supported note values.");
            }

            parts.Add(best.Value);
            remaining -= bestLength;
        }

        return parts;
    }

    private static int FindMeasureIndex(Fraction position, ImmutableArray<Measure> measures, Fraction[] starts)
    {
        for (int index = 0; index < measures.Length; index++)
        {
            Fraction end = starts[index] + measures[index].TimeSignature.Length;
            if (position >= starts[index] && position < end)
            {
                return index;
            }
        }

        return -1;
    }

    private static (Fraction[] Starts, Fraction TotalLength) GetMeasureStarts(ImmutableArray<Measure> measures)
    {
        Fraction[] starts = new Fraction[measures.Length];
        Fraction total = Fraction.Zero;
        for (int index = 0; index < measures.Length; index++)
        {
            starts[index] = total;
            total += measures[index].TimeSignature.Length;
        }

        return (starts, total);
    }

    private static Score ReplaceVoice(Score score, EditContext context, List<MusicEvent>[] eventsByMeasure)
    {
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content = score.Content.ToBuilder();
        for (int measureIndex = 0; measureIndex < eventsByMeasure.Length; measureIndex++)
        {
            StaffMeasureKey key = new(context.StaffIndex, measureIndex);
            ImmutableArray<MusicEvent> events = ImmutableArray.CreateRange(eventsByMeasure[measureIndex]);
            Voice normalizedVoice = new(context.VoiceNumber, events);

            if (!content.TryGetValue(key, out StaffMeasure? staffMeasure))
            {
                content.Add(key, new StaffMeasure([normalizedVoice]));
                continue;
            }

            int voiceIndex = FindVoiceIndex(staffMeasure, context.VoiceNumber);
            ImmutableArray<Voice> voices = voiceIndex >= 0
                ? staffMeasure.Voices.SetItem(voiceIndex, normalizedVoice)
                : staffMeasure.Voices.Add(normalizedVoice);
            content[key] = staffMeasure with { Voices = voices };
        }

        return score with { Content = content.ToImmutable() };
    }

    private static int FindVoiceIndex(StaffMeasure staffMeasure, int voiceNumber)
    {
        int match = -1;
        for (int index = 0; index < staffMeasure.Voices.Length; index++)
        {
            if (staffMeasure.Voices[index].Number != voiceNumber)
            {
                continue;
            }

            if (match >= 0)
            {
                throw new InvalidOperationException($"Voice {voiceNumber} occurs more than once in a staff measure.");
            }

            match = index;
        }

        return match;
    }

    private static EventId CreateEventId() => new(Guid.NewGuid());

    private sealed record LocatedEvent(Fraction Onset, MusicEvent Event);

    private sealed record ChordFragment(int MeasureIndex, Fraction Onset, Duration Duration);
}
