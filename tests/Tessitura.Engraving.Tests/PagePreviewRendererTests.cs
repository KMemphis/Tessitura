using SkiaSharp;
using Tessitura.Rendering;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class PagePreviewRendererTests
{
    [Fact]
    public void DrawsWhitePageOnDarkWorkspace()
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(1000, 800));
        PagePreviewRenderer.Draw(surface.Canvas, 1000, 800, 1, 0, 0);
        using SKImage image = surface.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        Assert.Equal(SKColors.White, bitmap.GetPixel(100, 100));
        Assert.Equal(new SKColor(47, 52, 61), bitmap.GetPixel(10, 10));
    }

    [Fact]
    public void DrawsTheFullHeightOfAContinuousGalley()
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(1000, 1000));
        PagePreviewRenderer.Draw(surface.Canvas, 1000, 1000, 1, 0, 0, pageHeight: 940);
        using SKImage image = surface.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        Assert.Equal(SKColors.White, bitmap.GetPixel(100, 900));
        Assert.Equal(new SKColor(47, 52, 61), bitmap.GetPixel(100, 990));
    }
}
