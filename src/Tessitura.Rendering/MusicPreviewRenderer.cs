using SkiaSharp;
using Tessitura.Smufl;

namespace Tessitura.Rendering;

/// <summary>Draws one provisional music example using Bravura metrics.</summary>
public sealed class MusicPreviewRenderer : IDisposable
{
    private readonly SKTypeface _typeface;
    private readonly SmuflMetadata _metadata;

    /// <summary>Loads the music font for the provisional page drawing.</summary>
    /// <param name="fontPath">The Bravura OpenType file.</param>
    /// <param name="metadata">The matching Bravura metrics.</param>
    public MusicPreviewRenderer(string fontPath, SmuflMetadata metadata)
    {
        _typeface = SKTypeface.FromFile(fontPath)
            ?? throw new FileNotFoundException("The music font could not be loaded.", fontPath);
        _metadata = metadata;
    }

    /// <summary>Calculates the exact upward stem attachment point in page coordinates.</summary>
    /// <param name="metadata">The matching SMuFL font metadata.</param>
    /// <param name="noteX">The notehead's horizontal glyph origin.</param>
    /// <param name="noteY">The notehead's vertical glyph origin.</param>
    /// <param name="staffSpace">The distance between adjacent staff lines.</param>
    /// <returns>The stem attachment point.</returns>
    public static SKPoint StemStart(SmuflMetadata metadata, float noteX, float noteY, float staffSpace)
    {
        SmuflPoint anchor = metadata.GetAnchor("noteheadBlack", "stemUpSE");
        return new SKPoint(
            noteX + (float)(anchor.X * staffSpace),
            noteY - (float)(anchor.Y * staffSpace));
    }

    /// <summary>Draws the five staff lines, treble clef, sharp, notehead, and stem.</summary>
    public void Draw(SKCanvas canvas)
    {
        const float staffSpace = 12;
        const float staffTop = 190;
        const float noteX = 350;
        const float noteY = staffTop + 3 * staffSpace;

        using SKPaint ink = new() { Color = SKColors.Black, IsAntialias = true };
        using SKPaint staffPaint = new()
        {
            Color = SKColors.Black,
            IsAntialias = true,
            StrokeWidth = (float)(_metadata.GetEngravingDefault("staffLineThickness") * staffSpace),
        };
        using SKPaint stemPaint = new()
        {
            Color = SKColors.Black,
            IsAntialias = true,
            StrokeWidth = (float)(_metadata.GetEngravingDefault("stemThickness") * staffSpace),
        };
        using SKFont font = new(_typeface, staffSpace * 4);

        for (int line = 0; line < 5; line++)
        {
            float y = staffTop + line * staffSpace;
            canvas.DrawLine(180, y, 570, y, staffPaint);
        }

        DrawGlyph(canvas, font, ink, "gClef", 190, staffTop + 3 * staffSpace);

        SmuflBoundingBox sharpBox = _metadata.GetBoundingBox("accidentalSharp");
        float accidentalGap = (float)(_metadata.GetEngravingDefault("staffLineThickness") * 2 * staffSpace);
        float sharpX = noteX - (float)(sharpBox.NorthEast.X * staffSpace) - accidentalGap;
        DrawGlyph(canvas, font, ink, "accidentalSharp", sharpX, noteY);
        DrawGlyph(canvas, font, ink, "noteheadBlack", noteX, noteY);

        SKPoint stemStart = StemStart(_metadata, noteX, noteY, staffSpace);
        // Behind Bars, "Stems": the standard stem length is 3.5 staff spaces.
        SKPoint stemEnd = new(stemStart.X, stemStart.Y - 3.5f * staffSpace);
        canvas.DrawLine(stemStart, stemEnd, stemPaint);
    }

    /// <inheritdoc />
    public void Dispose() => _typeface.Dispose();

    private void DrawGlyph(SKCanvas canvas, SKFont font, SKPaint paint, string glyphName, float x, float y)
    {
        string character = char.ConvertFromUtf32(_metadata.GetGlyphCodepoint(glyphName));
        canvas.DrawText(character, x, y, SKTextAlign.Left, font, paint);
    }
}
