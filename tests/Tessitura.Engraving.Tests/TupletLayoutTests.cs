using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Smufl;
using Xunit;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;

namespace Tessitura.Engraving.Tests;

public sealed class TupletLayoutTests
{
    [Fact]
    public void TripletIsDrawnWithBracketNumberAndItsAccidentalsAtSoundingPositions()
    {
        Duration eighth = new(NoteValue.Eighth, 0);
        Chord[] notes = [.. new[] { (Step.C, 0), (Step.F, 1), (Step.G, 0) }.Select((p, i) =>
            new Chord(new EventId(Guid.NewGuid()), new Fraction(i, 12), eighth, [new Note(new Pitch(p.Item1, p.Item2, 4))], StemDirection.Auto))];
        TupletGroup group = new(new EventId(Guid.NewGuid()), Fraction.Zero, eighth, 3, 2, [.. notes]);
        Rest rest = new(new EventId(Guid.NewGuid()), new Fraction(1, 4), new Duration(NoteValue.Half, 1));
        Score score = new(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [group, rest])])));
        SmuflMetadata metadata = LoadMetadata();

        ImmutableArray<DisplayLists.DrawingPrimitive> primitives = Compose(score, metadata);

        Assert.Equal(1, primitives.OfType<DisplayGlyph>().Count(g => g.ElementId.Value == group.Id.Value && g.Codepoint == metadata.GetGlyphCodepoint("tuplet3")));
        Assert.True(primitives.OfType<DisplayLine>().Count(l => l.ElementId.Value == group.Id.Value) >= 4, "bracket needs two horizontal parts and two hooks");
        double[] heads = [.. notes.Select(n => primitives.OfType<DisplayGlyph>().Single(g => g.ElementId.Value == n.Id.Value && g.Codepoint == metadata.GetGlyphCodepoint("noteheadBlack")).Origin.X)];
        Assert.True(heads[0] < heads[1] && heads[1] < heads[2]);
        Assert.Equal(heads[1] - heads[0], heads[2] - heads[1], precision: 6);
        Assert.Contains(primitives.OfType<DisplayGlyph>(), g => g.ElementId.Value == notes[1].Id.Value && g.Codepoint == metadata.GetGlyphCodepoint("accidentalSharp"));
    }

    private static ImmutableArray<DisplayLists.DrawingPrimitive> Compose(Score score, SmuflMetadata metadata)
    {
        Style style = Style.CreateDefault(metadata);
        ScorePageComposer composer = new(metadata, style);
        ScoreLayoutResult layout = new IncrementalScoreLayouter(metadata).Layout(score, style, composer.GetAvailableWidth(score));
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
