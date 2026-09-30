using SkiaSharp;
using Tessitura.Engraving.DisplayLists;
using Tessitura.Rendering;
using Tessitura.Smufl;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class ReferenceImageTests
{
    [Fact]
    public void F010PreviewMatchesApprovedPlatformReference()
    {
        string platform = OperatingSystem.IsWindows() ? "windows" :
            OperatingSystem.IsMacOS() ? "macos" : "ubuntu";
        string reference = System.IO.Path.Combine(Root, "docs", "capturas", $"f0.10-{platform}-render.png");
        string outputDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "reference-diffs");
        Directory.CreateDirectory(outputDirectory);
        string candidate = System.IO.Path.Combine(outputDirectory, $"f0.10-{platform}-candidate.png");
        string difference = System.IO.Path.Combine(outputDirectory, $"f0.10-{platform}-difference.png");
        SmuflMetadata metadata = LoadMetadata();
        using MusicPreviewRenderer music = new(FontPath, metadata);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(1000, 800));
        PagePreviewRenderer.Draw(surface.Canvas, 1000, 800, 1, 0, 0, music);
        ReferenceImageVerifier.SavePng(surface, candidate);

        ReferenceImageResult result = ReferenceImageVerifier.Compare(
            reference, candidate, difference, tolerance: 4, maximumDifferentPixels: 100);
        if (result.Matches)
        {
            File.Delete(candidate);
            File.Delete(difference);
        }

        Assert.True(result.Matches, $"{result.DifferentPixels} pixels differ; see {candidate} and {difference}");
    }

    [Fact]
    public void ChangedGlyphFailsThenPassesAfterExplicitApproval()
    {
        string approved = TemporaryPngPath();
        string candidate = TemporaryPngPath();
        string difference = TemporaryPngPath();
        ElementId id = new(Guid.Empty);
        try
        {
            using DisplayListRenderer renderer = new(FontPath);
            Page original = new(1, 10, 10,
                [new Glyph(id, new DisplayBox(1, 1, 3, 3), 0xE0A4, new DisplayPoint(2, 4), 4)]);
            Page changed = new(1, 10, 10,
                [new Glyph(id, new DisplayBox(1, 1, 3, 3), 0xE0A5, new DisplayPoint(2, 4), 4)]);
            SavePage(renderer, original, approved);
            SavePage(renderer, changed, candidate);

            ReferenceImageResult rejected = ReferenceImageVerifier.Compare(approved, candidate, difference, 0);
            Assert.False(rejected.Matches);
            Assert.True(rejected.DifferentPixels > 0);
            Assert.True(File.Exists(difference));

            ReferenceImageVerifier.Approve(candidate, approved);
            ReferenceImageResult accepted = ReferenceImageVerifier.Compare(approved, candidate, difference, 0);
            Assert.True(accepted.Matches);
            Assert.Equal(0, accepted.DifferentPixels);
        }
        finally
        {
            File.Delete(approved);
            File.Delete(candidate);
            File.Delete(difference);
        }
    }

    private static void SavePage(DisplayListRenderer renderer, Page page, string path)
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(100, 100));
        surface.Canvas.Clear(SKColors.White);
        renderer.Draw(page, surface.Canvas, 10);
        ReferenceImageVerifier.SavePng(surface, path);
    }

    private static string TemporaryPngPath() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tessitura-reference-{Guid.NewGuid():N}.png");

    private static SmuflMetadata LoadMetadata() => SmuflMetadata.Load(
        System.IO.Path.Combine(Root, "assets", "fonts", "Bravura.json"),
        System.IO.Path.Combine(Root, "assets", "fonts", "smufl_glyph_names.json"));

    private static string FontPath => System.IO.Path.Combine(Root, "assets", "fonts", "Bravura.otf");

    private static string Root
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(System.IO.Path.Combine(directory.FullName, "Tessitura.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Tessitura.sln was not found above the test directory.");
        }
    }
}
