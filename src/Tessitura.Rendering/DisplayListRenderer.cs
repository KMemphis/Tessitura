using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Tessitura.Engraving.DisplayLists;
using DisplayPath = Tessitura.Engraving.DisplayLists.Path;
using DisplayRect = Tessitura.Engraving.DisplayLists.Rect;

namespace Tessitura.Rendering;

/// <summary>Draws immutable engraving pages and records them as Skia pictures.</summary>
public sealed class DisplayListRenderer : IDisposable
{
    private readonly SKTypeface _musicTypeface;
    private readonly SKTypeface _textTypeface;
    private readonly SKShaper _textShaper;
    private readonly bool _ownsTextTypeface;
    private readonly Dictionary<int, string> _glyphTextByCodepoint = [];

    /// <summary>Loads the SMuFL font used by glyph primitives.</summary>
    /// <param name="musicFontPath">The SMuFL OpenType font file.</param>
    /// <param name="textFontPath">The optional OFL text font file.</param>
    public DisplayListRenderer(string musicFontPath, string? textFontPath = null)
    {
        _musicTypeface = SKTypeface.FromFile(musicFontPath)
            ?? throw new FileNotFoundException("The music font could not be loaded.", musicFontPath);
        if (textFontPath is null)
        {
            _textTypeface = SKTypeface.Default;
            _ownsTextTypeface = false;
        }
        else
        {
            _textTypeface = SKTypeface.FromFile(textFontPath)
                ?? throw new FileNotFoundException("The page-text font could not be loaded.", textFontPath);
            _ownsTextTypeface = true;
        }

        _textShaper = new SKShaper(_textTypeface);
    }

    /// <summary>Draws one page on an existing canvas at a chosen staff-space scale.</summary>
    /// <param name="page">The display list to draw.</param>
    /// <param name="canvas">The destination canvas.</param>
    /// <param name="staffSpace">The number of canvas units per staff space.</param>
    public void Draw(Page page, SKCanvas canvas, float staffSpace)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(staffSpace);

        using SKPaint ink = new() { Color = SKColors.Black, IsAntialias = true };
        using SKPaint stroke = new() { Color = SKColors.Black, IsAntialias = true };
        using SKFont musicFont = new(_musicTypeface, staffSpace * 4);
        using SKFont textFont = new(_textTypeface, staffSpace);

        foreach (DrawingPrimitive primitive in page.Primitives)
        {
            switch (primitive)
            {
                case Glyph glyph:
                    musicFont.Size = (float)(glyph.Size * staffSpace);
                    string character = GetGlyphText(glyph.Codepoint);
                    canvas.DrawText(character,
                        (float)(glyph.Origin.X * staffSpace),
                        (float)(glyph.Origin.Y * staffSpace),
                        SKTextAlign.Left, musicFont, ink);
                    break;

                case Line line:
                    stroke.StrokeWidth = (float)(line.StrokeWidth * staffSpace);
                    canvas.DrawLine(ToSkPoint(line.Start, staffSpace),
                        ToSkPoint(line.End, staffSpace), stroke);
                    break;

                case DisplayPath path:
                    // A stroke width of zero means a filled shape such as a slur.
                    stroke.StrokeWidth = (float)(path.StrokeWidth * staffSpace);
                    stroke.Style = path.StrokeWidth == 0 ? SKPaintStyle.Fill : SKPaintStyle.Stroke;
                    using (SKPath skPath = BuildPath(path, staffSpace))
                    {
                        canvas.DrawPath(skPath, stroke);
                    }

                    stroke.Style = SKPaintStyle.Fill;
                    break;

                case Text text:
                    textFont.Size = (float)(text.Size * staffSpace);
                    canvas.DrawShapedText(_textShaper, text.Content,
                        new SKPoint((float)(text.Origin.X * staffSpace),
                            (float)(text.Origin.Y * staffSpace)),
                        SKTextAlign.Left, textFont, ink);
                    break;

                case DisplayRect rect:
                    stroke.Style = rect.Filled ? SKPaintStyle.Fill : SKPaintStyle.Stroke;
                    canvas.DrawRect(
                        (float)(rect.Bounds.X * staffSpace),
                        (float)(rect.Bounds.Y * staffSpace),
                        (float)(rect.Bounds.Width * staffSpace),
                        (float)(rect.Bounds.Height * staffSpace), stroke);
                    stroke.Style = SKPaintStyle.Fill;
                    break;
            }
        }
    }

    /// <summary>Records one display list for repeated rendering at different zoom levels.</summary>
    /// <param name="page">The display list to record.</param>
    /// <param name="staffSpace">The number of picture units per staff space.</param>
    /// <returns>The recorded Skia picture.</returns>
    public SKPicture Record(Page page, float staffSpace)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(staffSpace);

        using SKPictureRecorder recorder = new();
        SKCanvas canvas = recorder.BeginRecording(new SKRect(0, 0,
            (float)(page.Width * staffSpace), (float)(page.Height * staffSpace)));
        Draw(page, canvas, staffSpace);
        return recorder.EndRecording()
            ?? throw new InvalidOperationException("The display list could not be recorded.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _textShaper.Dispose();
        if (_ownsTextTypeface)
        {
            _textTypeface.Dispose();
        }

        _musicTypeface.Dispose();
    }

    private static SKPoint ToSkPoint(DisplayPoint point, float scale) =>
        new((float)(point.X * scale), (float)(point.Y * scale));

    private static SKPath BuildPath(DisplayPath path, float scale)
    {
        SKPath result = new();
        foreach (PathCommand command in path.Commands)
        {
            switch (command.Verb)
            {
                case PathVerb.MoveTo:
                    result.MoveTo(ToSkPoint(command.Point1, scale));
                    break;
                case PathVerb.LineTo:
                    result.LineTo(ToSkPoint(command.Point1, scale));
                    break;
                case PathVerb.CubicTo:
                    result.CubicTo(ToSkPoint(command.Point1, scale),
                        ToSkPoint(command.Point2, scale), ToSkPoint(command.Point3, scale));
                    break;
                case PathVerb.Close:
                    result.Close();
                    break;
            }
        }

        return result;
    }

    private string GetGlyphText(int codepoint)
    {
        if (_glyphTextByCodepoint.TryGetValue(codepoint, out string? text))
        {
            return text;
        }

        text = char.ConvertFromUtf32(codepoint);
        _glyphTextByCodepoint.Add(codepoint, text);
        return text;
    }
}
