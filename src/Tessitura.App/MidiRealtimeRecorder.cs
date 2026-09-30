using Tessitura.Core;
using Tessitura.Playback.Midi;

namespace Tessitura.App;

/// <summary>Captures MIDI note events and writes them to the score on an eighth-note grid.</summary>
public sealed class MidiRealtimeRecorder
{
    private static readonly (int Ticks, Duration Duration)[] NotatedDurations =
    [
        (1, new Duration(NoteValue.Eighth, 0)),
        (2, new Duration(NoteValue.Quarter, 0)),
        (3, new Duration(NoteValue.Quarter, 1)),
        (4, new Duration(NoteValue.Half, 0)),
        (6, new Duration(NoteValue.Half, 1)),
        (8, new Duration(NoteValue.Whole, 0)),
    ];

    private readonly ScoreInputController _input;
    private readonly List<RecordedNote> _completed = [];
    private readonly Dictionary<(int Channel, int Pitch), Queue<long>> _held = [];
    private long _startedAtMs;
    private int _beatsPerMinute;
    private Fraction _anchor;

    /// <summary>Creates a real-time recorder for an editor score.</summary>
    /// <param name="input">The score input controller that applies recorded notes.</param>
    public MidiRealtimeRecorder(ScoreInputController input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
    }

    /// <summary>Gets whether MIDI events are currently being captured.</summary>
    public bool IsRecording { get; private set; }

    /// <summary>Begins a take using the cursor as its exact score-position anchor.</summary>
    /// <param name="timestampMs">The monotonic timestamp in milliseconds.</param>
    /// <param name="beatsPerMinute">The quarter-note tempo used to convert the take to eighth-note ticks.</param>
    public void Start(long timestampMs, int beatsPerMinute)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("A MIDI recording is already in progress.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(timestampMs);
        ArgumentOutOfRangeException.ThrowIfLessThan(beatsPerMinute, 20);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(beatsPerMinute, 300);
        _completed.Clear();
        _held.Clear();
        _startedAtMs = timestampMs;
        _beatsPerMinute = beatsPerMinute;
        _anchor = FindNearestRestAnchor(_input.CurrentScore, _input.Cursor);
        IsRecording = true;
    }

    /// <summary>Captures a note-on or note-off while a take is active.</summary>
    /// <param name="message">The MIDI message received from the port.</param>
    /// <param name="timestampMs">The monotonic message timestamp in milliseconds.</param>
    public void OnMessage(MidiMessage message, long timestampMs)
    {
        if (!IsRecording || timestampMs < _startedAtMs || message.Data1 is < 0 or > 127)
        {
            return;
        }

        (int Channel, int Pitch) key = (message.Channel, message.Data1);
        if (message.Kind == MidiMessageKind.NoteOn)
        {
            if (!_held.TryGetValue(key, out Queue<long>? starts))
            {
                starts = new Queue<long>();
                _held.Add(key, starts);
            }

            starts.Enqueue(timestampMs);
        }
        else if (message.Kind == MidiMessageKind.NoteOff && _held.TryGetValue(key, out Queue<long>? activeStarts) &&
            activeStarts.Count > 0)
        {
            AddCompleted(key.Pitch, activeStarts.Dequeue(), timestampMs);
            if (activeStarts.Count == 0)
            {
                _held.Remove(key);
            }
        }
    }

    /// <summary>Ends the take, quantizes its notes and inserts the resulting music through score commands.</summary>
    /// <param name="timestampMs">The monotonic stop timestamp in milliseconds.</param>
    /// <returns>The number of written chord onsets.</returns>
    public int Stop(long timestampMs)
    {
        if (!IsRecording)
        {
            return 0;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(timestampMs);
        foreach (KeyValuePair<(int Channel, int Pitch), Queue<long>> entry in _held)
        {
            while (entry.Value.TryDequeue(out long startedAt))
            {
                AddCompleted(entry.Key.Pitch, startedAt, Math.Max(startedAt, timestampMs));
            }
        }

        _held.Clear();
        IsRecording = false;
        List<QuantizedNote> notes = Quantize();
        if (notes.Count == 0)
        {
            return 0;
        }

        bool restoreSelectionMode = _input.Mode == ScoreInputMode.Selection;
        _input.EnterNoteEntry();
        _input.SetCursorPosition(_anchor);
        Fraction cursor = _anchor;
        int tick = 0;
        int chordCount = 0;
        int index = 0;
        while (index < notes.Count)
        {
            int startTick = notes[index].StartTick;
            while (tick < startTick)
            {
                _ = _input.EnterRest(new Duration(NoteValue.Eighth, 0));
                cursor += new Fraction(1, 8);
                tick++;
            }

            int end = index + 1;
            while (end < notes.Count && notes[end].StartTick == startTick)
            {
                end++;
            }

            int groupEndTick = startTick;
            List<int> pitches = new(end - index);
            for (int noteIndex = index; noteIndex < end; noteIndex++)
            {
                pitches.Add(notes[noteIndex].Pitch);
                groupEndTick = Math.Max(groupEndTick, notes[noteIndex].EndTick);
            }

            int nextStartTick = end < notes.Count ? notes[end].StartTick : int.MaxValue;
            if (nextStartTick != int.MaxValue)
            {
                groupEndTick = Math.Min(groupEndTick, nextStartTick);
            }

            int durationTicks = Math.Max(1, groupEndTick - startTick);
            Duration duration = SelectDuration(durationTicks, nextStartTick == int.MaxValue
                ? 8
                : Math.Max(1, nextStartTick - startTick));
            _input.SetCursorPosition(_anchor + new Fraction(startTick, 8));
            if (_input.EnterChord(pitches, duration))
            {
                chordCount++;
            }

            int selectedDurationTicks = checked((int)(duration.Length.Num * 8 / duration.Length.Den));
            tick = startTick + selectedDurationTicks;
            cursor = _anchor + new Fraction(tick, 8);
            index = end;
        }

        _input.SetCursorPosition(cursor);
        if (restoreSelectionMode)
        {
            _input.ExitNoteEntry();
        }

        return chordCount;
    }

    private List<QuantizedNote> Quantize()
    {
        List<QuantizedNote> notes = new(_completed.Count);
        foreach (RecordedNote note in _completed)
        {
            int start = ToTick(note.StartedAtMs);
            int end = Math.Max(start + 1, ToTick(note.EndedAtMs));
            notes.Add(new QuantizedNote(note.Pitch, start, end));
        }

        notes.Sort(static (left, right) => left.StartTick != right.StartTick
            ? left.StartTick.CompareTo(right.StartTick)
            : left.Pitch.CompareTo(right.Pitch));
        for (int index = 0; index < notes.Count; index++)
        {
            int nextStart = index + 1;
            while (nextStart < notes.Count && notes[nextStart].StartTick == notes[index].StartTick)
            {
                nextStart++;
            }

            if (nextStart < notes.Count && notes[index].EndTick > notes[nextStart].StartTick)
            {
                notes[index] = notes[index] with { EndTick = Math.Max(notes[index].StartTick + 1, notes[nextStart].StartTick) };
            }
        }

        return notes;
    }

    private void AddCompleted(int pitch, long startedAtMs, long endedAtMs) =>
        _completed.Add(new RecordedNote(pitch, startedAtMs, endedAtMs));

    private int ToTick(long timestampMs)
    {
        long elapsedMs = Math.Max(0, timestampMs - _startedAtMs);
        long wholeIntervals = elapsedMs / 30000;
        long remainder = elapsedMs % 30000;
        long ticks = checked(wholeIntervals * _beatsPerMinute +
            (remainder * _beatsPerMinute + 15000) / 30000);
        return ticks >= int.MaxValue ? int.MaxValue : (int)ticks;
    }

    private static Fraction FindNearestRestAnchor(Score score, ScoreInputCursor cursor)
    {
        Fraction measureStart = Fraction.Zero;
        Fraction? nearestPosition = null;
        Fraction? nearestDistance = null;
        for (int measureIndex = 0; measureIndex < score.Measures.Length; measureIndex++)
        {
            Measure measure = score.Measures[measureIndex];
            if (score.Content.TryGetValue(new StaffMeasureKey(cursor.StaffIndex,
                measureIndex), out StaffMeasure? staffMeasure))
            {
                foreach (Voice voice in staffMeasure.Voices)
                {
                    if (voice.Number != cursor.VoiceNumber)
                    {
                        continue;
                    }

                    foreach (MusicEvent musicEvent in voice.Events)
                    {
                        if (musicEvent is not Rest)
                        {
                            continue;
                        }

                        Fraction position = measureStart + musicEvent.Onset;
                        Fraction distance = position >= cursor.Position
                            ? position - cursor.Position
                            : cursor.Position - position;
                        if (nearestDistance is null || distance < nearestDistance.Value)
                        {
                            nearestPosition = position;
                            nearestDistance = distance;
                        }
                    }

                    break;
                }
            }

            measureStart += measure.TimeSignature.Length;
        }

        return nearestPosition ?? throw new InvalidOperationException(
            "Real-time MIDI recording needs at least one rest in the destination voice.");
    }

    private static Duration SelectDuration(int targetTicks, int maximumTicks)
    {
        int boundedTicks = Math.Clamp(targetTicks, 1, Math.Min(8, maximumTicks));
        Duration best = NotatedDurations[0].Duration;
        int bestDistance = int.MaxValue;
        foreach ((int ticks, Duration duration) in NotatedDurations)
        {
            if (ticks > maximumTicks)
            {
                continue;
            }

            int distance = Math.Abs(ticks - boundedTicks);
            if (distance < bestDistance)
            {
                best = duration;
                bestDistance = distance;
            }
        }

        return best;
    }

    private sealed record RecordedNote(int Pitch, long StartedAtMs, long EndedAtMs);

    private sealed record QuantizedNote(int Pitch, int StartTick, int EndTick);
}
