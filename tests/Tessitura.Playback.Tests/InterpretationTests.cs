using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Playback.Performance;
using Xunit;

namespace Tessitura.Playback.Tests;

public sealed class InterpretationTests
{
    [Theory]
    [InlineData(Articulation.Staccato, 1, 2)]
    [InlineData(Articulation.Tenuto, 1, 1)]
    [InlineData(Articulation.Staccatissimo, 1, 4)]
    [InlineData(Articulation.Marcato, 17, 20)]
    public void ArticulationsSetTheSoundingShareOfTheNotatedLength(Articulation kind, int num, int den)
    {
        Chord note = Quarter(Fraction.Zero, Step.C, 4);
        Score score = SingleStaff([note]);

        Interpretation result = Interpreter.Interpret(score, new PerformanceHints([], [new ArticulationMark(note.Id, kind)], []));

        PerformedNote performed = Assert.Single(result.Notes);
        Assert.Equal(new Fraction(1, 4), performed.NotatedLength);
        Assert.Equal(new Fraction(1, 4) * new Fraction(num, den), performed.SoundingLength);
    }

    [Fact]
    public void DefinitionValuesAreStaccatoFiftyPercentAndTenutoOneHundredPercent()
    {
        InterpretationSettings settings = new();

        Assert.Equal(new Fraction(1, 2), settings.StaccatoGate);
        Assert.Equal(Fraction.One, settings.TenutoGate);
        Assert.True(settings.NormalGate < settings.TenutoGate);
        Assert.True(settings.StaccatissimoGate < settings.StaccatoGate);
    }

    [Fact]
    public void UnmarkedNotesUseTheNormalGateAndMezzoForteVelocity()
    {
        Chord note = Quarter(Fraction.Zero, Step.C, 4);

        PerformedNote performed = Assert.Single(Interpreter.Interpret(SingleStaff([note])).Notes);

        Assert.Equal(new Fraction(1, 4) * new Fraction(9, 10), performed.SoundingLength);
        Assert.Equal(80, performed.Velocity);
        Assert.Equal(60, performed.Midi);
    }

    [Fact]
    public void DynamicsMapToVelocityFromTheAnchoredEventOnwardAcrossTheInstrument()
    {
        Chord[] notes = [Quarter(Fraction.Zero, Step.C, 4), Quarter(new Fraction(1, 4), Step.D, 4),
            Quarter(new Fraction(1, 2), Step.E, 4), Quarter(new Fraction(3, 4), Step.F, 4)];
        Score score = SingleStaff(notes);
        PerformanceHints hints = new([new DynamicMark(notes[1].Id, Dynamic.P), new DynamicMark(notes[3].Id, Dynamic.Ff)], [], []);

        Interpretation result = Interpreter.Interpret(score, hints);

        Assert.Equal([80, 49, 49, 112], result.Notes.Select(n => n.Velocity));
        InterpretationSettings settings = new();
        Assert.Equal([16, 33, 49, 64, 80, 96, 112, 126], Enum.GetValues<Dynamic>().Select(settings.VelocityOf));
    }

    [Fact]
    public void AccentAndMarcatoRaiseVelocityWithoutLeavingTheMidiRange()
    {
        Chord accent = Quarter(Fraction.Zero, Step.C, 4);
        Chord marcato = Quarter(new Fraction(1, 4), Step.D, 4);
        Chord loud = Quarter(new Fraction(1, 2), Step.E, 4);
        Score score = SingleStaff([accent, marcato, loud]);
        PerformanceHints hints = new([new DynamicMark(loud.Id, Dynamic.Fff)],
            [new ArticulationMark(accent.Id, Articulation.Accent), new ArticulationMark(marcato.Id, Articulation.Marcato),
                new ArticulationMark(loud.Id, Articulation.Marcato)], []);

        Interpretation result = Interpreter.Interpret(score, hints);

        Assert.Equal([96, 104, 127], result.Notes.Select(n => n.Velocity));
    }

    [Fact]
    public void ShortestGateWinsWhenArticulationsAreCombined()
    {
        Chord note = Quarter(Fraction.Zero, Step.C, 4);
        PerformanceHints hints = new([], [new ArticulationMark(note.Id, Articulation.Tenuto), new ArticulationMark(note.Id, Articulation.Staccato),
            new ArticulationMark(note.Id, Articulation.Accent)], []);

        PerformedNote performed = Assert.Single(Interpreter.Interpret(SingleStaff([note]), hints).Notes);

        Assert.Equal(new Fraction(1, 8), performed.SoundingLength);
        Assert.Equal(96, performed.Velocity);
    }

    [Fact]
    public void TiedNotesSoundAsOneNoteAndTheGateAppliesToTheMergedLength()
    {
        Chord first = new(new EventId(Guid.NewGuid()), new Fraction(3, 4), new Duration(NoteValue.Quarter, 0),
            [new Note(new Pitch(Step.G, 0, 4), TiedToNext: true)], StemDirection.Auto);
        Chord second = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Half, 0),
            [new Note(new Pitch(Step.G, 0, 4))], StemDirection.Auto);
        Score score = new(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])],
            [new Measure(1, new TimeSignature(4, 4)), new Measure(2, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [Rest(Fraction.Zero, NoteValue.Half, 1), first])]))
                .Add(new StaffMeasureKey(0, 1), new StaffMeasure([new Voice(1, [second, Rest(new Fraction(1, 2), NoteValue.Half, 0)])])));

        Interpretation result = Interpreter.Interpret(score, new PerformanceHints([], [new ArticulationMark(first.Id, Articulation.Tenuto)], []));

        PerformedNote merged = Assert.Single(result.Notes);
        Assert.Equal(new Fraction(3, 4), merged.Start);
        Assert.Equal(new Fraction(3, 4), merged.NotatedLength);
        Assert.Equal(new Fraction(3, 4), merged.SoundingLength);
    }

    [Fact]
    public void NotesAreSortedAcrossInstrumentsStavesAndVoices()
    {
        Chord early = Quarter(Fraction.Zero, Step.C, 4);
        Chord late = Quarter(new Fraction(1, 4), Step.E, 4);
        Chord low = Quarter(Fraction.Zero, Step.C, 3);
        Score score = new(new ScoreMetadata("T", ""),
            [new Instrument("A", [new Staff("a")]), new Instrument("B", [new Staff("b", Clef.Bass), new Staff("c")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [late, Rest(Fraction.Zero, NoteValue.Quarter, 0)])]))
                .Add(new StaffMeasureKey(1, 0), new StaffMeasure([new Voice(1, [low, Rest(new Fraction(1, 4), NoteValue.Half, 1)])]))
                .Add(new StaffMeasureKey(2, 0), new StaffMeasure([new Voice(1, [early, Rest(new Fraction(1, 4), NoteValue.Half, 1)])])));

        Interpretation result = Interpreter.Interpret(score);

        Assert.Equal([(Fraction.Zero, 1, 48), (Fraction.Zero, 1, 60), (new Fraction(1, 4), 0, 64)],
            result.Notes.Select(n => (n.Start, n.Instrument, n.Midi)));
    }

    [Fact]
    public void TempoMapConvertsExactPositionsToSecondsAcrossTempoChanges()
    {
        TempoMap map = new(120, [new TempoMark(new Fraction(1, 1), 60), new TempoMark(new Fraction(2, 1), 240)]);

        Assert.Equal(0, map.SecondsAt(Fraction.Zero));
        Assert.Equal(0.5, map.SecondsAt(new Fraction(1, 4)), precision: 12);
        Assert.Equal(2.0, map.SecondsAt(Fraction.One), precision: 12);
        Assert.Equal(4.0, map.SecondsAt(new Fraction(3, 2)), precision: 12);
        Assert.Equal(6.0, map.SecondsAt(new Fraction(2, 1)), precision: 12);
        Assert.Equal(6.25, map.SecondsAt(new Fraction(9, 4)), precision: 12);
        Assert.Equal(120, map.QuarterNotesPerMinuteAt(new Fraction(3, 4)));
        Assert.Equal(60, map.QuarterNotesPerMinuteAt(Fraction.One));
        Assert.Equal(240, map.QuarterNotesPerMinuteAt(new Fraction(5, 2)));
    }

    [Fact]
    public void TempoMapHandlesUnsortedMarksLateSamePositionWinsAndRejectsBadTempos()
    {
        TempoMap map = new(100, [new TempoMark(new Fraction(2, 1), 80), new TempoMark(Fraction.Zero, 120), new TempoMark(new Fraction(2, 1), 60)]);

        Assert.Equal(120, map.QuarterNotesPerMinuteAt(Fraction.One));
        Assert.Equal(60, map.QuarterNotesPerMinuteAt(new Fraction(2, 1)));
        Assert.Equal(4.0 + 1.0, map.SecondsAt(new Fraction(9, 4)), precision: 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TempoMap(0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TempoMap(double.NaN, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TempoMap(120, [new TempoMark(Fraction.One, -5)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.SecondsAt(new Fraction(-1, 4)));
    }

    [Fact]
    public void InterpretationCarriesTheTempoMapOfItsHints()
    {
        Score score = SingleStaff([Quarter(Fraction.Zero, Step.C, 4)]);

        Interpretation result = Interpreter.Interpret(score, new PerformanceHints([], [], [new TempoMark(Fraction.Zero, 90)]),
            new InterpretationSettings { DefaultTempo = 120 });

        Assert.Equal(90, result.Tempo.QuarterNotesPerMinuteAt(Fraction.Zero));
        Assert.Equal(60.0 / 90 * 4, result.Tempo.SecondsAt(Fraction.One), precision: 12);
    }

    private static Chord Quarter(Fraction onset, Step step, int octave) =>
        new(new EventId(Guid.NewGuid()), onset, new Duration(NoteValue.Quarter, 0), [new Note(new Pitch(step, 0, octave))], StemDirection.Auto);

    private static Rest Rest(Fraction onset, NoteValue value, int dots) =>
        new(new EventId(Guid.NewGuid()), onset, new Duration(value, dots));

    private static Score SingleStaff(Chord[] notes)
    {
        List<MusicEvent> events = [.. notes];
        Fraction end = notes.Length == 0 ? Fraction.Zero : notes[^1].Onset + notes[^1].Duration.Length;
        if (end < Fraction.One)
        {
            events.Add(Rest(end, NoteValue.Quarter, 0));
        }

        return new Score(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [.. events])])));
    }
}

public sealed class TupletInterpretationTests
{
    [Fact]
    public void TripletMembersSoundAThirdOfAQuarterEach()
    {
        Duration eighth = new(NoteValue.Eighth, 0);
        Chord[] notes = [.. Enumerable.Range(0, 3).Select(i => new Chord(new EventId(Guid.NewGuid()), new Fraction(i, 12), eighth,
            [new Note(new Pitch(Step.C, 0, 4 + i))], StemDirection.Auto))];
        TupletGroup group = new(new EventId(Guid.NewGuid()), Fraction.Zero, eighth, 3, 2, [.. notes]);
        Score score = new(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])],
            [new Measure(1, new TimeSignature(4, 4))],
            System.Collections.Immutable.ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0),
                new StaffMeasure([new Voice(1, [group, new Rest(new EventId(Guid.NewGuid()), new Fraction(1, 4), new Duration(NoteValue.Half, 1))])])));

        Interpretation result = Interpreter.Interpret(score);

        Assert.Equal([Fraction.Zero, new Fraction(1, 12), new Fraction(1, 6)], result.Notes.Select(n => n.Start));
        Assert.All(result.Notes, n => Assert.Equal(new Fraction(1, 12), n.NotatedLength));
        Assert.Equal(0.5, result.Tempo.SecondsAt(group.Length), precision: 12);
    }
}

public sealed class AttachedMarkInterpretationTests
{
    [Fact]
    public void ArticulationsAndDynamicsAttachedToTheScoreDriveTheInterpretation()
    {
        Chord first = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Quarter, 0), [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Chord second = new(new EventId(Guid.NewGuid()), new Fraction(1, 4), new Duration(NoteValue.Quarter, 0), [new Note(new Pitch(Step.D, 0, 4))], StemDirection.Auto);
        Score score = new(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])], [new Measure(1, new TimeSignature(4, 4))],
            System.Collections.Immutable.ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0),
                new StaffMeasure([new Voice(1, [first, second, new Rest(new EventId(Guid.NewGuid()), new Fraction(1, 2), new Duration(NoteValue.Half, 0))])])),
            [new ArticulationAttachment(first.Id, ArticulationKind.Staccato), new ArticulationAttachment(second.Id, ArticulationKind.Tenuto),
                new DynamicAttachment(first.Id, DynamicLevel.P), new ArticulationAttachment(first.Id, ArticulationKind.Fermata)]);

        Interpretation result = Interpreter.Interpret(score);

        Assert.Equal(new Fraction(1, 8), result.Notes[0].SoundingLength); // staccato: half of the notated length
        Assert.Equal(new Fraction(1, 4), result.Notes[1].SoundingLength); // tenuto: the whole value
        Assert.Equal([49, 49], result.Notes.Select(n => n.Velocity));
    }
}

public sealed class TempoAttachmentTests
{
    [Fact]
    public void TempoAttachmentsBecomeTempoMarksAtTheirEventPositionInQuarterNotes()
    {
        Chord a = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Whole, 0), [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Chord b = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Whole, 0), [new Note(new Pitch(Step.D, 0, 4))], StemDirection.Auto);
        Score score = new(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])],
            [new Measure(1, new TimeSignature(4, 4)), new Measure(2, new TimeSignature(4, 4))],
            System.Collections.Immutable.ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [a])]))
                .Add(new StaffMeasureKey(0, 1), new StaffMeasure([new Voice(1, [b])])),
            [new TempoAttachment(a.Id, new Duration(NoteValue.Quarter, 0), 120), new TempoAttachment(b.Id, new Duration(NoteValue.Half, 0), 30)]);

        Interpretation result = Interpreter.Interpret(score);

        Assert.Equal(120, result.Tempo.QuarterNotesPerMinuteAt(Fraction.Zero));
        Assert.Equal(60, result.Tempo.QuarterNotesPerMinuteAt(Fraction.One)); // half = 30 is 60 quarters per minute
        Assert.Equal(2.0 + 4.0, result.Tempo.SecondsAt(new Fraction(2, 1)), precision: 9);
    }
}
