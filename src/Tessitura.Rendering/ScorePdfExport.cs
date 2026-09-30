using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Engraving.DisplayLists;
using Tessitura.Smufl;

namespace Tessitura.Rendering;

/// <summary>Lays out a whole score and writes it as a PDF.</summary>
public static class ScorePdfExport
{
    /// <summary>Composes every page of the score and exports them.</summary>
    /// <param name="outputPath">The destination PDF path.</param>
    /// <param name="score">The score snapshot.</param>
    /// <param name="style">The engraving style.</param>
    /// <param name="metadata">The SMuFL font metadata.</param>
    /// <param name="musicFontPath">The music font file to embed.</param>
    /// <param name="textFontPath">The text font file to embed.</param>
    /// <returns>The number of pages written.</returns>
    public static int Export(string outputPath, Score score, Style style, SmuflMetadata metadata,
        string musicFontPath, string? textFontPath = null)
    {
        ArgumentNullException.ThrowIfNull(score);
        ScorePageComposer composer = new(metadata, style);
        ScoreLayoutResult layout = new IncrementalScoreLayouter(metadata)
            .Layout(score, style, composer.GetAvailableWidth(score));
        List<Page> pages = [];
        HashSet<int> seen = [];
        foreach (SystemLine system in layout.Systems)
        {
            ScorePageComposition composition = composer.Compose(score, layout, system.Range.StartIndex);
            if (seen.Add(composition.Page.Number))
            {
                pages.Add(composition.Page);
            }
        }

        pages.Sort((a, b) => a.Number.CompareTo(b.Number));
        using DisplayListRenderer renderer = new(musicFontPath, textFontPath);
        PdfExporter.Export(outputPath, pages, renderer,
            (float)(composer.GetStaffSpacePoints(score)));
        return pages.Count;
    }
}
