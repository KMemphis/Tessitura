using Tessitura.Playback.Midi;

namespace Tessitura.App;

/// <summary>
/// Turns notes played on a MIDI keyboard into notation at the input cursor: keys pressed within the chord
/// window (or still held) make one chord, written when the keys are released or the window has passed.
/// </summary>
public sealed class MidiStepInput
{
    private readonly ScoreInputController _input;
    private readonly object _gate = new();
    private readonly HashSet<int> _held = [];
    private readonly List<int> _pending = [];
    private long _lastPressMs;

    /// <summary>Creates the step input.</summary>
    /// <param name="input">The editor that receives the notes.</param>
    /// <param name="chordWindowMs">Keys pressed within this many milliseconds belong to the same chord.</param>
    public MidiStepInput(ScoreInputController input, int chordWindowMs = 60)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        ChordWindowMs = chordWindowMs;
    }

    /// <summary>Gets the chord window in milliseconds.</summary>
    public int ChordWindowMs { get; }

    /// <summary>Feeds a message received from the keyboard.</summary>
    /// <param name="message">The MIDI message.</param>
    /// <param name="timestampMs">A monotonic time in milliseconds.</param>
    public void OnMessage(MidiMessage message, long timestampMs)
    {
        lock (_gate)
        {
            if (message.Kind == MidiMessageKind.NoteOn)
            {
                if (_pending.Count > 0 && timestampMs - _lastPressMs > ChordWindowMs && _held.Count == 0)
                {
                    FlushLocked();
                }

                _held.Add(message.Data1);
                _pending.Add(message.Data1);
                _lastPressMs = timestampMs;
            }
            else if (message.Kind == MidiMessageKind.NoteOff)
            {
                _held.Remove(message.Data1);
                if (_held.Count == 0 && timestampMs - _lastPressMs >= 0)
                {
                    FlushLocked();
                }
            }
        }
    }

    /// <summary>Writes a chord whose window has expired; call from a timer.</summary>
    /// <param name="timestampMs">The current monotonic time in milliseconds.</param>
    public void Tick(long timestampMs)
    {
        lock (_gate)
        {
            if (_pending.Count > 0 && _held.Count == 0 && timestampMs - _lastPressMs > ChordWindowMs)
            {
                FlushLocked();
            }
        }
    }

    private void FlushLocked()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        int[] notes = [.. _pending];
        _pending.Clear();
        _input.EnterChord(notes);
    }
}
