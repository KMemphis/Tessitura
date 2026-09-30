using System.Diagnostics;
using System.Text.Json;
using MeltySynth;
using MiniAudioEx.Core.StandardAPI;
using MiniAudioEx.Native;
using Silk.NET.OpenAL;
using MiniAudioContext = MiniAudioEx.Core.StandardAPI.AudioContext;

namespace Tessitura.Benchmarks;

internal static class Program
{
    private const int SampleRate = 48000;
    private const int ChannelCount = 2;
    private const int FrameCount = SampleRate / 2;

    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "midi-probe")
        {
            return MidiProbe.Run(args[1]);
        }

        if (args.Length != 2 || args[0] != "audio-probe" ||
            (args[1] != "openal" && args[1] != "miniaudio"))
        {
            Console.Error.WriteLine("Usage: dotnet run --project tests/Tessitura.Benchmarks -- audio-probe openal|miniaudio OR midi-probe list|loopback");
            return 2;
        }

        string fontPath = Path.Combine(AppContext.BaseDirectory, "sf_spec_test.sf2");
        Synthesizer synthesizer = new(fontPath, SampleRate);
        synthesizer.NoteOn(0, 60, 100);
        float[] samples = new float[FrameCount * ChannelCount];
        synthesizer.RenderInterleaved(samples);
        bool hasSignal = false;
        for (int i = 0; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i]) > 0.0001f)
            {
                hasSignal = true;
                break;
            }
        }

        if (!hasSignal)
        {
            Console.Error.WriteLine("MeltySynth produced silence.");
            return 1;
        }

        try
        {
            ProbeResult result = args[1] == "openal"
                ? RunOpenAl(samples)
                : RunMiniaudio(samples);
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static unsafe ProbeResult RunOpenAl(float[] samples)
    {
        using ALContext alc = ALContext.GetApi(true);
        using AL al = AL.GetApi(true);
        Device* device = alc.OpenDevice("");
        if (device is null)
        {
            throw new InvalidOperationException("OpenAL Soft could not open an output device.");
        }

        Context* context = alc.CreateContext(device, null);
        if (context is null || !alc.MakeContextCurrent(context))
        {
            alc.CloseDevice(device);
            throw new InvalidOperationException("OpenAL Soft could not create an output context.");
        }

        uint source = 0;
        uint buffer = 0;
        try
        {
            short[] pcm = new short[samples.Length];
            for (int i = 0; i < pcm.Length; i++)
            {
                pcm[i] = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            }

            source = al.GenSource();
            buffer = al.GenBuffer();
            fixed (short* data = pcm)
            {
                al.BufferData(buffer, BufferFormat.Stereo16, data, pcm.Length * sizeof(short), SampleRate);
            }

            al.SetSourceProperty(source, SourceInteger.Buffer, buffer);
            long start = Stopwatch.GetTimestamp();
            al.SourcePlay(source);
            double dispatchMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Thread.Sleep(600);
            return new ProbeResult("openal-soft", Environment.OSVersion.ToString(), SampleRate,
                dispatchMs, null, al.GetError().ToString());
        }
        finally
        {
            if (source != 0)
            {
                al.SourceStop(source);
                al.DeleteSource(source);
            }

            if (buffer != 0)
            {
                al.DeleteBuffer(buffer);
            }

            alc.MakeContextCurrent(null);
            alc.DestroyContext(context);
            alc.CloseDevice(device);
        }
    }

    private static ProbeResult RunMiniaudio(float[] samples)
    {
        MiniAudioContext.Initialize(SampleRate, ChannelCount);
        try
        {
            AudioSource source = new();
            int cursor = 0;
            long firstCallback = 0;
            source.Read += Read;

            void Read(NativeArray<float> output, ulong frameCount, int channels)
            {
                Interlocked.CompareExchange(ref firstCallback, Stopwatch.GetTimestamp(), 0);
                for (int i = 0; i < output.Length; i++)
                {
                    output[i] = cursor < samples.Length ? samples[cursor++] : 0;
                }
            }

            long start = Stopwatch.GetTimestamp();
            source.Play();
            double dispatchMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Thread.Sleep(600);
            MiniAudioContext.Update();
            double? callbackMs = firstCallback == 0
                ? null
                : Stopwatch.GetElapsedTime(start, firstCallback).TotalMilliseconds;
            return new ProbeResult("miniaudio", Environment.OSVersion.ToString(), SampleRate,
                dispatchMs, callbackMs, firstCallback == 0 ? "No audio callback" : "OK");
        }
        finally
        {
            MiniAudioContext.Deinitialize();
        }
    }

    private sealed record ProbeResult(
        string Backend,
        string Platform,
        int SampleRate,
        double DispatchMilliseconds,
        double? FirstCallbackMilliseconds,
        string Status);
}
