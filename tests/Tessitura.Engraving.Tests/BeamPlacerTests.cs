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

public sealed class BeamPlacerTests
{
    [Theory]
    [InlineData(StemDirection.Up, 7.0, 3.0)]
    [InlineData(StemDirection.Down, 3.0, 7.0)]
    public void PrimaryBeamLimitsRiseAndKeepsMinimumStemLength(
        StemDirection direction, double firstY, double lastY)
    {
        Style style = DefaultStyle();
        BeamPlacer placer = new(style);
        BeamNote[] notes =
        [
            new(Id, new DisplayPoint(2, firstY), 1),
            new(Id, new DisplayPoint(8, (firstY + lastY) / 2), 1),
            new(Id, new DisplayPoint(14, lastY), 1),
        ];

        ImmutableArray<DrawingPrimitive> placed = placer.Place(notes, direction);
        DisplayLine[] beams = placed.OfType<DisplayLine>()
            .Where(line => line.StrokeWidth == style.BeamThickness).ToArray();
        DisplayLine[] stems = placed.OfType<DisplayLine>()
            .Where(line => line.StrokeWidth == style.StemThickness).ToArray();
        DisplayLine primary = Assert.Single(beams);
        Assert.InRange(Math.Abs(primary.End.Y - primary.Start.Y), 0, 0.5);
        Assert.Equal(style.BeamThickness, primary.StrokeWidth);
        Assert.Equal(notes.Length, stems.Length);
        foreach (DisplayLine stem in stems)
        {
            Assert.True(Math.Abs(stem.End.Y - stem.Start.Y) >= style.StemLength);
        }
    }

    [Fact]
    public void SecondaryAndTertiaryBeamsUseSmuflSpacing()
    {
        Style style = DefaultStyle();
        BeamPlacer placer = new(style);
        BeamNote[] notes =
        [
            new(Id, new DisplayPoint(2, 7), 2),
            new(Id, new DisplayPoint(6, 6), 3),
            new(Id, new DisplayPoint(10, 5), 2),
            new(Id, new DisplayPoint(14, 4), 3),
        ];

        DisplayLine[] beams = placer.Place(notes, StemDirection.Up)
            .OfType<DisplayLine>()
            .Where(line => line.StrokeWidth == style.BeamThickness).ToArray();
        Assert.Equal(4, beams.Length); // primary, secondary, and two tertiary partials
        Assert.Equal(style.BeamThickness + style.BeamSpacing,
            beams[1].Start.Y - beams[0].Start.Y, 6);
        Assert.True(beams[2].End.X - beams[2].Start.X <
            beams[0].End.X - beams[0].Start.X);
    }

    [Fact]
    public void IsolatedSixteenthGetsPartialBeamTowardsItsNeighbour()
    {
        Style style = DefaultStyle();
        BeamPlacer placer = new(style);
        BeamNote[] notes =
        [
            new(Id, new DisplayPoint(2, 7), 1),
            new(Id, new DisplayPoint(6, 5), 2),
            new(Id, new DisplayPoint(10, 6), 1),
        ];

        DisplayLine[] beams = placer.Place(notes, StemDirection.Up)
            .OfType<DisplayLine>()
            .Where(line => line.StrokeWidth == style.BeamThickness).ToArray();
        Assert.Equal(2, beams.Length);
        Assert.Equal(6, beams[1].Start.X, 6);
        Assert.InRange(beams[1].End.X, 6.5, 8);
    }

    [Fact]
    public void GeneratesAscendingDescendingAndMixedBeamCandidates()
    {
        string? captureDirectory = Environment.GetEnvironmentVariable("TESSITURA_CAPTURE_DIR");
        if (captureDirectory is null)
        {
            return;
        }

        Directory.CreateDirectory(captureDirectory);
        SmuflMetadata metadata = Metadata();
        BeamPlacer placer = new(Style.CreateDefault(metadata));
        using DisplayListRenderer renderer = new(FontPath);
        Save(renderer, GroupPage(placer, metadata, StemDirection.Up,
            [8, 7, 6, 5], [1, 2, 2, 1]),
            Path.Combine(captureDirectory, "f1.10-ascending-candidate.png"));
        Save(renderer, GroupPage(placer, metadata, StemDirection.Down,
            [4, 5, 6, 7], [2, 2, 2, 2]),
            Path.Combine(captureDirectory, "f1.10-descending-candidate.png"));
        Save(renderer, GroupPage(placer, metadata, StemDirection.Up,
            [7, 5, 7, 6], [1, 2, 1, 3]),
            Path.Combine(captureDirectory, "f1.10-mixed-candidate.png"));
    }

    private static Page GroupPage(BeamPlacer placer, SmuflMetadata metadata,
        StemDirection direction, double[] noteY, int[] levels)
    {
        ImmutableArray<DrawingPrimitive>.Builder items = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        StaffElementPlacer staff = new(metadata, DefaultStyle());
        items.AddRange(staff.PlaceStaffLines(Id, 2, 32, 4));
        BeamNote[] notes = new BeamNote[noteY.Length];
        SmuflPoint anchor = metadata.GetAnchor("noteheadBlack",
            direction == StemDirection.Up ? "stemUpSE" : "stemDownNW");
        SmuflBoundingBox box = metadata.GetBoundingBox("noteheadBlack");
        for (int index = 0; index < noteY.Length; index++)
        {
            double x = 5 + index * 8;
            double y = noteY[index];
            items.Add(new Glyph(new ElementId(Id.Value),
                new DisplayBox(x + box.SouthWest.X, y - box.NorthEast.Y,
                    box.NorthEast.X - box.SouthWest.X,
                    box.NorthEast.Y - box.SouthWest.Y),
                metadata.GetGlyphCodepoint("noteheadBlack"), new DisplayPoint(x, y), 4));
            notes[index] = new BeamNote(Id,
                new DisplayPoint(x + anchor.X, y - anchor.Y), levels[index]);
        }

        items.AddRange(placer.Place(notes, direction));
        return new Page(1, 34, 14, items.ToImmutable());
    }

    private static void Save(DisplayListRenderer renderer, Page page, string path)
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(
            (int)(page.Width * 20), (int)(page.Height * 20)));
        surface.Canvas.Clear(SKColors.White);
        renderer.Draw(page, surface.Canvas, 20);
        using SKImage image = surface.Snapshot();
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream output = File.Create(path);
        png.SaveTo(output);
    }

    private static Style DefaultStyle() => Style.CreateDefault(Metadata());
    private static SmuflMetadata Metadata() => SmuflMetadata.Load(
        Path.Combine(Root, "assets", "fonts", "Bravura.json"),
        Path.Combine(Root, "assets", "fonts", "smufl_glyph_names.json"));
    private static string FontPath => Path.Combine(Root, "assets", "fonts", "Bravura.otf");
    private static EventId Id => new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

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
