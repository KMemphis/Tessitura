using MeltySynth;

namespace Tessitura.Playback.Audio;

/// <summary>
/// Schedules synthesizer commands on the sample clock and renders them, block by block, on the audio thread.
/// Everything the audio thread touches is preallocated; control calls from other threads only publish values.
/// </summary>
public sealed class Sequencer : IAudioSource
{
    private const int NoRequest = 0;
    private const int Play = 1;
    private const int Pause = 2;

    private readonly Synthesizer _synthesizer;
    private readonly int _maxBlockFrames;
    private readonly float[] _left;
    private readonly float[] _right;

    private SequenceData _data = SequenceData.Empty;
    private int _nextEvent;
    private long _position;
    private bool _playing;

    private Mixer? _mixer;
    private int _appliedMixerVersion = -1;
    private SequenceData? _pendingData;
    private long _pendingSeek = -1;
    private int _pendingTransport;

    /// <summary>Creates a sequencer over a synthesizer.</summary>
    /// <param name="synthesizer">The synthesizer; its sample rate defines the sample clock.</param>
    /// <param name="maxBlockFrames">The largest block the audio device will ask for.</param>
    public Sequencer(Synthesizer synthesizer, int maxBlockFrames = 8192)
    {
        _synthesizer = synthesizer ?? throw new ArgumentNullException(nameof(synthesizer));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBlockFrames, 1);
        _maxBlockFrames = maxBlockFrames;
        _left = new float[maxBlockFrames];
        _right = new float[maxBlockFrames];
    }

    /// <summary>Gets the position of the sample clock, in samples from the start of the piece.</summary>
    public long PositionSamples => Volatile.Read(ref _position);

    /// <summary>Gets whether the transport is playing.</summary>
    public bool IsPlaying
    {
        get
        {
            // A control request not yet picked up by the audio thread already counts.
            int request = Volatile.Read(ref _pendingTransport);
            return request == Play || (request != Pause && Volatile.Read(ref _playing));
        }
    }

    /// <summary>Gets the length of the loaded sequence in samples.</summary>
    public long LengthSamples => Volatile.Read(ref _data).LengthSamples;

    /// <summary>Replaces the sequence, also while playing: the position is kept and the audio thread picks it up at the next block.</summary>
    /// <param name="data">The new sequence, built for the synthesizer's sample rate.</param>
    public void Load(SequenceData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Volatile.Write(ref _pendingData, data);
    }

    /// <summary>Connects a mixer whose changes take effect at the next audio block.</summary>
    /// <param name="mixer">The mixer.</param>
    public void Attach(Mixer mixer)
    {
        ArgumentNullException.ThrowIfNull(mixer);
        Volatile.Write(ref _mixer, mixer);
    }

    /// <summary>Starts or resumes playing.</summary>
    public void StartPlaying() => Volatile.Write(ref _pendingTransport, Play);

    /// <summary>Pauses; sounding notes are released.</summary>
    public void PausePlaying() => Volatile.Write(ref _pendingTransport, Pause);

    /// <summary>Moves the sample clock; sounding notes are released.</summary>
    /// <param name="sample">The new position in samples, not negative.</param>
    public void Seek(long sample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sample);
        Volatile.Write(ref _pendingSeek, sample);
    }

    /// <inheritdoc />
    public void Render(Span<float> interleaved, int frames)
    {
        ApplyRequests();
        int done = 0;
        while (done < frames)
        {
            int chunk = Math.Min(frames - done, _maxBlockFrames);
            int count = chunk;
            if (_playing)
            {
                DispatchDue();
                if (_nextEvent < _data.Events.Length)
                {
                    long until = _data.Events[_nextEvent].Sample - _position;
                    if (until < count)
                    {
                        count = (int)Math.Max(1, until);
                    }
                }
            }

            _synthesizer.Render(_left.AsSpan(0, count), _right.AsSpan(0, count));
            for (int i = 0; i < count; i++)
            {
                interleaved[2 * (done + i)] = _left[i];
                interleaved[2 * (done + i) + 1] = _right[i];
            }

            if (_playing)
            {
                Volatile.Write(ref _position, _position + count);
                if (_nextEvent >= _data.Events.Length && _position > _data.LengthSamples)
                {
                    _playing = false; // the piece ended; release tails keep ringing on later blocks
                }
            }

            done += count;
        }
    }

    private void DispatchDue()
    {
        System.Collections.Immutable.ImmutableArray<SequencedEvent> events = _data.Events;
        while (_nextEvent < events.Length && events[_nextEvent].Sample <= _position)
        {
            SequencedEvent command = events[_nextEvent++];
            switch (command.Kind)
            {
                case SequencedEventKind.NoteOn:
                    _synthesizer.NoteOn(command.Channel, command.Data1, command.Data2);
                    break;
                case SequencedEventKind.NoteOff:
                    _synthesizer.NoteOff(command.Channel, command.Data1);
                    break;
                default:
                    _synthesizer.ProcessMidiMessage(command.Channel, 0xC0, command.Data1, 0);
                    break;
            }
        }
    }

    private void ApplyRequests()
    {
        Mixer? mixer = Volatile.Read(ref _mixer);
        if (mixer is not null && mixer.Version != _appliedMixerVersion)
        {
            _appliedMixerVersion = mixer.Version;
            mixer.Apply(_synthesizer);
        }

        SequenceData? data = Interlocked.Exchange(ref _pendingData, null);
        long seek = Interlocked.Exchange(ref _pendingSeek, -1);
        int transport = Interlocked.Exchange(ref _pendingTransport, NoRequest);
        bool reposition = false;
        if (data is not null)
        {
            _data = data;
            _synthesizer.NoteOffAll(immediate: false);
            reposition = true;
        }

        if (seek >= 0)
        {
            Volatile.Write(ref _position, seek);
            _synthesizer.NoteOffAll(immediate: false);
            reposition = true;
        }

        if (reposition)
        {
            _nextEvent = _data.FirstAtOrAfter(_position);
        }

        if (transport == Play)
        {
            if (_position >= _data.LengthSamples && _data.Events.Length > 0 && _nextEvent >= _data.Events.Length)
            {
                Volatile.Write(ref _position, 0);
                _nextEvent = 0;
            }

            Volatile.Write(ref _playing, true);
        }
        else if (transport == Pause)
        {
            Volatile.Write(ref _playing, false);
            _synthesizer.NoteOffAll(immediate: false);
        }
    }
}
