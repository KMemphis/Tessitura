using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Smufl;
using Xunit;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;
using DisplayText = Tessitura.Engraving.DisplayLists.Text;

namespace Tessitura.Engraving.Tests;

public sealed class RepeatEngravingTests
{
    [Fact]
    public void DrawsRepeatBarsVoltasSegnoCodaAndJumpLabels()
    {
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        ScorePageComposer composer = new(metadata, style);
        RepeatInfo?[] repeats =
        [
            new RepeatInfo(StartRepeat: true, Endings: [1]),
            new RepeatInfo(EndRepeat: 2, Endings: [1]),
            new RepeatInfo(StartRepeat: true, Target: RepeatTarget.Segno,
                Jump: RepeatJump.ToCoda, Endings: [2]),
            new RepeatInfo(EndRepeat: 2, Target: RepeatTarget.Coda,
                Jump: RepeatJump.DalSegnoAlCoda),
            null,
        ];
        ImmutableArray<Measure> measures = [.. repeats.Select((repeat, index) =>
            new Measure(index + 1, new TimeSignature(4, 4), Repeat: repeat))];
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        for (int index = 0; index < measures.Length; index++)
        {
            content[new StaffMeasureKey(0, index)] = new StaffMeasure([
                new Voice(1, [new Rest(new EventId(Guid.NewGuid()), Fraction.Zero,
                    new Duration(NoteValue.Whole, 0))])
            ]);
        }

        Score score = new(new ScoreMetadata("Repeat engraving", ""),
            [new Instrument("Piano", [new Staff("Treble")])], measures, content.ToImmutable());
        ScoreLayoutResult layout = new IncrementalScoreLayouter(metadata).Layout(
            score, style, composer.GetAvailableWidth(score));

        ImmutableArray<DisplayLists.DrawingPrimitive> primitives =
            composer.Compose(score, layout, measureIndex: 0).Page.Primitives;
        DisplayGlyph[] glyphs = [.. primitives.OfType<DisplayGlyph>()];
        DisplayText[] texts = [.. primitives.OfType<DisplayText>()];

        Assert.Contains(glyphs, glyph => glyph.Codepoint == metadata.GetGlyphCodepoint("repeatLeft"));
        Assert.Contains(glyphs, glyph => glyph.Codepoint == metadata.GetGlyphCodepoint("repeatRight"));
        DisplayGlyph combined = Assert.Single(glyphs, glyph =>
            glyph.Codepoint == metadata.GetGlyphCodepoint("repeatRightLeft"));
        Assert.Contains(glyphs, glyph => glyph.Codepoint == metadata.GetGlyphCodepoint("segno"));
        Assert.Contains(glyphs, glyph => glyph.Codepoint == metadata.GetGlyphCodepoint("coda"));
        Assert.Contains(texts, text => text.Content == "1.");
        Assert.Contains(texts, text => text.Content == "2.");
        Assert.Contains(texts, text => text.Content == "To Coda");
        Assert.Contains(texts, text => text.Content == "D.S. al Coda");
        Assert.Equal(4, combined.Size);

        DisplayLine[] scoreLines = [.. primitives.OfType<DisplayLine>()
            .Where(static line => line.ElementId.Value == Guid.Empty)];
        Assert.Contains(scoreLines, static line =>
            Math.Abs(line.Start.Y - line.End.Y) < 1e-6 &&
            Math.Abs(line.Start.Y - Math.Round(line.Start.Y)) > 0.05);
        Assert.True(scoreLines.Count(static line =>
                Math.Abs(line.Start.X - line.End.X) < 1e-6 &&
                Math.Abs(line.Start.Y - Math.Round(line.Start.Y)) > 0.05) >= 2,
            "A volta bracket needs two hooks.");
        Assert.Contains(scoreLines, line => Math.Abs(line.Start.Y - line.End.Y) < 1e-6 &&
            Math.Abs(line.StrokeWidth - metadata.GetEngravingDefault("repeatEndingLineThickness")) < 1e-6);
    }

    private static SmuflMetadata LoadMetadata()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Tessitura.sln")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        return SmuflMetadata.Load(Path.Combine(root, "assets", "fonts", "Bravura.json"),
            Path.Combine(root, "assets", "fonts", "smufl_glyph_names.json"));
    }
}
