using System.Collections.Immutable;
using SkiaSharp;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Engraving.DisplayLists;
using Tessitura.Rendering;
using Tessitura.Smufl;
using Xunit;
using Path = System.IO.Path;

namespace Tessitura.Engraving.Tests;

public sealed class PageTextLayouterTests
{
    [Fact]
    public void ShapesAndCentersTitleAndComposerAndPositionsMeasureNumbers()
    {
        using PageTextLayouter layouter = new(TextFontPath);
        ElementId pageId = new(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        PageMeasureNumber[] measures =
        [
            new(pageId, 1, 12, 24),
            new(pageId, 12, 68, 24),
        ];

        ImmutableArray<Text> text = layouter.LayoutPageHeader(
            "Fuga en Do", "Ángela Núñez", measures,
            pageWidth: 80, titleBaselineY: 8, composerBaselineY: 12,
            titleSize: 5, composerSize: 2.5, measureNumberSize: 1.5);

        Assert.Equal(4, text.Length);
        Text title = text.Single(item => item.Content == "Fuga en Do");
        Text composer = text.Single(item => item.Content == "Ángela Núñez");
        Assert.Equal(40, title.Origin.X + title.Bounds.Width / 2, 4);
        Assert.Equal(40, composer.Origin.X + composer.Bounds.Width / 2, 4);
        Assert.Equal(8, title.Origin.Y, 6);
        Assert.Equal(12, composer.Origin.Y, 6);
        Assert.True(title.Bounds.Width > composer.Bounds.Width);
        Assert.All(text, item => Assert.True(item.Bounds.Width > 0));
        Assert.Equal(12, text.Single(item => item.Content == "1").Origin.X +
            text.Single(item => item.Content == "1").Bounds.Width / 2, 6);
        Assert.Equal(68, text.Single(item => item.Content == "12").Origin.X +
            text.Single(item => item.Content == "12").Bounds.Width / 2, 6);
    }

    [Fact]
    public void OmitsBlankHeaderTextButKeepsMeasureNumbers()
    {
        using PageTextLayouter layouter = new(TextFontPath);
        PageMeasureNumber[] measures = [
            new(new ElementId(Guid.Empty), 4, 20, 8),
        ];

        ImmutableArray<Text> text = layouter.LayoutPageHeader(
            "  ", string.Empty, measures, 50, 6, 10, 5, 2.5, 1.5);

        Assert.Single(text);
        Assert.Equal("4", text[0].Content);
    }

    [Fact]
    public void FirstPageMatchesApprovedPlatformReference()
    {
        string platform = OperatingSystem.IsWindows() ? "windows" :
            OperatingSystem.IsMacOS() ? "macos" : "ubuntu";
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "reference-diffs");
        Directory.CreateDirectory(outputDirectory);
        string candidate = Path.Combine(outputDirectory, $"f1.13-{platform}-first-page-candidate.png");
        string difference = Path.Combine(outputDirectory, $"f1.13-{platform}-first-page-difference.png");
        string reference = Path.Combine(Root, "tests", "Tessitura.Engraving.Tests",
            "References", $"f1.13-{platform}-first-page.png");

        using PageTextLayouter textLayouter = new(TextFontPath);
        Page page = CreateFirstPage(textLayouter);
        using DisplayListRenderer renderer = new(FontPath, TextFontPath);
        Save(renderer, page, candidate);

        string? captureDirectory = Environment.GetEnvironmentVariable("TESSITURA_CAPTURE_DIR");
        if (captureDirectory is not null)
        {
            Directory.CreateDirectory(captureDirectory);
            File.Copy(candidate, Path.Combine(captureDirectory,
                "f1.13-first-page-candidate.png"), true);
        }

        ReferenceImageResult result = ReferenceImageVerifier.Compare(
            reference, candidate, difference, tolerance: 16, maximumDifferentPixels: 500);
        if (result.Matches)
        {
            File.Delete(candidate);
            File.Delete(difference);
        }

        Assert.True(result.Matches,
            $"{result.DifferentPixels} pixels differ in the first-page reference; see {candidate} and {difference}");
    }

    private static Page CreateFirstPage(PageTextLayouter textLayouter)
    {
        SmuflMetadata metadata = SmuflMetadata.Load(
            Path.Combine(Root, "assets", "fonts", "Bravura.json"),
            Path.Combine(Root, "assets", "fonts", "smufl_glyph_names.json"));
        EventId eventId = new(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        ElementId pageId = new(eventId.Value);
        ImmutableArray<Text> header = textLayouter.LayoutPageHeader(
            "Estudio en Do", "María Núñez", [],
            pageWidth: 68, titleBaselineY: 15, composerBaselineY: 20,
            titleSize: 5, composerSize: 2.5, measureNumberSize: 1.5);
        StaffElementPlacer placer = new(metadata, Style.CreateDefault(metadata));
        ImmutableArray<DrawingPrimitive>.Builder items = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        items.AddRange(header);
        const double staffLeft = 4;
        const double staffRight = 64;
        double[] positions = [10, 16, 22, 28, 40, 46, 52, 58];
        Step[][] scalePatterns =
        [
            [Step.C, Step.D, Step.E, Step.F, Step.G, Step.A, Step.B, Step.C],
            [Step.C, Step.E, Step.G, Step.E, Step.D, Step.F, Step.A, Step.F],
            [Step.G, Step.F, Step.E, Step.D, Step.C, Step.D, Step.E, Step.G],
            [Step.C, Step.E, Step.D, Step.F, Step.E, Step.G, Step.F, Step.C],
        ];
        for (int systemIndex = 0; systemIndex < scalePatterns.Length; systemIndex++)
        {
            double staffTop = 29 + systemIndex * 16;
            PageMeasureNumber[] measureNumbers =
            [
                new(pageId, 1 + systemIndex * 2, 6, staffTop - 3.5),
                new(pageId, 2 + systemIndex * 2, 36, staffTop - 3.5),
            ];
            items.AddRange(textLayouter.LayoutPageHeader(string.Empty, string.Empty,
                measureNumbers, 68, 0, 0, 5, 2.5, 1.5));
            items.AddRange(placer.PlaceStaffLines(eventId, staffLeft, staffRight, staffTop));

            SmuflBoundingBox clefBox = metadata.GetBoundingBox("gClef");
            items.Add(new Glyph(pageId,
                new DisplayBox(staffLeft + clefBox.SouthWest.X, staffTop + 3 - clefBox.NorthEast.Y,
                    clefBox.NorthEast.X - clefBox.SouthWest.X,
                    clefBox.NorthEast.Y - clefBox.SouthWest.Y),
                metadata.GetGlyphCodepoint("gClef"), new DisplayPoint(staffLeft, staffTop + 3), 4));
            items.Add(new Line(pageId, new DisplayBox(34, staffTop, 0.12, 4),
                new DisplayPoint(34, staffTop), new DisplayPoint(34, staffTop + 4), 0.12));
            items.Add(new Line(pageId, new DisplayBox(staffRight, staffTop, 0.12, 4),
                new DisplayPoint(staffRight, staffTop), new DisplayPoint(staffRight, staffTop + 4), 0.12));

            for (int index = 0; index < scalePatterns[systemIndex].Length; index++)
            {
                items.AddRange(placer.PlaceNote(eventId,
                    new Pitch(scalePatterns[systemIndex][index], 0, 4),
                    new Duration(NoteValue.Quarter, 0), AccidentalMark.None,
                    positions[index], staffTop));
            }
        }

        return new Page(1, 68, 96, items.ToImmutable());
    }

    private static void Save(DisplayListRenderer renderer, Page page, string path)
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(
            (int)(page.Width * 18), (int)(page.Height * 18)));
        surface.Canvas.Clear(SKColors.White);
        renderer.Draw(page, surface.Canvas, 18);
        ReferenceImageVerifier.SavePng(surface, path);
    }

    private static string TextFontPath => Path.Combine(Root,
        "assets", "fonts", "NotoSerif[wdth,wght].ttf");
    private static string FontPath => Path.Combine(Root, "assets", "fonts", "Bravura.otf");

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
