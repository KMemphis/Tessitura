using System.Text.RegularExpressions;
using SkiaSharp;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Engraving.DisplayLists;
using Tessitura.Rendering;
using Tessitura.Smufl;
using Xunit;
using Path = System.IO.Path;

namespace Tessitura.Engraving.Tests;

public sealed class ImageExporterTests
{
    [Fact]
    public void PngAndSvgMatchThePdfPageAndDrawTheSameContent()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tessitura-img-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            (Page page, DisplayListRenderer renderer) = CreateReference();
            using (renderer)
            {
                string pdf = Path.Combine(directory, "a.pdf");
                string png72 = Path.Combine(directory, "a72.png");
                string png300 = Path.Combine(directory, "a300.png");
                string svg = Path.Combine(directory, "a.svg");
                PdfExporter.Export(pdf, [page], renderer);
                ImageExporter.ExportPng(png72, page, renderer, dpi: 72);
                ImageExporter.ExportPng(png300, page, renderer, dpi: 300);
                ImageExporter.ExportSvg(svg, page, renderer);

                // The PDF page is page size times points-per-staff-space; PNG at 72 dpi has that many pixels.
                string pdfText = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(pdf));
                Match box = Regex.Match(pdfText, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)");
                double pdfWidth = double.Parse(box.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                double pdfHeight = double.Parse(box.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                using SKBitmap small = SKBitmap.Decode(png72);
                using SKBitmap large = SKBitmap.Decode(png300);
                Assert.Equal((int)Math.Ceiling(pdfWidth), small.Width);
                Assert.Equal((int)Math.Ceiling(pdfHeight), small.Height);
                Assert.Equal(ImageExporter.PixelSize(page, 300), (large.Width, large.Height));
                Assert.True(large.Width > small.Width * 4 - 4);

                // The SVG canvas is the same size in points and holds one element per drawn primitive kind.
                string svgText = File.ReadAllText(svg);
                Match size = Regex.Match(svgText, @"width=""([\d.]+)""\s+height=""([\d.]+)""");
                Assert.True(size.Success);
                Assert.Equal(pdfWidth, double.Parse(size.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), precision: 0);
                Assert.Equal(pdfHeight, double.Parse(size.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), precision: 0);
                Assert.Contains("<path", svgText);
                Assert.True(CountDark(small) > 200, "the PNG is blank");
                Assert.True(CountDark(large) > CountDark(small), "the higher resolution has no more ink");
                // Ink coverage of the 300 dpi image, scaled down, agrees with the 72 dpi image (same drawing pass).
                double ratio = CountDark(large) / (300.0 / 72 * 300.0 / 72) / CountDark(small);
                Assert.InRange(ratio, 0.7, 1.4);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RejectsResolutionsOutsideTheSupportedRange()
    {
        (Page page, DisplayListRenderer renderer) = CreateReference();
        using (renderer)
        {
            string path = Path.Combine(Path.GetTempPath(), $"tessitura-bad-{Guid.NewGuid():N}.png");
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageExporter.ExportPng(path, page, renderer, dpi: 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageExporter.PixelSize(page, 5000));
            Assert.False(File.Exists(path));
        }
    }

    private static int CountDark(SKBitmap bitmap)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor color = bitmap.GetPixel(x, y);
                if (color.Red < 128 && color.Green < 128 && color.Blue < 128 && color.Alpha > 128)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static (Page, DisplayListRenderer) CreateReference()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Tessitura.sln")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        SmuflMetadata metadata = SmuflMetadata.Load(Path.Combine(root, "assets", "fonts", "Bravura.json"),
            Path.Combine(root, "assets", "fonts", "smufl_glyph_names.json"));
        Style style = Style.CreateDefault(metadata);
        Score score = MusicXmlScore();
        ScorePageComposer composer = new(metadata, style);
        ScoreLayoutResult layout = new IncrementalScoreLayouter(metadata).Layout(score, style, composer.GetAvailableWidth(score));
        Page page = composer.Compose(score, layout, 0).Page;
        DisplayListRenderer renderer = new(Path.Combine(root, "assets", "fonts", "Bravura.otf"),
            Path.Combine(root, "assets", "fonts", "NotoSerif[wdth,wght].ttf"));
        return (page, renderer);
    }

    private static Score MusicXmlScore()
    {
        Rest rest = new(new EventId(Guid.NewGuid()), new Fraction(1, 2), new Duration(NoteValue.Half, 0));
        Chord a = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Quarter, 0), [new Note(new Pitch(Step.E, 0, 4))], StemDirection.Auto);
        Chord b = new(new EventId(Guid.NewGuid()), new Fraction(1, 4), new Duration(NoteValue.Quarter, 0), [new Note(new Pitch(Step.G, 1, 4))], StemDirection.Auto);
        return new Score(new ScoreMetadata("Referencia", "Tessitura"), [new Instrument("I", [new Staff("S")])],
            [new Measure(1, new TimeSignature(4, 4), new KeySignature(2))],
            System.Collections.Immutable.ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [a, b, rest])])));
    }
}
