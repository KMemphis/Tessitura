using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Smufl;
using Xunit;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;
using DisplayPrimitive = Tessitura.Engraving.DisplayLists.DrawingPrimitive;

namespace Tessitura.Engraving.Tests;

public sealed class VoiceLayoutTests
{
    [Fact]
    public void TwoVoicesGetOppositeStemsAndClashingHeadsDoNotOverlap()
    {
        Chord upper = Note(Step.E, 5);
        Chord lower = Note(Step.D, 5); // a second below: the heads clash
        Score score = Build(new List<MusicEvent>[] { [upper, Rest(new Fraction(1, 4), NoteValue.Half, 1)], [lower, Rest(new Fraction(1, 4), NoteValue.Half, 1)] });

        ImmutableArray<DisplayPrimitive> primitives = Compose(score);

        DisplayLine stemUp = Assert.Single(primitives.OfType<DisplayLine>(), l => l.ElementId.Value == upper.Id.Value);
        DisplayLine stemDown = Assert.Single(primitives.OfType<DisplayLine>(), l => l.ElementId.Value == lower.Id.Value);
        Assert.True(stemUp.End.Y < stemUp.Start.Y, "voice 1 stem must point up");
        Assert.True(stemDown.End.Y > stemDown.Start.Y, "voice 2 stem must point down");
        DisplayGlyph head1 = Head(primitives, upper);
        DisplayGlyph head2 = Head(primitives, lower);
        Assert.False(Intersects(head1, head2), "clashing noteheads overlap");
        Assert.True(head2.Bounds.X >= head1.Bounds.X + head1.Bounds.Width - 1e-6, "the lower voice head moves right");
    }

    [Fact]
    public void UnisonOfEqualValuesSharesTheHeadPosition()
    {
        Chord a = Note(Step.G, 4);
        Chord b = Note(Step.G, 4);
        ImmutableArray<DisplayPrimitive> primitives = Compose(Build(new List<MusicEvent>[] { [a, Rest(new Fraction(1, 4), NoteValue.Half, 1)], [b, Rest(new Fraction(1, 4), NoteValue.Half, 1)] }));

        Assert.Equal(Head(primitives, a).Bounds.X, Head(primitives, b).Bounds.X, precision: 6);
    }

    [Fact]
    public void FourVoicesKeepTheirRestsApart()
    {
        List<Rest> rests = [.. Enumerable.Range(0, 4).Select(_ => Rest(Fraction.Zero, NoteValue.Whole, 0))];
        Score score = Build(new List<MusicEvent>[] { [rests[0]], [rests[1]], [rests[2]], [rests[3]] });

        ImmutableArray<DisplayPrimitive> primitives = Compose(score);

        List<DisplayGlyph> glyphs = [.. rests.Select(r => primitives.OfType<DisplayGlyph>().Single(g => g.ElementId.Value == r.Id.Value))];
        for (int i = 0; i < glyphs.Count; i++)
        {
            for (int j = i + 1; j < glyphs.Count; j++)
            {
                Assert.False(Intersects(glyphs[i], glyphs[j]), $"rests of voices {i + 1} and {j + 1} overlap");
            }
        }

        Assert.True(glyphs[0].Origin.Y < glyphs[1].Origin.Y, "voice 1 rest sits above voice 2");
    }

    [Fact]
    public void FourVoiceNotesAlternateStemDirections()
    {
        Chord[] notes = [Note(Step.C, 5), Note(Step.A, 4), Note(Step.F, 4), Note(Step.C, 4)];
        Score score = Build(new List<MusicEvent>[] { [notes[0], Rest(new Fraction(1, 4), NoteValue.Half, 1)], [notes[1], Rest(new Fraction(1, 4), NoteValue.Half, 1)],
            [notes[2], Rest(new Fraction(1, 4), NoteValue.Half, 1)], [notes[3], Rest(new Fraction(1, 4), NoteValue.Half, 1)] });

        ImmutableArray<DisplayPrimitive> primitives = Compose(score);

        bool[] up = [.. notes.Select(n => primitives.OfType<DisplayLine>().Single(l => l.ElementId.Value == n.Id.Value && l.Start.X == l.End.X).End.Y
            < primitives.OfType<DisplayLine>().Single(l => l.ElementId.Value == n.Id.Value && l.Start.X == l.End.X).Start.Y)];
        Assert.Equal([true, false, true, false], up);
    }

    private static bool Intersects(DisplayGlyph a, DisplayGlyph b) =>
        a.Bounds.X < b.Bounds.X + b.Bounds.Width - 1e-6 && b.Bounds.X < a.Bounds.X + a.Bounds.Width - 1e-6 &&
        a.Bounds.Y < b.Bounds.Y + b.Bounds.Height - 1e-6 && b.Bounds.Y < a.Bounds.Y + a.Bounds.Height - 1e-6;

    private static DisplayGlyph Head(ImmutableArray<DisplayPrimitive> primitives, Chord chord) =>
        primitives.OfType<DisplayGlyph>().Single(g => g.ElementId.Value == chord.Id.Value && g.Codepoint == Metadata.GetGlyphCodepoint("noteheadBlack"));

    private static Chord Note(Step step, int octave) =>
        new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Quarter, 0), [new Note(new Pitch(step, 0, octave))], StemDirection.Auto);

    private static Rest Rest(Fraction onset, NoteValue value, int dots) => new(new EventId(Guid.NewGuid()), onset, new Duration(value, dots));

    private static Score Build(List<MusicEvent>[] voices)
    {
        List<Voice> built = [];
        for (int i = 0; i < voices.Length; i++)
        {
            List<MusicEvent> events = voices[i];
            Fraction end = events.Count == 0 ? Fraction.Zero : events[^1].Onset + events[^1].Duration.Length;
            if (end < Fraction.One && events.Count > 0 && events[^1] is Chord)
            {
                events.Add(Rest(end, NoteValue.Half, 1));
            }

            built.Add(new Voice(i + 1, [.. events]));
        }

        return new Score(new ScoreMetadata("V", ""), [new Instrument("I", [new Staff("S")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), new StaffMeasure([.. built])));
    }

    private static SmuflMetadata Metadata { get; } = LoadMetadata();

    private static ImmutableArray<DisplayPrimitive> Compose(Score score)
    {
        Style style = Style.CreateDefault(Metadata);
        ScorePageComposer composer = new(Metadata, style);
        ScoreLayoutResult layout = new IncrementalScoreLayouter(Metadata).Layout(score, style, composer.GetAvailableWidth(score));
        return composer.Compose(score, layout, 0).Page.Primitives;
    }

    private static SmuflMetadata LoadMetadata()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Tessitura.sln")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        return SmuflMetadata.Load(Path.Combine(root, "assets", "fonts", "Bravura.json"), Path.Combine(root, "assets", "fonts", "smufl_glyph_names.json"));
    }
}
