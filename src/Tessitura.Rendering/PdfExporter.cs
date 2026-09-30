using SkiaSharp;
using Tessitura.Engraving.DisplayLists;

namespace Tessitura.Rendering;

/// <summary>Exports engraving pages as vector PDF documents.</summary>
public static class PdfExporter
{
    /// <summary>Gets the default conversion from staff spaces to PDF points.</summary>
    public const float DefaultPointsPerStaffSpace = 8.75f;

    /// <summary>Writes every page to a PDF, embedding the fonts used by the renderer.</summary>
    /// <param name="outputPath">The destination PDF path.</param>
    /// <param name="pages">The pages in document order.</param>
    /// <param name="renderer">The display-list renderer configured with music and text fonts.</param>
    /// <param name="pointsPerStaffSpace">The physical size of one staff space in PDF points.</param>
    public static void Export(string outputPath, IReadOnlyList<Page> pages,
        DisplayListRenderer renderer,
        float pointsPerStaffSpace = DefaultPointsPerStaffSpace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(renderer);
        if (!float.IsFinite(pointsPerStaffSpace) || pointsPerStaffSpace <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointsPerStaffSpace),
                "The staff-space size must be a finite positive number of points.");
        }

        if (pages.Count == 0)
        {
            throw new ArgumentException("A PDF must contain at least one page.", nameof(pages));
        }

        for (int index = 0; index < pages.Count; index++)
        {
            Page page = pages[index]
                ?? throw new ArgumentException("Pages cannot contain null entries.", nameof(pages));
            _ = PageDimension(page.Width, pointsPerStaffSpace, nameof(pages));
            _ = PageDimension(page.Height, pointsPerStaffSpace, nameof(pages));
        }

        using SKDocument document = SKDocument.CreatePdf(outputPath)
            ?? throw new IOException("SkiaSharp could not create the PDF document.");
        try
        {
            for (int index = 0; index < pages.Count; index++)
            {
                Page page = pages[index];
                float width = PageDimension(page.Width, pointsPerStaffSpace, nameof(pages));
                float height = PageDimension(page.Height, pointsPerStaffSpace, nameof(pages));
                SKCanvas canvas = document.BeginPage(width, height);
                renderer.Draw(page, canvas, pointsPerStaffSpace);
                document.EndPage();
            }

            document.Close();
        }
        catch
        {
            document.Abort();
            throw;
        }
    }

    private static float PageDimension(double staffSpaces, float scale, string parameterName)
    {
        double points = staffSpaces * scale;
        if (!double.IsFinite(staffSpaces) || staffSpaces <= 0 ||
            !double.IsFinite(points) || points > float.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName,
                "Page dimensions must be finite positive values that fit in PDF points.");
        }

        return (float)points;
    }
}
