using Tessitura.Playback.Audio;

namespace Tessitura.App;

/// <summary>Produces allocation-free quarter-note clicks for real-time MIDI recording.</summary>
public sealed class MetronomeClickSource : IAudioSource
{
    private readonly int _sampleRate;
    private readonly long _beatFrames;
    private readonly int _clickFrames;
    private long _framePosition;

    /// <summary>Creates a click source at a fixed quarter-note tempo.</summary>
    /// <param name="sampleRate">The audio sample rate in frames per second.</param>
    /// <param name="beatsPerMinute">The quarter-note tempo.</param>
    public MetronomeClickSource(int sampleRate, int beatsPerMinute)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfLessThan(beatsPerMinute, 20);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(beatsPerMinute, 300);
        _sampleRate = sampleRate;
        _beatFrames = Math.Max(1, (long)Math.Round((double)sampleRate * 60 / beatsPerMinute));
        _clickFrames = Math.Max(1, sampleRate / 50);
    }

    /// <inheritdoc />
    public void Render(Span<float> interleaved, int frames)
    {
        int frameCount = Math.Min(frames, interleaved.Length / 2);
        long firstFrame = _framePosition;
        for (int frame = 0; frame < frameCount; frame++)
        {
            long beatPosition = (firstFrame + frame) % _beatFrames;
            float sample = 0;
            if (beatPosition < _clickFrames)
            {
                double time = (double)beatPosition / _sampleRate;
                double envelope = 1.0 - (double)beatPosition / _clickFrames;
                sample = (float)(Math.Sin(2 * Math.PI * 1400 * time) * envelope * 0.35);
            }

            int sampleIndex = frame * 2;
            interleaved[sampleIndex] = sample;
            interleaved[sampleIndex + 1] = sample;
        }

        if (frameCount < frames)
        {
            interleaved[(frameCount * 2)..].Clear();
        }

        _framePosition += frames;
    }
}
