using SkiaSharp;

namespace Tessitura.Rendering;

/// <summary>Draws the provisional A4 page preview before engraving is available.</summary>
public static class PagePreviewRenderer
{
    /// <summary>Draws the workspace, A4 page, and page shadow.</summary>
    public static void Draw(
        SKCanvas canvas,
        double width,
        double height,
        double zoom,
        double panX,
        double panY,
        MusicPreviewRenderer? music = null)
    {
        using SKPaint background = new() { Color = new SKColor(47, 52, 61) };
        using SKPaint shadow = new() { Color = new SKColor(0, 0, 0, 75), IsAntialias = true };
        using SKPaint page = new() { Color = SKColors.White, IsAntialias = true };

        canvas.DrawRect(0, 0, (float)width, (float)height, background);
        canvas.Save();
        canvas.Translate((float)panX, (float)panY);
        canvas.Scale((float)zoom);
        canvas.DrawRect(87, 47, 595, 842, shadow);
        canvas.DrawRect(80, 40, 595, 842, page);
        music?.Draw(canvas);
        canvas.Restore();
    }
}
