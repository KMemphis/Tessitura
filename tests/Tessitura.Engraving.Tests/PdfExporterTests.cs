using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;
using Tessitura.Engraving.DisplayLists;
using Tessitura.Rendering;
using Tessitura.Smufl;
using Xunit;
using Path = System.IO.Path;

namespace Tessitura.Engraving.Tests;

public sealed class PdfExporterTests
{
    [Fact]
    public void ExportsEveryPageAtStaffSpaceScaleWithEmbeddedFonts()
    {
        string path = TemporaryPdfPath();
        try
        {
            SmuflMetadata metadata = LoadMetadata();
            using DisplayListRenderer renderer = new(MusicFontPath, TextFontPath);
            Page[] pages =
            [
                CreatePage(1, 68, 96, "Estudio en Do", metadata),
                CreatePage(2, 40, 60, "Ángela Núñez", metadata),
            ];

            PdfExporter.Export(path, pages, renderer, pointsPerStaffSpace: 8.75f);

            byte[] bytes = File.ReadAllBytes(path);
            string? captureDirectory = Environment.GetEnvironmentVariable("TESSITURA_CAPTURE_DIR");
            if (captureDirectory is not null)
            {
                Directory.CreateDirectory(captureDirectory);
                File.Copy(path, Path.Combine(captureDirectory, "f1.14-multipage-sample.pdf"), true);
                using SKSurface surface = SKSurface.Create(new SKImageInfo(68 * 18, 96 * 18));
                surface.Canvas.Clear(SKColors.White);
                renderer.Draw(pages[0], surface.Canvas, 18);
                ReferenceImageVerifier.SavePng(surface,
                    Path.Combine(captureDirectory, "f1.14-screen-page.png"));
            }

            Assert.True(bytes.Length > 1000);
            string source = Encoding.Latin1.GetString(bytes);
            Assert.StartsWith("%PDF-", source, StringComparison.Ordinal);
            Assert.Equal(2, Regex.Matches(source, @"/Type\s*/Page\b").Count);
            MatchCollection mediaBoxes = Regex.Matches(source,
                @"/MediaBox\s*\[\s*0\s+0\s+(?<width>[\d.]+)\s+(?<height>[\d.]+)\s*\]");
            Assert.Equal(2, mediaBoxes.Count);
            Assert.Equal(595d, double.Parse(mediaBoxes[0].Groups["width"].Value,
                CultureInfo.InvariantCulture), 1);
            Assert.Equal(840d, double.Parse(mediaBoxes[0].Groups["height"].Value,
                CultureInfo.InvariantCulture), 1);
            Assert.Equal(350d, double.Parse(mediaBoxes[1].Groups["width"].Value,
                CultureInfo.InvariantCulture), 1);
            Assert.Equal(525d, double.Parse(mediaBoxes[1].Groups["height"].Value,
                CultureInfo.InvariantCulture), 1);
            Assert.Contains("NotoSerif", source, StringComparison.Ordinal);
            Assert.Contains("Bravura", source, StringComparison.Ordinal);
            Assert.True(Regex.Matches(source, @"/Subtype\s*/Type3\b").Count >= 2 &&
                Regex.Matches(source, @"/CharProcs\b").Count >= 2,
                "SkiaSharp should embed the text and SMuFL glyph programs in the PDF.");
            Assert.DoesNotContain("/Subtype /Image", source, StringComparison.Ordinal);

        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsAnEmptyDocumentAndInvalidStaffSpaceScale()
    {
        string path = TemporaryPdfPath();
        try
        {
            using DisplayListRenderer renderer = new(MusicFontPath, TextFontPath);
            Assert.Throws<ArgumentException>(() =>
                PdfExporter.Export(path, Array.Empty<Page>(), renderer));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PdfExporter.Export(path, [new Page(1, 10, 10, [])], renderer,
                    pointsPerStaffSpace: 0));
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Page CreatePage(int number, double width, double height,
        string title, SmuflMetadata metadata)
    {
        ElementId id = new(Guid.Parse("55555555-5555-5555-5555-555555555555"));
        ImmutableArray<DrawingPrimitive> primitives =
        [
            new Text(id, new DisplayBox(6, 6, 24, 3), title,
                new DisplayPoint(6, 9), 3),
            new Glyph(id, default, metadata.GetGlyphCodepoint("noteheadBlack"),
                new DisplayPoint(20, 30), 4),
            new Line(id, new DisplayBox(20, 30, 0.12, 4),
                new DisplayPoint(20, 30), new DisplayPoint(20, 26), 0.12),
        ];
        return new Page(number, width, height, primitives);
    }

    private static SmuflMetadata LoadMetadata() => SmuflMetadata.Load(
        Path.Combine(Root, "assets", "fonts", "Bravura.json"),
        Path.Combine(Root, "assets", "fonts", "smufl_glyph_names.json"));

    private static string TemporaryPdfPath() =>
        Path.Combine(Path.GetTempPath(), $"tessitura-pdf-{Guid.NewGuid():N}.pdf");

    private static string MusicFontPath => Path.Combine(Root, "assets", "fonts", "Bravura.otf");
    private static string TextFontPath => Path.Combine(Root,
        "assets", "fonts", "NotoSerif[wdth,wght].ttf");

    private static string Root
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Tessitura.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Tessitura.sln was not found above the test directory.");
        }
    }
}
