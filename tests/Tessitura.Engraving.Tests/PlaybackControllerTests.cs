using System.Collections.Immutable;
using MeltySynth;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Playback.Audio;
using Xunit;
using Instrument = Tessitura.Core.Instrument;

namespace Tessitura.Engraving.Tests;

public sealed class PlaybackControllerTests
{
    [Fact]
    public void SpaceStartsPlaybackAndTheCursorFollowsTheSoundWithinOneFrame()
    {
        ScoreInputController input = new(CreateScore(measures: 4));
        FakeOutput output = new();
        using PlaybackController playback = new(input, rate => new Synthesizer(
            new SoundFont(Path.Combine(AppContext.BaseDirectory, "sf_spec_test.sf2")), new SynthesizerSettings(rate)), () => output);
        string settings = Path.Combine(Path.GetTempPath(), $"tessitura-play-{Guid.NewGuid():N}.json");
        try
        {
            ActionRegistry actions = ActionRegistry.LoadOrCreate(playback.CreateActions(), settings);
            Assert.True(actions.TryExecute(Avalonia.Input.Key.Space, Avalonia.Input.KeyModifiers.None));
            Assert.True(playback.IsPlaying);

            double worst = 0;
            const double frame = 1.0 / 60;
            for (int tick = 1; tick <= 240; tick++)
            {
                // 16.67 ms of audio per display frame, delivered in 240-frame device callbacks.
                output.RenderSeconds(frame);
                playback.Tick();
                double audible = playback.PositionSamples / 48000.0 - output.BufferMilliseconds / 1000.0;
                double shown = SecondsOf(input.Cursor.Position);
                worst = Math.Max(worst, Math.Abs(audible - shown));
            }

            Assert.True(worst < frame, $"cursor is {worst * 1000:F1} ms away from the sound");
            Assert.True(input.Cursor.Position > new Fraction(1, 1));
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void PauseKeepsTheCursorAndStopRewinds()
    {
        ScoreInputController input = new(CreateScore(measures: 4));
        FakeOutput output = new();
        using PlaybackController playback = new(input, rate => new Synthesizer(
            new SoundFont(Path.Combine(AppContext.BaseDirectory, "sf_spec_test.sf2")), new SynthesizerSettings(rate)), () => output);

        playback.Toggle();
        output.RenderSeconds(1.0);
        playback.Toggle();
        Fraction paused = input.Cursor.Position;

        Assert.False(playback.IsPlaying);
        Assert.True(paused > Fraction.Zero);
        output.RenderSeconds(0.5);
        Assert.Equal(paused, input.Cursor.Position);

        playback.Toggle();
        output.RenderSeconds(0.2);
        Assert.True(playback.IsPlaying);
        playback.StopAndRewind();
        Assert.False(playback.IsPlaying);
        Assert.Equal(Fraction.Zero, input.Cursor.Position);
    }

    [Fact]
    public void PlaybackStartsFromTheCursorAndStopsAtTheEnd()
    {
        ScoreInputController input = new(CreateScore(measures: 2));
        input.SetCursorPosition(new Fraction(1, 1));
        FakeOutput output = new();
        using PlaybackController playback = new(input, rate => new Synthesizer(
            new SoundFont(Path.Combine(AppContext.BaseDirectory, "sf_spec_test.sf2")), new SynthesizerSettings(rate)), () => output);

        playback.Start();
        output.RenderSeconds(0.1);
        playback.Tick();
        Assert.True(input.Cursor.Position >= new Fraction(1, 1));

        output.RenderSeconds(4);
        playback.Tick();
        Assert.False(playback.IsPlaying);
        Assert.False(output.Running);
    }

    private static double SecondsOf(Fraction position) => (double)position.Num / position.Den * 4 * 0.5;

    private static Score CreateScore(int measures)
    {
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content = ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        for (int m = 0; m < measures; m++)
        {
            List<MusicEvent> events = [];
            for (int beat = 0; beat < 4; beat++)
            {
                events.Add(new Chord(new EventId(Guid.NewGuid()), new Fraction(beat, 4), new Duration(NoteValue.Quarter, 0),
                    [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto));
            }

            content[new StaffMeasureKey(0, m)] = new StaffMeasure([new Voice(1, [.. events])]);
        }

        return new Score(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])],
            [.. Enumerable.Range(0, measures).Select(m => new Measure(m + 1, new TimeSignature(4, 4)))], content.ToImmutable());
    }

    private sealed class FakeOutput : IAudioOutput
    {
        private IAudioSource? _source;
        private readonly float[] _block = new float[2 * 240];

        public int SampleRate => 48000;

        public int BufferFrames => 240;

        public double BufferMilliseconds => 10;

        public long CallbackCount { get; private set; }

        public long LateCallbackCount => 0;

        public bool Running { get; private set; }

        public void Start(IAudioSource source)
        {
            _source = source;
            Running = true;
        }

        public void Stop() => Running = false;

        public void Dispose() => Running = false;

        public void RenderSeconds(double seconds)
        {
            if (!Running || _source is null)
            {
                return;
            }

            int blocks = (int)Math.Round(seconds * SampleRate / 240);
            for (int i = 0; i < blocks; i++)
            {
                _source.Render(_block, 240);
                CallbackCount++;
            }
        }
    }
}
