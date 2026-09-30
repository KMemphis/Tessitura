using System.Diagnostics;
using System.Text.Json;
using MeltySynth;
using Tessitura.Playback.Audio;
using Tessitura.Playback.Performance;

namespace Tessitura.Benchmarks;

/// <summary>Plays the reference score through the real audio device and reports buffer, callback and dropout figures.</summary>
internal static class AudioPlay
{
    public static int Run(string[] args)
    {
        double seconds = args.Length > 0 && double.TryParse(args[0], out double parsed) ? parsed : 10;
        string soundFontPath = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "sf_spec_test.sf2");
        const int sampleRate = 48000;
        try
        {
            SoundFont soundFont = new(soundFontPath);
            Synthesizer synthesizer = new(soundFont, new SynthesizerSettings(sampleRate));
            Sequencer sequencer = new(synthesizer);
            sequencer.Load(SequenceData.Build(Interpreter.Interpret(ReferenceScoreFactory.Create()), sampleRate));
            using MiniAudioOutput output = new(sampleRate, periodFrames: 240, periods: 2);
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            int collectionsBefore = GC.CollectionCount(0);
            sequencer.StartPlaying();
            Stopwatch clock = Stopwatch.StartNew();
            output.Start(sequencer);
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
            output.Stop();
            clock.Stop();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                platform = Environment.OSVersion.ToString(),
                soundFont = Path.GetFileName(soundFontPath),
                output.SampleRate,
                periodFrames = output.BufferFrames,
                bufferMilliseconds = output.BufferMilliseconds,
                playedSeconds = clock.Elapsed.TotalSeconds,
                sequencerSeconds = (double)sequencer.PositionSamples / sampleRate,
                callbacks = output.CallbackCount,
                lateCallbacks = output.LateCallbackCount,
                allocatedBytesDuringPlayback = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
                gen0Collections = GC.CollectionCount(0) - collectionsBefore,
            }));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
