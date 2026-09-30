using SkiaSharp;
using Tessitura.Rendering;
using Tessitura.Smufl;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class MusicPreviewRendererTests
{
    [Fact]
    public void StemStartsAtBravuraStemUpAnchor()
    {
        SmuflMetadata metadata = LoadMetadata();
        SmuflPoint anchor = metadata.GetAnchor("noteheadBlack", "stemUpSE");

        SKPoint start = MusicPreviewRenderer.StemStart(metadata, 350, 190, 12);

        Assert.Equal(350 + anchor.X * 12, start.X, 3);
        Assert.Equal(190 - anchor.Y * 12, start.Y, 3);
    }

    [Fact]
    public void DrawsStaffAndMusicUsingBravura()
    {
        using MusicPreviewRenderer music = new(FontPath, LoadMetadata());
        using SKSurface surface = SKSurface.Create(new SKImageInfo(1000, 800));
        PagePreviewRenderer.Draw(surface.Canvas, 1000, 800, 1, 0, 0, music);
        using SKImage image = surface.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        Assert.NotEqual(SKColors.White, bitmap.GetPixel(400, 226));

        string? captureDirectory = Environment.GetEnvironmentVariable("TESSITURA_CAPTURE_DIR");
        if (captureDirectory is not null)
        {
            Directory.CreateDirectory(captureDirectory);
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream output = File.Create(Path.Combine(captureDirectory, "music-preview.png"));
            png.SaveTo(output);
        }
    }

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
