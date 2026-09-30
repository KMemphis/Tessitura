using System.Collections.Immutable;
using SkiaSharp;
using Tessitura.Engraving;
using Tessitura.Engraving.DisplayLists;
using Tessitura.Rendering;
using Tessitura.Smufl;
using Xunit;
using Path = System.IO.Path;

namespace Tessitura.Engraving.Tests;

public sealed class F116ReferenceCatalogTests
{
    [Fact]
    public void CatalogContainsTwentyDistinctExamplesForThePlannedCoverage()
    {
        SmuflMetadata metadata = LoadMetadata();
        ImmutableArray<ReferenceExample> examples = ReferenceCatalog.Create(
            metadata, Style.CreateDefault(metadata));

        string[] expectedIds =
        [
            "c-major-scale",
            "g-major-one-sharp",
            "d-major-two-sharps",
            "f-major-one-flat",
            "chromatic-sharps-and-flats",
            "whole-notes",
            "half-and-quarter-values",
            "eighth-note-groups",
            "sixteenth-note-groups",
            "rests-in-all-values",
            "dotted-values",
            "simple-meter-two-four",
            "simple-meter-three-four",
            "common-time-four-four",
            "six-eight-compound-meter",
            "nine-eight-compound-meter",
            "piano-grand-staff",
            "string-quartet",
            "ledger-line-range",
            "mixed-rhythm-and-accidentals",
        ];
        Assert.Equal(20, examples.Length);
        Assert.Equal(expectedIds, examples.Select(static example => example.Id));
        foreach (ReferenceExample example in examples)
        {
            Assert.True(example.Page.Width > 0);
            Assert.True(example.Page.Height > 0);
            Assert.NotEmpty(example.Page.Primitives);
        }
    }

    [Fact]
    public void EveryCatalogExampleRendersToAReviewablePngCandidate()
    {
        SmuflMetadata metadata = LoadMetadata();
        ImmutableArray<ReferenceExample> examples = ReferenceCatalog.Create(
            metadata, Style.CreateDefault(metadata));
        string? captureDirectory = Environment.GetEnvironmentVariable("TESSITURA_CAPTURE_DIR");
        string outputDirectory = captureDirectory is null
            ? Path.Combine(Path.GetTempPath(), $"tessitura-f1-16-{Guid.NewGuid():N}")
            : captureDirectory;
        Directory.CreateDirectory(outputDirectory);
        try
        {
            using DisplayListRenderer renderer = new(FontPath, TextFontPath);
            foreach (ReferenceExample example in examples)
            {
                int scale = 12;
                using SKSurface surface = SKSurface.Create(new SKImageInfo(
                    (int)(example.Page.Width * scale), (int)(example.Page.Height * scale)));
                surface.Canvas.Clear(SKColors.White);
                renderer.Draw(example.Page, surface.Canvas, scale);
                using SKImage image = surface.Snapshot();
                using SKBitmap bitmap = SKBitmap.FromImage(image);

                int inkPixels = 0;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        if (bitmap.GetPixel(x, y).Red < 240)
                        {
                            inkPixels++;
                        }
                    }
                }

                Assert.True(inkPixels > 1000, $"{example.Id} rendered without enough visible ink.");
                string candidate = Path.Combine(outputDirectory,
                    $"f1.16-{example.Id}-candidate.png");
                using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
                using (FileStream output = File.Create(candidate))
                {
                    png.SaveTo(output);
                }

                Assert.True(new FileInfo(candidate).Length > 1000, $"{example.Id} PNG is unexpectedly small.");
            }

            Assert.Equal(20, Directory.GetFiles(outputDirectory, "f1.16-*-candidate.png").Length);
            SaveContactSheet(examples, outputDirectory);

            List<string> mismatches = [];
            string platform = PlatformName();
            foreach (ReferenceExample example in examples)
            {
                string candidate = Path.Combine(outputDirectory,
                    $"f1.16-{example.Id}-candidate.png");
                string reference = Path.Combine(ReferenceDirectory,
                    $"f1.16-{platform}-{example.Id}.png");
                string difference = Path.Combine(outputDirectory,
                    $"f1.16-{example.Id}-difference.png");
                if (!File.Exists(reference))
                {
                    mismatches.Add($"{example.Id}: missing approved {platform} reference {reference}.");
                    continue;
                }

                ReferenceImageResult result = ReferenceImageVerifier.Compare(
                    reference, candidate, difference, tolerance: 16, maximumDifferentPixels: 500);
                if (result.Matches)
                {
                    File.Delete(difference);
                }
                else
                {
                    mismatches.Add(
                        $"{example.Id}: {result.DifferentPixels} pixels differ; see {candidate} and {difference}.");
                }
            }

            Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
        }
        finally
        {
            if (captureDirectory is null)
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static void SaveContactSheet(ImmutableArray<ReferenceExample> examples,
        string outputDirectory)
    {
        const int columns = 4;
        const int rows = 5;
        const int cellWidth = 460;
        const int cellHeight = 190;
        using SKSurface surface = SKSurface.Create(new SKImageInfo(
            columns * cellWidth, rows * cellHeight));
        surface.Canvas.Clear(SKColors.White);
        using SKTypeface typeface = SKTypeface.FromFile(TextFontPath)
            ?? throw new FileNotFoundException("The contact sheet text font could not be loaded.");
        using SKFont font = new(typeface, 12);
        using SKPaint ink = new() { Color = SKColors.Black, IsAntialias = true };

        for (int index = 0; index < examples.Length; index++)
        {
            ReferenceExample example = examples[index];
            int column = index % columns;
            int row = index / columns;
            float left = column * cellWidth + 6;
            float top = row * cellHeight;
            string candidatePath = Path.Combine(outputDirectory,
                $"f1.16-{example.Id}-candidate.png");
            using SKBitmap bitmap = SKBitmap.Decode(candidatePath)
                ?? throw new InvalidDataException($"Could not decode candidate: {candidatePath}");
            surface.Canvas.DrawText(example.Id, left, top + 16, font, ink);
            surface.Canvas.DrawBitmap(bitmap, new SKRect(left, top + 22,
                left + cellWidth - 12, top + cellHeight - 5));
        }

        using SKImage image = surface.Snapshot();
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream output = File.Create(Path.Combine(outputDirectory,
            "f1.16-contact-sheet.png"));
        png.SaveTo(output);
    }

    private static SmuflMetadata LoadMetadata() => SmuflMetadata.Load(
        Path.Combine(Root, "assets", "fonts", "Bravura.json"),
        Path.Combine(Root, "assets", "fonts", "smufl_glyph_names.json"));

    private static string FontPath => Path.Combine(Root, "assets", "fonts", "Bravura.otf");

    private static string TextFontPath => Path.Combine(Root, "assets", "fonts", "NotoSerif[wdth,wght].ttf");

    private static string PlatformName() => OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "macos" : "ubuntu";

    private static string ReferenceDirectory => Path.Combine(
        Root, "tests", "Tessitura.Engraving.Tests", "References");

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
