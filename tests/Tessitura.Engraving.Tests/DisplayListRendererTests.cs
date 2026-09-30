using System.Collections.Immutable;
using SkiaSharp;
using Tessitura.Engraving.DisplayLists;
using Tessitura.Rendering;
using Tessitura.Smufl;
using Xunit;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;
using DisplayPath = Tessitura.Engraving.DisplayLists.Path;
using DisplayRect = Tessitura.Engraving.DisplayLists.Rect;
using DisplayText = Tessitura.Engraving.DisplayLists.Text;

namespace Tessitura.Engraving.Tests;

public sealed class DisplayListRendererTests
{
    [Fact]
    public void DrawsF010MusicIdenticallyAndRecordsAPicture()
    {
        SmuflMetadata metadata = SmuflMetadata.Load(
            System.IO.Path.Combine(Root, "assets", "fonts", "Bravura.json"),
            System.IO.Path.Combine(Root, "assets", "fonts", "smufl_glyph_names.json"));
        string fontPath = System.IO.Path.Combine(Root, "assets", "fonts", "Bravura.otf");
        Page page = CreateMusicPage(metadata);

        using SKSurface previous = SKSurface.Create(new SKImageInfo(800, 400));
        using SKSurface current = SKSurface.Create(new SKImageInfo(800, 400));
        using SKSurface fromPicture = SKSurface.Create(new SKImageInfo(800, 400));
        previous.Canvas.Clear(SKColors.White);
        current.Canvas.Clear(SKColors.White);
        fromPicture.Canvas.Clear(SKColors.White);

        using MusicPreviewRenderer preview = new(fontPath, metadata);
        using DisplayListRenderer renderer = new(fontPath);
        preview.Draw(previous.Canvas);
        renderer.Draw(page, current.Canvas, 12);
        using SKPicture picture = renderer.Record(page, 12);
        fromPicture.Canvas.DrawPicture(picture);

        using SKBitmap expected = SKBitmap.FromImage(previous.Snapshot());
        using SKBitmap actual = SKBitmap.FromImage(current.Snapshot());
        using SKBitmap recorded = SKBitmap.FromImage(fromPicture.Snapshot());
        AssertPixelsEqual(expected, actual);
        AssertPixelsEqual(actual, recorded);
    }

    [Fact]
    public void DrawsVectorPathTextAndRectangle()
    {
        ElementId id = new(Guid.Empty);
        Page page = new(1, 20, 10,
        [
            new DisplayPath(id, new DisplayBox(1, 1, 5, 5),
            [
                new PathCommand(PathVerb.MoveTo, new DisplayPoint(1, 1), default, default),
                new PathCommand(PathVerb.LineTo, new DisplayPoint(4, 1), default, default),
            ], 0.2),
            new DisplayRect(id, new DisplayBox(6, 1, 2, 2), true),
            new DisplayText(id, new DisplayBox(10, 1, 8, 3), "A", new DisplayPoint(10, 4), 3),
        ]);
        using DisplayListRenderer renderer = new(System.IO.Path.Combine(Root, "assets", "fonts", "Bravura.otf"));
        using SKSurface surface = SKSurface.Create(new SKImageInfo(200, 100));
        surface.Canvas.Clear(SKColors.White);

        renderer.Draw(page, surface.Canvas, 10);
        using SKBitmap bitmap = SKBitmap.FromImage(surface.Snapshot());

        Assert.NotEqual(SKColors.White, bitmap.GetPixel(20, 10));
        Assert.Equal(SKColors.Black, bitmap.GetPixel(70, 20));
        bool hasTextInk = false;
        for (int y = 10; y < 45 && !hasTextInk; y++)
        {
            for (int x = 100; x < 180; x++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.White)
                {
                    hasTextInk = true;
                    break;
                }
            }
        }

        Assert.True(hasTextInk);
    }

    private static Page CreateMusicPage(SmuflMetadata metadata)
    {
        const double staffSpace = 12;
        const double staffTop = 190;
        const double noteX = 350;
        const double noteY = staffTop + 3 * staffSpace;
        ElementId id = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        ImmutableArray<DrawingPrimitive>.Builder primitives = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        double staffThickness = metadata.GetEngravingDefault("staffLineThickness");
        double stemThickness = metadata.GetEngravingDefault("stemThickness");

        for (int line = 0; line < 5; line++)
        {
            double y = (staffTop + line * staffSpace) / staffSpace;
            primitives.Add(new DisplayLine(id, new DisplayBox(180 / staffSpace, y, 390 / staffSpace, staffThickness),
                new DisplayPoint(180 / staffSpace, y), new DisplayPoint(570 / staffSpace, y), staffThickness));
        }

        primitives.Add(new Glyph(id, default, metadata.GetGlyphCodepoint("gClef"),
            new DisplayPoint(190 / staffSpace, (staffTop + 3 * staffSpace) / staffSpace), 4));
        SmuflBoundingBox sharpBox = metadata.GetBoundingBox("accidentalSharp");
        double sharpX = noteX - sharpBox.NorthEast.X * staffSpace - staffThickness * 2 * staffSpace;
        primitives.Add(new Glyph(id, default, metadata.GetGlyphCodepoint("accidentalSharp"),
            new DisplayPoint(sharpX / staffSpace, noteY / staffSpace), 4));
        primitives.Add(new Glyph(id, default, metadata.GetGlyphCodepoint("noteheadBlack"),
            new DisplayPoint(noteX / staffSpace, noteY / staffSpace), 4));

        SKPoint stemStart = MusicPreviewRenderer.StemStart(metadata, (float)noteX, (float)noteY, (float)staffSpace);
        primitives.Add(new DisplayLine(id, default,
            new DisplayPoint(stemStart.X / staffSpace, stemStart.Y / staffSpace),
            new DisplayPoint(stemStart.X / staffSpace, (stemStart.Y - 3.5 * staffSpace) / staffSpace),
            stemThickness));
        return new Page(1, 800 / staffSpace, 400 / staffSpace, primitives.ToImmutable());
    }

    private static void AssertPixelsEqual(SKBitmap expected, SKBitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                Assert.Equal(expected.GetPixel(x, y), actual.GetPixel(x, y));
            }
        }
    }

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
