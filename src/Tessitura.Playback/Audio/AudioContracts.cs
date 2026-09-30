namespace Tessitura.Playback.Audio;

/// <summary>Produces audio on the device's audio thread. Implementations must not allocate, lock or throw.</summary>
public interface IAudioSource
{
    /// <summary>Fills a block of interleaved stereo float samples.</summary>
    /// <param name="interleaved">The output, left then right for each frame, with room for <paramref name="frames"/> frames.</param>
    /// <param name="frames">The number of frames to produce.</param>
    void Render(Span<float> interleaved, int frames);
}

/// <summary>Plays an <see cref="IAudioSource"/> through an audio device.</summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>Gets the sample rate of the open device in Hz.</summary>
    int SampleRate { get; }

    /// <summary>Gets the size of one device period in frames.</summary>
    int BufferFrames { get; }

    /// <summary>Gets the total buffered audio in milliseconds (period size times period count).</summary>
    double BufferMilliseconds { get; }

    /// <summary>Gets the number of callbacks received since <see cref="Start"/>.</summary>
    long CallbackCount { get; }

    /// <summary>Gets the callback intervals that exceeded twice the period, which indicate a probable dropout.</summary>
    long LateCallbackCount { get; }

    /// <summary>Starts pulling audio from the source.</summary>
    /// <param name="source">The audio source, called from the device thread.</param>
    void Start(IAudioSource source);

    /// <summary>Stops the device.</summary>
    void Stop();
}
