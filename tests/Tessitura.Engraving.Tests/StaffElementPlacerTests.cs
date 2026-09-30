using System.Collections.Immutable;
using SkiaSharp;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Engraving.DisplayLists;
using Tessitura.Rendering;
using Tessitura.Smufl;
using Xunit;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;
using Path = System.IO.Path;

namespace Tessitura.Engraving.Tests;

public sealed class StaffElementPlacerTests
{
    [Fact]
    public void StemUsesSmuflAnchorAndLedgerLinesSpanBothSidesOfStaff()
    {
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        StaffElementPlacer placer = new(metadata, style);

        ImmutableArray<DrawingPrimitive> low = placer.PlaceNote(Id,
            new Pitch(Step.C, 0, 3), new Duration(NoteValue.Quarter, 0),
            AccidentalMark.None, 5, 10);
        ImmutableArray<DrawingPrimitive> high = placer.PlaceNote(Id,
            new Pitch(Step.C, 0, 7), new Duration(NoteValue.Quarter, 0),
            AccidentalMark.None, 5, 10);

        Assert.True(low.OfType<DisplayLine>().Count() >= 4);
        Assert.True(high.OfType<DisplayLine>().Count() >= 5);
        SmuflPoint anchor = metadata.GetAnchor("noteheadBlack", "stemUpSE");
        DisplayLine lowStem = low.OfType<DisplayLine>().Last();
        Glyph lowHead = low.OfType<Glyph>().Single();
        Assert.Equal(lowHead.Origin.X + anchor.X, lowStem.Start.X, 6);
        Assert.Equal(lowHead.Origin.Y - anchor.Y, lowStem.Start.Y, 6);
        Assert.Equal(12, lowStem.End.Y, 6);
        DisplayLine highStem = high.OfType<DisplayLine>().Last();
        Assert.Equal(12, highStem.End.Y, 6);

        ImmutableArray<DrawingPrimitive> middle = placer.PlaceNote(Id,
            new Pitch(Step.E, 0, 4), new Duration(NoteValue.Quarter, 0),
            AccidentalMark.None, 5, 10);
        DisplayLine middleStem = middle.OfType<DisplayLine>().Last();
        Assert.Equal(style.StemLength, middleStem.Start.Y - middleStem.End.Y, 6);
    }

    [Fact]
    public void AccidentalAndTwoDotsHaveClearanceFromNotehead()
    {
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        StaffElementPlacer placer = new(metadata, style);

        ImmutableArray<DrawingPrimitive> placed = placer.PlaceNote(Id,
            new Pitch(Step.F, 1, 4), new Duration(NoteValue.Quarter, 2),
            AccidentalMark.Sharp, 6, 4);

        Glyph head = placed.OfType<Glyph>().Single(glyph => glyph.Codepoint ==
            metadata.GetGlyphCodepoint("noteheadBlack"));
        Glyph sharp = placed.OfType<Glyph>().Single(glyph => glyph.Codepoint ==
            metadata.GetGlyphCodepoint("accidentalSharp"));
        Glyph[] dots = placed.OfType<Glyph>().Where(glyph => glyph.Codepoint ==
            metadata.GetGlyphCodepoint("augmentationDot")).ToArray();
        Assert.True(sharp.Bounds.X + sharp.Bounds.Width + style.MinimumAccidentalGap
            <= head.Bounds.X);
        Assert.Equal(2, dots.Length);
        Assert.True(dots[0].Bounds.X >= head.Bounds.X + head.Bounds.Width);
        Assert.True(dots[1].Bounds.X > dots[0].Bounds.X);
    }

    [Theory]
    [InlineData(NoteValue.Whole, "restWhole")]
    [InlineData(NoteValue.Half, "restHalf")]
    [InlineData(NoteValue.Quarter, "restQuarter")]
    [InlineData(NoteValue.Eighth, "rest8th")]
    [InlineData(NoteValue.Sixteenth, "rest16th")]
    [InlineData(NoteValue.ThirtySecond, "rest32nd")]
    [InlineData(NoteValue.SixtyFourth, "rest64th")]
    [InlineData(NoteValue.HundredTwentyEighth, "rest128th")]
    public void EveryRestValueUsesItsSmuflGlyph(NoteValue value, string glyphName)
    {
        SmuflMetadata metadata = LoadMetadata();
        StaffElementPlacer placer = new(metadata, Style.CreateDefault(metadata));

        ImmutableArray<DrawingPrimitive> placed = placer.PlaceRest(Id,
            new Duration(value, 0), 5, 4);

        Assert.Equal(metadata.GetGlyphCodepoint(glyphName),
            Assert.Single(placed.OfType<Glyph>()).Codepoint);
    }

    [Fact]
    public void FourOctaveScaleAndAllRestsMatchApprovedReferences()
    {
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        StaffElementPlacer placer = new(metadata, style);
        Page scale = ScalePage(placer);
        Page rests = RestsPage(placer);
        Assert.Equal(29, scale.Primitives.OfType<Glyph>().Count());
        Assert.Equal(8, rests.Primitives.OfType<Glyph>().Count());

        using DisplayListRenderer renderer = new(FontPath);
        VerifyReference(renderer, scale, "scale", 250);
        VerifyReference(renderer, rests, "rests", 150);
    }

    private static void VerifyReference(DisplayListRenderer renderer, Page page,
        string name, int maximumDifferentPixels)
    {
        string platform = OperatingSystem.IsWindows() ? "windows" :
            OperatingSystem.IsMacOS() ? "macos" : "ubuntu";
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "reference-diffs");
        Directory.CreateDirectory(outputDirectory);
        string candidate = Path.Combine(outputDirectory, $"f1.9-{platform}-{name}-candidate.png");
        string difference = Path.Combine(outputDirectory, $"f1.9-{platform}-{name}-difference.png");
        string reference = Path.Combine(Root, "tests", "Tessitura.Engraving.Tests",
            "References", $"f1.9-{platform}-{name}.png");
        Save(renderer, page, candidate);

        string? captureDirectory = Environment.GetEnvironmentVariable("TESSITURA_CAPTURE_DIR");
        if (captureDirectory is not null)
        {
            Directory.CreateDirectory(captureDirectory);
            File.Copy(candidate, Path.Combine(captureDirectory, $"f1.9-{name}-candidate.png"), true);
        }

        ReferenceImageResult result = ReferenceImageVerifier.Compare(
            reference, candidate, difference, tolerance: 4, maximumDifferentPixels);
        if (result.Matches)
        {
            File.Delete(candidate);
            File.Delete(difference);
        }

        Assert.True(result.Matches,
            $"{result.DifferentPixels} pixels differ in {name}; see {candidate} and {difference}");
    }

    private static Page ScalePage(StaffElementPlacer placer)
    {
        ImmutableArray<DrawingPrimitive>.Builder items = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        items.AddRange(placer.PlaceStaffLines(Id, 7, 92, 14));
        for (int index = 0; index < 29; index++)
        {
            int octave = 3 + index / 7;
            Step step = (Step)(index % 7);
            items.AddRange(placer.PlaceNote(Id, new Pitch(step, 0, octave),
                new Duration(NoteValue.Quarter, 0), AccidentalMark.None,
                10 + index * 2.8, 14));
        }

        return new Page(1, 100, 36, items.ToImmutable());
    }

    private static Page RestsPage(StaffElementPlacer placer)
    {
        ImmutableArray<DrawingPrimitive>.Builder items = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        items.AddRange(placer.PlaceStaffLines(Id, 3, 57, 5));
        NoteValue[] values =
        [
            NoteValue.Whole, NoteValue.Half, NoteValue.Quarter, NoteValue.Eighth,
            NoteValue.Sixteenth, NoteValue.ThirtySecond, NoteValue.SixtyFourth,
            NoteValue.HundredTwentyEighth,
        ];
        for (int index = 0; index < values.Length; index++)
        {
            items.AddRange(placer.PlaceRest(Id, new Duration(values[index], 0),
                6 + index * 7, 5));
        }

        return new Page(1, 62, 16, items.ToImmutable());
    }

    private static void Save(DisplayListRenderer renderer, Page page, string path)
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(
            (int)(page.Width * 12), (int)(page.Height * 12)));
        surface.Canvas.Clear(SKColors.White);
        renderer.Draw(page, surface.Canvas, 12);
        using SKImage image = surface.Snapshot();
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream output = File.Create(path);
        png.SaveTo(output);
    }

    private static EventId Id => new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    private static SmuflMetadata LoadMetadata() => SmuflMetadata.Load(
        Path.Combine(Root, "assets", "fonts", "Bravura.json"),
        Path.Combine(Root, "assets", "fonts", "smufl_glyph_names.json"));

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
