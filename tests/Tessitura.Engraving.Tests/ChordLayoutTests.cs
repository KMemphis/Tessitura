using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Smufl;
using Xunit;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;
using DisplayPrimitive = Tessitura.Engraving.DisplayLists.DrawingPrimitive;

namespace Tessitura.Engraving.Tests;

public sealed class ChordLayoutTests
{
    private static readonly SmuflMetadata Metadata = LoadMetadata();

    [Fact]
    public void ClusterAlternatesHeadsAroundOneStemWithoutOverlaps()
    {
        Chord chord = Chord((Step.C, 0, 4), (Step.D, 0, 4), (Step.E, 0, 4), (Step.F, 0, 4));

        ImmutableArray<DisplayPrimitive> primitives = Compose(chord);

        DisplayGlyph[] heads = [.. Heads(primitives, chord).OrderBy(g => g.Origin.Y)]; // top to bottom: F E D C
        double width = heads[0].Bounds.Width;
        // Stem up: the upper head of each second goes right. F and D move right; E and C stay.
        Assert.Equal([1, 0, 1, 0], heads.Select(h => Math.Round((h.Origin.X - heads[1].Origin.X) / width)));
        for (int i = 0; i < heads.Length; i++)
        {
            for (int j = i + 1; j < heads.Length; j++)
            {
                Assert.False(Overlap(heads[i], heads[j]), "heads overlap");
            }
        }

        Assert.Single(primitives.OfType<DisplayLine>(), l => l.ElementId.Value == chord.Id.Value && l.Start.X == l.End.X);
    }

    [Fact]
    public void StemDownSecondPutsTheLowerHeadLeft()
    {
        Chord chord = Chord((Step.G, 0, 5), (Step.A, 0, 5));

        DisplayGlyph[] heads = [.. Heads(Compose(chord), chord).OrderBy(g => g.Origin.Y)]; // A5 above G5

        Assert.True(heads[1].Origin.X < heads[0].Origin.X, "the lower head of a stem-down second moves left");
        Assert.False(Overlap(heads[0], heads[1]));
    }

    [Fact]
    public void FiveAccidentalsStackInColumnsWithoutCollisions()
    {
        Chord chord = Chord((Step.C, 1, 4), (Step.E, -1, 4), (Step.G, 1, 4), (Step.B, -1, 4), (Step.D, 1, 5));

        ImmutableArray<DisplayPrimitive> primitives = Compose(chord);

        DisplayGlyph[] accidentals = [.. primitives.OfType<DisplayGlyph>().Where(g => g.ElementId.Value == chord.Id.Value &&
            (g.Codepoint == Metadata.GetGlyphCodepoint("accidentalSharp") || g.Codepoint == Metadata.GetGlyphCodepoint("accidentalFlat")))];
        Assert.Equal(5, accidentals.Length);
        double firstHead = Heads(primitives, chord).Min(h => h.Bounds.X);
        for (int i = 0; i < accidentals.Length; i++)
        {
            Assert.True(accidentals[i].Bounds.X + accidentals[i].Bounds.Width <= firstHead + 1e-6, "accidental must lie left of the heads");
            for (int j = i + 1; j < accidentals.Length; j++)
            {
                Assert.False(Overlap(accidentals[i], accidentals[j]), "accidentals overlap");
            }
        }

        Assert.True(accidentals.Select(a => Math.Round(a.Origin.X, 3)).Distinct().Count() >= 2, "needs more than one column");
    }

    [Fact]
    public void DottedChordPutsOneNonOverlappingColumnOfDotsRightOfAllHeads()
    {
        Chord chord = Chord(new Duration(NoteValue.Quarter, 1), (Step.E, 0, 4), (Step.F, 0, 4), (Step.A, 0, 4));

        ImmutableArray<DisplayPrimitive> primitives = Compose(chord);

        DisplayGlyph[] dots = [.. primitives.OfType<DisplayGlyph>().Where(g => g.ElementId.Value == chord.Id.Value && g.Codepoint == Metadata.GetGlyphCodepoint("augmentationDot"))];
        double rightmost = Heads(primitives, chord).Max(h => h.Bounds.X + h.Bounds.Width);
        Assert.Equal(3, dots.Length);
        Assert.All(dots, d => Assert.True(d.Bounds.X >= rightmost));
        for (int i = 0; i < dots.Length; i++)
        {
            for (int j = i + 1; j < dots.Length; j++)
            {
                Assert.False(Overlap(dots[i], dots[j]), "dots overlap");
            }
        }
    }

    private static bool Overlap(DisplayGlyph a, DisplayGlyph b) =>
        a.Bounds.X < b.Bounds.X + b.Bounds.Width - 1e-6 && b.Bounds.X < a.Bounds.X + a.Bounds.Width - 1e-6 &&
        a.Bounds.Y < b.Bounds.Y + b.Bounds.Height - 1e-6 && b.Bounds.Y < a.Bounds.Y + a.Bounds.Height - 1e-6;

    private static IEnumerable<DisplayGlyph> Heads(ImmutableArray<DisplayPrimitive> primitives, Chord chord) =>
        primitives.OfType<DisplayGlyph>().Where(g => g.ElementId.Value == chord.Id.Value &&
            (g.Codepoint == Metadata.GetGlyphCodepoint("noteheadBlack") || g.Codepoint == Metadata.GetGlyphCodepoint("noteheadHalf")));

    private static Chord Chord(params (Step Step, int Alter, int Octave)[] pitches) => Chord(new Duration(NoteValue.Quarter, 0), pitches);

    private static Chord Chord(Duration duration, params (Step Step, int Alter, int Octave)[] pitches) =>
        new(new EventId(Guid.NewGuid()), Fraction.Zero, duration, [.. pitches.Select(p => new Note(new Pitch(p.Step, p.Alter, p.Octave)))], StemDirection.Auto);

    private static ImmutableArray<DisplayPrimitive> Compose(Chord chord)
    {
        Score score = new(new ScoreMetadata("C", ""), [new Instrument("I", [new Staff("S")])], [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1,
            [chord, new Rest(new EventId(Guid.NewGuid()), chord.Duration.Length, new Duration(NoteValue.Half, 1))])])));
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
