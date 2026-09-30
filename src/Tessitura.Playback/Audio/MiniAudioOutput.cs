using System.Diagnostics;
using MiniAudioEx.Core.AdvancedAPI;
using MiniAudioEx.Native;

namespace Tessitura.Playback.Audio;

/// <summary>Plays an <see cref="IAudioSource"/> through miniaudio's low-level device API (approved in F0.11).</summary>
public sealed unsafe class MiniAudioOutput : IAudioOutput
{
    private readonly MaDevice _device = new();
    private readonly ma_device_data_proc _callback;
    private readonly long _lateThresholdTicks;
    private IAudioSource? _source;
    private long _callbackCount;
    private long _lateCallbackCount;
    private long _lastCallbackTicks;
    private bool _started;
    private bool _disposed;

    /// <summary>Opens the default playback device.</summary>
    /// <param name="sampleRate">The requested sample rate in Hz.</param>
    /// <param name="periodFrames">The requested frames per period; 240 at 48 kHz is 5 ms.</param>
    /// <param name="periods">The number of periods, at least two; buffered time is period size times this.</param>
    /// <exception cref="InvalidOperationException">No playback device could be opened.</exception>
    public MiniAudioOutput(int sampleRate = 48000, int periodFrames = 240, int periods = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(periods, 2);
        _callback = OnData;
        ma_device_config config = _device.GetConfig(ma_device_type.playback);
        config.sampleRate = (uint)sampleRate;
        config.periodSizeInFrames = (uint)periodFrames;
        config.periods = (uint)periods;
        config.playback.format = ma_format.f32;
        config.playback.channels = 2;
        config.SetDataCallback(_callback);
        if (_device.Initialize(config) != ma_result.success)
        {
            _device.Dispose();
            throw new InvalidOperationException("miniaudio could not open the default playback device.");
        }

        SampleRate = sampleRate;
        BufferFrames = periodFrames;
        BufferMilliseconds = 1000.0 * periodFrames * periods / sampleRate;
        _lateThresholdTicks = (long)(2.0 * periodFrames / sampleRate * Stopwatch.Frequency);
    }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public int BufferFrames { get; }

    /// <inheritdoc />
    public double BufferMilliseconds { get; }

    /// <inheritdoc />
    public long CallbackCount => Interlocked.Read(ref _callbackCount);

    /// <inheritdoc />
    public long LateCallbackCount => Interlocked.Read(ref _lateCallbackCount);

    /// <inheritdoc />
    public void Start(IAudioSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(_disposed, this);
        Volatile.Write(ref _source, source);
        _lastCallbackTicks = 0;
        if (_device.Start() != ma_result.success)
        {
            throw new InvalidOperationException("miniaudio could not start the playback device.");
        }

        _started = true;
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_started)
        {
            _ = _device.Stop();
            _started = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _device.Dispose();
    }

    private void OnData(ma_device_ptr device, IntPtr output, IntPtr input, uint frameCount)
    {
        long now = Stopwatch.GetTimestamp();
        long previous = _lastCallbackTicks;
        _lastCallbackTicks = now;
        Interlocked.Increment(ref _callbackCount);
        if (previous != 0 && now - previous > _lateThresholdTicks)
        {
            Interlocked.Increment(ref _lateCallbackCount);
        }

        Span<float> block = new((void*)output, checked((int)frameCount * 2));
        IAudioSource? source = Volatile.Read(ref _source);
        if (source is null)
        {
            block.Clear();
            return;
        }

        source.Render(block, (int)frameCount);
    }
}
