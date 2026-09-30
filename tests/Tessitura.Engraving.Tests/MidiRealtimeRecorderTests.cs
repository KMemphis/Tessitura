using System.Collections.Immutable;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Playback.Audio;
using Tessitura.Playback.Midi;
using Xunit;
using Instrument = Tessitura.Core.Instrument;

namespace Tessitura.Engraving.Tests;

public sealed class MidiRealtimeRecorderTests
{
    [Fact]
    public void MelodyPlayedAtTempoIsQuantizedToEighthNotes()
    {
        ScoreInputController input = CreateInput();
        MidiRealtimeRecorder recorder = new(input);
        recorder.Start(1000, 120);

        Press(recorder, 1008, 60);
        Release(recorder, 1118, 60);
        Press(recorder, 1254, 62);
        Release(recorder, 1372, 62);
        Press(recorder, 1507, 64);
        Release(recorder, 1620, 64);

        Assert.Equal(3, recorder.Stop(1630));

        Chord[] chords = GetChords(input.CurrentScore);
        Assert.Equal(3, chords.Length);
        Assert.Equal([Fraction.Zero, new Fraction(1, 8), new Fraction(1, 4)], chords.Select(chord => chord.Onset));
        Assert.Equal([60, 62, 64], chords.Select(chord => chord.Notes[0].Pitch.MidiNumber));
        Assert.All(chords, chord => Assert.Equal(new Duration(NoteValue.Eighth, 0), chord.Duration));
        Assert.Equal(ScoreInputMode.Selection, input.Mode);
        Assert.Equal(new Fraction(3, 8), input.Cursor.Position);
    }

    [Fact]
    public void RecordingInsertsAnEighthRestForAMissedGridPosition()
    {
        ScoreInputController input = CreateInput();
        MidiRealtimeRecorder recorder = new(input);
        recorder.Start(0, 120);

        Press(recorder, 0, 60);
        Release(recorder, 100, 60);
        Press(recorder, 500, 64);
        Release(recorder, 610, 64);
        recorder.Stop(620);

        MusicEvent[] events = GetEvents(input.CurrentScore);
        Assert.Contains(events, musicEvent => musicEvent is Rest && musicEvent.Onset == new Fraction(1, 8) &&
            musicEvent.Duration.Length == new Fraction(1, 8));
        Assert.Equal([Fraction.Zero, new Fraction(1, 4)], GetChords(input.CurrentScore).Select(chord => chord.Onset));
    }

    [Fact]
    public void RecordingAnchorsToTheNearestRestBoundary()
    {
        ScoreInputController input = CreateInput();
        input.SetCursorPosition(new Fraction(1, 8));
        MidiRealtimeRecorder recorder = new(input);
        recorder.Start(0, 120);

        Press(recorder, 0, 60);
        Release(recorder, 100, 60);
        recorder.Stop(110);

        Assert.Equal(Fraction.Zero, Assert.Single(GetChords(input.CurrentScore)).Onset);
    }

    [Fact]
    public void MetronomeProducesAudibleClicksAtTheSelectedTempo()
    {
        MetronomeClickSource source = new(48000, 120);
        float[] samples = new float[48000 * 2];

        source.Render(samples, 48000);

        Assert.Contains(samples.Take(960 * 2), sample => sample != 0);
        Assert.All(samples.Skip(1200 * 2).Take(1000), sample => Assert.Equal(0, sample));
        Assert.Contains(samples.Skip(24000 * 2).Take(960 * 2), sample => sample != 0);
    }

    [Fact]
    public void MetronomeAudioCallbackDoesNotAllocate()
    {
        MetronomeClickSource source = new(48000, 120);
        float[] block = new float[240 * 2];
        Warm(source, block);
        Thread.Sleep(300);
        Warm(source, block);
        Thread.Sleep(300);
        Warm(source, block);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            source.Render(block, 240);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void Press(MidiRealtimeRecorder recorder, long time, int note) =>
        recorder.OnMessage(new MidiMessage(MidiMessageKind.NoteOn, 0, note, 90), time);

    private static void Release(MidiRealtimeRecorder recorder, long time, int note) =>
        recorder.OnMessage(new MidiMessage(MidiMessageKind.NoteOff, 0, note, 0), time);

    private static void Warm(MetronomeClickSource source, float[] block)
    {
        for (int iteration = 0; iteration < 400; iteration++)
        {
            source.Render(block, 240);
        }
    }

    private static ScoreInputController CreateInput()
    {
        Score score = new(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0),
                new StaffMeasure([new Voice(1, [new Rest(new EventId(Guid.NewGuid()), Fraction.Zero,
                    new Duration(NoteValue.Whole, 0))])] )));
        return new ScoreInputController(score);
    }

    private static MusicEvent[] GetEvents(Score score) =>
        [.. score.Content[new StaffMeasureKey(0, 0)].Voices[0].Events];

    private static Chord[] GetChords(Score score) =>
        [.. score.Content.Values.SelectMany(staffMeasure => staffMeasure.Voices)
            .SelectMany(voice => voice.Events).OfType<Chord>()];
}
