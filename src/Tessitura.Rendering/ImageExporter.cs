using SkiaSharp;
using Tessitura.Engraving.DisplayLists;

namespace Tessitura.Rendering;

/// <summary>Exports engraving pages as PNG rasters and SVG vectors through the same drawing pass as the PDF.</summary>
public static class ImageExporter
{
    /// <summary>The PDF's points per inch; a page at 72 dpi has the PDF's pixel size.</summary>
    public const float PointsPerInch = 72f;

    /// <summary>Gets the pixel size of a page at a resolution.</summary>
    /// <param name="page">The page.</param>
    /// <param name="dpi">The resolution in dots per inch.</param>
    /// <param name="pointsPerStaffSpace">The physical staff-space size, as for the PDF.</param>
    /// <returns>The width and height in pixels.</returns>
    public static (int Width, int Height) PixelSize(Page page, int dpi, float pointsPerStaffSpace = PdfExporter.DefaultPointsPerStaffSpace)
    {
        ArgumentNullException.ThrowIfNull(page);
        ValidateDpi(dpi);
        float scale = pointsPerStaffSpace * dpi / PointsPerInch;
        return ((int)Math.Ceiling(page.Width * scale), (int)Math.Ceiling(page.Height * scale));
    }

    /// <summary>Renders a page to a PNG on a white background.</summary>
    /// <param name="outputPath">The destination file.</param>
    /// <param name="page">The page.</param>
    /// <param name="renderer">The display-list renderer with its fonts.</param>
    /// <param name="dpi">The resolution from 36 to 1200 dots per inch.</param>
    /// <param name="pointsPerStaffSpace">The physical staff-space size, as for the PDF.</param>
    public static void ExportPng(string outputPath, Page page, DisplayListRenderer renderer, int dpi = 300,
        float pointsPerStaffSpace = PdfExporter.DefaultPointsPerStaffSpace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(renderer);
        (int width, int height) = PixelSize(page, dpi, pointsPerStaffSpace);
        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new IOException("SkiaSharp could not create the image surface.");
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        renderer.Draw(page, canvas, pointsPerStaffSpace * dpi / PointsPerInch);
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        string temporary = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream stream = File.Create(temporary))
            {
                data.SaveTo(stream);
            }

            File.Move(temporary, outputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Renders a page to an SVG whose size in points equals the PDF page.</summary>
    /// <param name="outputPath">The destination file.</param>
    /// <param name="page">The page.</param>
    /// <param name="renderer">The display-list renderer with its fonts.</param>
    /// <param name="pointsPerStaffSpace">The physical staff-space size, as for the PDF.</param>
    public static void ExportSvg(string outputPath, Page page, DisplayListRenderer renderer,
        float pointsPerStaffSpace = PdfExporter.DefaultPointsPerStaffSpace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(renderer);
        SKRect bounds = SKRect.Create((float)(page.Width * pointsPerStaffSpace), (float)(page.Height * pointsPerStaffSpace));
        string temporary = $"{outputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream stream = File.Create(temporary))
            {
                using SKCanvas canvas = SKSvgCanvas.Create(bounds, stream);
                renderer.Draw(page, canvas, pointsPerStaffSpace);
            }

            File.Move(temporary, outputPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void ValidateDpi(int dpi)
    {
        if (dpi is < 36 or > 1200)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "The resolution must be between 36 and 1200 dpi.");
        }
    }
}
