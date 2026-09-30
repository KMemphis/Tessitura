namespace Tessitura.Playback.Audio;

/// <summary>Per-instrument volume, pan, mute and solo that act in real time on the sequencer.</summary>
public sealed class Mixer
{
    private readonly float[] _volume;
    private readonly float[] _pan;
    private readonly bool[] _mute;
    private readonly bool[] _solo;
    private int _version;

    /// <summary>Creates a mixer.</summary>
    /// <param name="instrumentCount">The number of instruments in the score.</param>
    public Mixer(int instrumentCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(instrumentCount);
        InstrumentCount = instrumentCount;
        _volume = new float[instrumentCount];
        _pan = new float[instrumentCount];
        _mute = new bool[instrumentCount];
        _solo = new bool[instrumentCount];
        Array.Fill(_volume, 1f);
    }

    /// <summary>Gets the number of instruments.</summary>
    public int InstrumentCount { get; }

    /// <summary>Gets a counter that changes whenever any control changes.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>Gets the volume of an instrument, from 0 to 1.</summary>
    /// <param name="instrument">The instrument index.</param>
    /// <returns>The volume.</returns>
    public float GetVolume(int instrument) => _volume[instrument];

    /// <summary>Gets the pan of an instrument, from -1 (left) to 1 (right).</summary>
    /// <param name="instrument">The instrument index.</param>
    /// <returns>The pan.</returns>
    public float GetPan(int instrument) => _pan[instrument];

    /// <summary>Gets whether an instrument is muted.</summary>
    /// <param name="instrument">The instrument index.</param>
    /// <returns>Whether it is muted.</returns>
    public bool IsMuted(int instrument) => _mute[instrument];

    /// <summary>Gets whether an instrument is soloed.</summary>
    /// <param name="instrument">The instrument index.</param>
    /// <returns>Whether it is soloed.</returns>
    public bool IsSoloed(int instrument) => _solo[instrument];

    /// <summary>Sets the volume.</summary>
    /// <param name="instrument">The instrument index.</param>
    /// <param name="volume">From 0 to 1.</param>
    public void SetVolume(int instrument, float volume)
    {
        _volume[instrument] = Math.Clamp(volume, 0f, 1f);
        Changed();
    }

    /// <summary>Sets the pan.</summary>
    /// <param name="instrument">The instrument index.</param>
    /// <param name="pan">From -1 to 1.</param>
    public void SetPan(int instrument, float pan)
    {
        _pan[instrument] = Math.Clamp(pan, -1f, 1f);
        Changed();
    }

    /// <summary>Mutes or unmutes an instrument.</summary>
    /// <param name="instrument">The instrument index.</param>
    /// <param name="mute">Whether to mute.</param>
    public void SetMute(int instrument, bool mute)
    {
        _mute[instrument] = mute;
        Changed();
    }

    /// <summary>Solos or unsolos an instrument; while any is soloed, the others are silent.</summary>
    /// <param name="instrument">The instrument index.</param>
    /// <param name="solo">Whether to solo.</param>
    public void SetSolo(int instrument, bool solo)
    {
        _solo[instrument] = solo;
        Changed();
    }

    /// <summary>Gets the volume that actually sounds after mute and solo, from 0 to 1.</summary>
    /// <param name="instrument">The instrument index.</param>
    /// <returns>The effective volume.</returns>
    public float EffectiveVolume(int instrument)
    {
        bool anySolo = false;
        for (int i = 0; i < _solo.Length; i++)
        {
            anySolo |= _solo[i];
        }

        return _mute[instrument] || (anySolo && !_solo[instrument]) ? 0f : _volume[instrument];
    }

    /// <summary>Sends the current settings to a synthesizer as MIDI volume and pan controllers, without allocating.</summary>
    /// <param name="synthesizer">The synthesizer.</param>
    public void Apply(MeltySynth.Synthesizer synthesizer)
    {
        // Instruments share a channel when there are more than fifteen; the last one wins.
        for (int instrument = 0; instrument < InstrumentCount; instrument++)
        {
            int channel = SequenceData.ChannelOf(instrument);
            synthesizer.ProcessMidiMessage(channel, 0xB0, 7, (int)Math.Round(EffectiveVolume(instrument) * 127));
            synthesizer.ProcessMidiMessage(channel, 0xB0, 10, (int)Math.Round((_pan[instrument] + 1f) * 63.5f));
        }
    }

    private void Changed() => Interlocked.Increment(ref _version);
}
