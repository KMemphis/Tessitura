using System.Collections.Immutable;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Playback.Midi;
using Xunit;
using Instrument = Tessitura.Core.Instrument;

namespace Tessitura.Engraving.Tests;

public sealed class MidiStepInputTests
{
    [Fact]
    public void KeyboardChordIsWrittenAsOneChordAtTheCursor()
    {
        (ScoreInputController input, MidiStepInput midi) = Create();
        input.EnterNoteEntry();

        Press(midi, 0, 60, 64, 67);
        Release(midi, 10, 60, 64, 67);

        Chord chord = Assert.IsType<Chord>(input.CurrentScore.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[0]);
        Assert.Equal([60, 64, 67], chord.Notes.Select(n => n.Pitch.MidiNumber).Order());
        Assert.Equal(new Fraction(1, 4), input.Cursor.Position);
    }

    [Fact]
    public void SeparatePressesBecomeSuccessiveNotesAndSharpsFollowTheKey()
    {
        (ScoreInputController input, MidiStepInput midi) = Create();
        input.EnterNoteEntry();

        midi.OnMessage(new MidiMessage(MidiMessageKind.NoteOn, 0, 61, 90), 0);
        midi.OnMessage(new MidiMessage(MidiMessageKind.NoteOff, 0, 61, 0), 50);
        midi.OnMessage(new MidiMessage(MidiMessageKind.NoteOn, 0, 62, 90), 500);
        midi.OnMessage(new MidiMessage(MidiMessageKind.NoteOff, 0, 62, 0), 600);
        midi.Tick(2000);

        Chord[] chords = [.. input.CurrentScore.Content[new StaffMeasureKey(0, 0)].Voices[0].Events.OfType<Chord>()];
        Assert.Equal(2, chords.Length);
        Assert.Equal(new Pitch(Step.C, 1, 4), chords[0].Notes[0].Pitch);
        Assert.Equal(new Pitch(Step.D, 0, 4), chords[1].Notes[0].Pitch);
        Assert.Equal(new Fraction(1, 2), input.Cursor.Position);
    }

    [Fact]
    public void FlatKeyUsesFlatsAndNothingIsWrittenOutsideNoteEntry()
    {
        (ScoreInputController input, MidiStepInput midi) = Create(fifths: -2);

        Press(midi, 0, 70);
        Release(midi, 5, 70);
        Assert.Empty(input.CurrentScore.Content[new StaffMeasureKey(0, 0)].Voices[0].Events.OfType<Chord>());

        input.EnterNoteEntry();
        Press(midi, 1000, 70);
        Release(midi, 1005, 70);
        Chord chord = Assert.IsType<Chord>(input.CurrentScore.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[0]);
        Assert.Equal(new Pitch(Step.B, -1, 4), chord.Notes[0].Pitch);
    }

    [Fact]
    public void UndoRemovesAWholeKeyboardChordStepByStep()
    {
        (ScoreInputController input, MidiStepInput midi) = Create();
        input.EnterNoteEntry();
        Press(midi, 0, 60, 64);
        Release(midi, 5, 60, 64);

        input.Undo();
        input.Undo();

        Assert.Empty(input.CurrentScore.Content[new StaffMeasureKey(0, 0)].Voices[0].Events.OfType<Chord>());
    }

    [Fact]
    public void PortsReportTheirSupportAndDeviceLists()
    {
        IMidiPort unsupported = new UnsupportedMidiPort();
        Assert.False(unsupported.IsSupported);
        Assert.Empty(unsupported.GetInputs());
        Assert.Throws<NotSupportedException>(() => unsupported.OpenInput("x"));

        using IMidiPort port = DryWetMidiPort.CreateForThisSystem();
        Assert.Equal(OperatingSystem.IsMacOS() || OperatingSystem.IsWindows(), port.IsSupported);
        if (port.IsSupported)
        {
            Assert.NotNull(port.GetInputs());
            Assert.NotNull(port.GetOutputs());
            Assert.Throws<InvalidOperationException>(() => port.Send(new MidiMessage(MidiMessageKind.NoteOn, 0, 60, 90)));
        }
    }

    private static void Press(MidiStepInput midi, long time, params int[] notes)
    {
        foreach (int note in notes)
        {
            midi.OnMessage(new MidiMessage(MidiMessageKind.NoteOn, 0, note, 90), time);
        }
    }

    private static void Release(MidiStepInput midi, long time, params int[] notes)
    {
        foreach (int note in notes)
        {
            midi.OnMessage(new MidiMessage(MidiMessageKind.NoteOff, 0, note, 0), time);
        }
    }

    private static (ScoreInputController, MidiStepInput) Create(int fifths = 0)
    {
        Rest rest = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Whole, 0));
        Score score = new(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])],
            [new Measure(1, new TimeSignature(4, 4), new KeySignature(fifths))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [rest])])));
        ScoreInputController input = new(score);
        return (input, new MidiStepInput(input));
    }
}
