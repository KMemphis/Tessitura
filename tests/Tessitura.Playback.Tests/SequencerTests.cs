using System.Collections.Immutable;
using MeltySynth;
using Instrument = Tessitura.Core.Instrument;
using Tessitura.Core;
using Tessitura.Playback.Audio;
using Tessitura.Playback.Performance;
using Xunit;

namespace Tessitura.Playback.Tests;

public sealed class SequencerTests
{
    private const int Rate = 48000;

    [Fact]
    public void BuildPlacesCommandsOnTheSampleClockInAudioOrder()
    {
        Score score = Melody(quarters: 4, instruments: 2);

        SequenceData data = SequenceData.Build(Interpreter.Interpret(score), Rate, [24, 40]);

        Assert.Equal(Rate, data.SampleRate);
        SequencedEvent first = data.Events[0];
        Assert.Equal((0L, SequencedEventKind.ProgramChange), (first.Sample, first.Kind));
        Assert.Contains(data.Events, e => e.Kind == SequencedEventKind.ProgramChange && e.Channel == 0 && e.Data1 == 24);
        Assert.Contains(data.Events, e => e.Kind == SequencedEventKind.ProgramChange && e.Channel == 1 && e.Data1 == 40);
        // Quarter notes at 120 bpm start every 0.5 s = 24000 samples; the 90 % gate releases them at 0.45 s = 21600.
        List<long> ons = [.. data.Events.Where(e => e.Kind == SequencedEventKind.NoteOn && e.Channel == 0).Select(e => e.Sample)];
        Assert.Equal([0L, 24000, 48000, 72000], ons);
        Assert.Contains(data.Events, e => e.Kind == SequencedEventKind.NoteOff && e.Channel == 0 && e.Sample == 21600);
        Assert.Equal(data.Events.OrderBy(e => e.Sample).Select(e => e.Sample), data.Events.Select(e => e.Sample));
        Assert.Equal(data.Events[^1].Sample, data.LengthSamples);
        Assert.Equal(0, data.FirstAtOrAfter(0));
        Assert.Equal(data.Events.Length, data.FirstAtOrAfter(long.MaxValue));
    }

    [Fact]
    public void ChannelsSkipThePercussionChannel()
    {
        Assert.Equal([0, 1, 8, 10, 11], new[] { 0, 1, 8, 9, 10 }.Select(i => (int)SequenceData.ChannelOf(i)));
        Assert.DoesNotContain(Enumerable.Range(0, 60), i => SequenceData.ChannelOf(i) == 9);
        Assert.All(Enumerable.Range(0, 60), i => Assert.InRange(SequenceData.ChannelOf(i), 0, 15));
    }

    [Fact]
    public void NotesStartOnTheExactSampleAndNothingSoundsBeforeThem()
    {
        Sequencer sequencer = CreateSequencer(Melody(quarters: 2, delayQuarters: 1), out _);

        float[] audio = RenderAll(sequencer, frames: Rate, block: 480);

        // The first note starts at 0.5 s = sample 24000.
        Assert.All(audio.Take(2 * 24000), sample => Assert.Equal(0f, sample));
        Assert.Contains(audio.Skip(2 * 24000).Take(2 * 480), sample => Math.Abs(sample) > 1e-5f);
        Assert.All(audio, sample => Assert.True(float.IsFinite(sample)));
    }

    [Fact]
    public void OutputIsIdenticalWhateverTheBlockSize()
    {
        Score score = Melody(quarters: 6, instruments: 2);
        float[] reference = RenderAll(CreateSequencer(score, out _), frames: Rate * 2, block: 480);

        foreach (int block in new[] { 1, 7, 64, 100, 1000, 4096 })
        {
            float[] other = RenderAll(CreateSequencer(score, out _), frames: Rate * 2, block);
            Assert.True(reference.AsSpan().SequenceEqual(other), $"block size {block} changed the audio");
        }
    }

    [Fact]
    public void RenderingAllocatesNoMemoryOnceRunning()
    {
        Sequencer sequencer = CreateSequencer(Melody(quarters: 16, instruments: 3), out _);
        float[] block = new float[2 * 240];
        sequencer.StartPlaying();
        for (int i = 0; i < 400; i++)
        {
            sequencer.Render(block, 240); // warm up: first voices, JIT and pools
        }

        Thread.Sleep(300); // let the tiered JIT finish promoting the render path
        for (int i = 0; i < 400; i++)
        {
            sequencer.Render(block, 240);
        }

        Thread.Sleep(300); // let the tiered JIT finish promoting the render path
        for (int i = 0; i < 400; i++)
        {
            sequencer.Render(block, 240);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1500; i++)
        {
            sequencer.Render(block, 240);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(sequencer.PositionSamples > 1500 * 240);
    }

    [Fact]
    public void TransportPausesSeeksAndStopsAtTheEndOfThePiece()
    {
        Sequencer sequencer = CreateSequencer(Melody(quarters: 4), out SequenceData data);
        float[] block = new float[2 * 480];

        sequencer.Render(block, 480);
        Assert.False(sequencer.IsPlaying);
        Assert.Equal(0, sequencer.PositionSamples);

        sequencer.StartPlaying();
        sequencer.Render(block, 480);
        Assert.True(sequencer.IsPlaying);
        Assert.Equal(480, sequencer.PositionSamples);

        sequencer.PausePlaying();
        sequencer.Render(block, 480);
        Assert.False(sequencer.IsPlaying);
        Assert.Equal(480, sequencer.PositionSamples);

        sequencer.Seek(24000);
        sequencer.StartPlaying();
        sequencer.Render(block, 480);
        Assert.Equal(24480, sequencer.PositionSamples);

        for (int i = 0; i < 400 && sequencer.IsPlaying; i++)
        {
            sequencer.Render(block, 480);
        }

        Assert.False(sequencer.IsPlaying);
        Assert.True(sequencer.PositionSamples > data.LengthSamples);
        sequencer.StartPlaying();
        sequencer.Render(block, 480);
        Assert.True(sequencer.IsPlaying);
        Assert.Equal(480, sequencer.PositionSamples);
    }

    [Fact]
    public void ReplacingTheSequenceWhilePlayingKeepsThePositionAndPlaysTheNewNotes()
    {
        Sequencer sequencer = CreateSequencer(Melody(quarters: 2), out _);
        float[] block = new float[2 * 480];
        sequencer.StartPlaying();
        for (int i = 0; i < 10; i++)
        {
            sequencer.Render(block, 480);
        }

        long position = sequencer.PositionSamples;
        sequencer.Load(SequenceData.Build(Interpreter.Interpret(Melody(quarters: 8)), Rate));
        sequencer.Render(block, 480);

        Assert.Equal(position + 480, sequencer.PositionSamples);
        Assert.True(sequencer.LengthSamples > 24000 * 6);
        Assert.True(sequencer.IsPlaying);
    }

    private static float[] RenderAll(Sequencer sequencer, int frames, int block)
    {
        float[] audio = new float[2 * frames];
        sequencer.StartPlaying();
        for (int done = 0; done < frames; done += block)
        {
            int count = Math.Min(block, frames - done);
            sequencer.Render(audio.AsSpan(2 * done, 2 * count), count);
        }

        return audio;
    }

    private static Sequencer CreateSequencer(Score score, out SequenceData data)
    {
        SoundFont soundFont = new(Path.Combine(AppContext.BaseDirectory, "sf_spec_test.sf2"));
        Synthesizer synthesizer = new(soundFont, new SynthesizerSettings(Rate) { EnableReverbAndChorus = false });
        Sequencer sequencer = new(synthesizer);
        data = SequenceData.Build(Interpreter.Interpret(score), Rate);
        sequencer.Load(data);
        return sequencer;
    }

    private static Score Melody(int quarters, int instruments = 1, int delayQuarters = 0)
    {
        ImmutableArray<Instrument>.Builder list = ImmutableArray.CreateBuilder<Instrument>();
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content = ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        int measures = (quarters + delayQuarters + 3) / 4;
        for (int instrument = 0; instrument < instruments; instrument++)
        {
            list.Add(new Instrument($"I{instrument}", [new Staff("S")]));
            int slot = 0;
            for (int m = 0; m < measures; m++)
            {
                List<MusicEvent> events = [];
                for (int beat = 0; beat < 4; beat++, slot++)
                {
                    Fraction onset = new(beat, 4);
                    if (slot < delayQuarters || slot >= delayQuarters + quarters)
                    {
                        events.Add(new Rest(new EventId(Guid.NewGuid()), onset, new Duration(NoteValue.Quarter, 0)));
                    }
                    else
                    {
                        events.Add(new Chord(new EventId(Guid.NewGuid()), onset, new Duration(NoteValue.Quarter, 0),
                            [new Note(new Pitch((Step)((slot + instrument) % 7), 0, 4 - instrument))], StemDirection.Auto));
                    }
                }

                content[new StaffMeasureKey(instrument, m)] = new StaffMeasure([new Voice(1, [.. events])]);
            }
        }

        return new Score(new ScoreMetadata("T", ""), list.ToImmutable(),
            [.. Enumerable.Range(0, measures).Select(m => new Measure(m + 1, new TimeSignature(4, 4)))], content.ToImmutable());
    }
}
