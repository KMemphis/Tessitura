using System.Collections.Immutable;
using MeltySynth;
using Tessitura.Core;
using Tessitura.Playback.Audio;
using Tessitura.Playback.Performance;
using Xunit;
using Instrument = Tessitura.Core.Instrument;

namespace Tessitura.Playback.Tests;

public sealed class MixerTests
{
    private const int Rate = 48000;

    [Fact]
    public void EffectiveVolumeFollowsMuteAndSolo()
    {
        Mixer mixer = new(3);
        mixer.SetVolume(0, 0.5f);

        Assert.Equal(0.5f, mixer.EffectiveVolume(0));
        mixer.SetMute(0, true);
        Assert.Equal(0f, mixer.EffectiveVolume(0));
        mixer.SetMute(0, false);
        mixer.SetSolo(1, true);
        Assert.Equal(0f, mixer.EffectiveVolume(0));
        Assert.Equal(1f, mixer.EffectiveVolume(1));
        mixer.SetSolo(2, true);
        Assert.Equal(1f, mixer.EffectiveVolume(2));
        mixer.SetSolo(1, false);
        mixer.SetSolo(2, false);
        Assert.Equal(0.5f, mixer.EffectiveVolume(0));
        mixer.SetVolume(0, 7);
        mixer.SetPan(0, -9);
        Assert.Equal((1f, -1f), (mixer.GetVolume(0), mixer.GetPan(0)));
    }

    [Fact]
    public void ControlsActInRealTimeOnTheRenderedAudio()
    {
        (Sequencer sequencer, Mixer mixer) = Create(instruments: 2);
        float[] block = new float[2 * 4800];
        sequencer.StartPlaying();
        sequencer.Render(block, 4800);
        double full = Energy(block);
        Assert.True(full > 0);

        mixer.SetMute(0, true);
        mixer.SetMute(1, true);
        sequencer.Render(block, 4800); // next block picks the change up
        sequencer.Render(block, 4800);
        Assert.True(Energy(block) < full * 0.01, "muting did not silence the output");

        mixer.SetMute(0, false);
        mixer.SetMute(1, false);
        mixer.SetVolume(0, 1);
        for (int i = 0; i < 6; i++)
        {
            sequencer.Render(block, 4800);
        }

        Assert.True(Energy(block) > full * 0.05, "unmuting did not bring the sound back");
    }

    [Fact]
    public void SoloSilencesTheOtherInstruments()
    {
        double both = Energy(RenderSeconds(instrumentsSetup: _ => { }));
        double soloed = Energy(RenderSeconds(m => m.SetSolo(0, true)));
        double none = Energy(RenderSeconds(m => { m.SetSolo(1, true); m.SetMute(1, true); }));

        Assert.True(soloed < both);
        Assert.True(soloed > 0);
        Assert.True(none < soloed * 0.01);
    }

    [Fact]
    public void PanMovesTheSoundBetweenTheStereoChannels()
    {
        float[] left = RenderSeconds(m => { m.SetPan(0, -1); m.SetPan(1, -1); });
        float[] right = RenderSeconds(m => { m.SetPan(0, 1); m.SetPan(1, 1); });

        Assert.True(Channel(left, 0) > Channel(left, 1) * 10);
        Assert.True(Channel(right, 1) > Channel(right, 0) * 10);
    }

    [Fact]
    public void MixerChangesAllocateNothingOnTheAudioThread()
    {
        (Sequencer sequencer, Mixer mixer) = Create(instruments: 3);
        float[] block = new float[2 * 240];
        sequencer.StartPlaying();
        for (int i = 0; i < 300; i++)
        {
            sequencer.Render(block, 240);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            if (i % 50 == 0)
            {
                mixer.SetVolume(i / 50 % 3, (i % 100) / 100f);
            }

            sequencer.Render(block, 240);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData("Piano", 0)]
    [InlineData("Violín I", 40)]
    [InlineData("Violonchelo", 42)]
    [InlineData("Viola", 41)]
    [InlineData("Flauta", 73)]
    [InlineData("Clarinete en Si♭", 71)]
    [InlineData("Trompeta", 56)]
    [InlineData("Soprano", 52)]
    [InlineData("Instrumento raro", 0)]
    public void InstrumentNamesMapToGeneralMidiPrograms(string name, int program) =>
        Assert.Equal(program, GeneralMidiPrograms.FromName(name));

    private static float[] RenderSeconds(Action<Mixer> instrumentsSetup)
    {
        (Sequencer sequencer, Mixer mixer) = Create(instruments: 2);
        instrumentsSetup(mixer);
        float[] audio = new float[2 * Rate / 2];
        sequencer.StartPlaying();
        sequencer.Render(audio.AsSpan(0, 2 * 240), 240);
        sequencer.Render(audio.AsSpan(2 * 240), Rate / 2 - 240);
        return audio;
    }

    private static (Sequencer, Mixer) Create(int instruments)
    {
        ImmutableArray<Instrument>.Builder list = ImmutableArray.CreateBuilder<Instrument>();
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content = ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        for (int i = 0; i < instruments; i++)
        {
            list.Add(new Instrument($"I{i}", [new Staff("S")]));
            content[new StaffMeasureKey(i, 0)] = new StaffMeasure([new Voice(1,
                [.. Enumerable.Range(0, 4).Select(b => (MusicEvent)new Chord(new EventId(Guid.NewGuid()), new Fraction(b, 4),
                    new Duration(NoteValue.Quarter, 0), [new Note(new Pitch(Step.C, 0, 4 + i))], StemDirection.Auto))])]);
        }

        Score score = new(new ScoreMetadata("T", ""), list.ToImmutable(), [new Measure(1, new TimeSignature(4, 4))], content.ToImmutable());
        Synthesizer synthesizer = new(new SoundFont(Path.Combine(AppContext.BaseDirectory, "sf_spec_test.sf2")),
            new SynthesizerSettings(Rate) { EnableReverbAndChorus = false });
        Sequencer sequencer = new(synthesizer);
        Mixer mixer = new(instruments);
        sequencer.Attach(mixer);
        sequencer.Load(SequenceData.Build(Interpreter.Interpret(score), Rate));
        return (sequencer, mixer);
    }

    private static double Energy(float[] samples) => samples.Sum(s => (double)s * s);

    private static double Channel(float[] interleaved, int channel)
    {
        double sum = 0;
        for (int i = channel; i < interleaved.Length; i += 2)
        {
            sum += (double)interleaved[i] * interleaved[i];
        }

        return sum;
    }
}
