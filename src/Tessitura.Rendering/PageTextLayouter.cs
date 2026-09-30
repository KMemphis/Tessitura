using System.Collections.Immutable;
using System.Globalization;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Tessitura.Engraving.DisplayLists;

namespace Tessitura.Rendering;

/// <summary>Describes a measure number and its horizontal and baseline position.</summary>
/// <param name="ElementId">The page or measure element that owns the label.</param>
/// <param name="Number">The one-based displayed measure number.</param>
/// <param name="CenterX">The horizontal center in staff-space coordinates.</param>
/// <param name="BaselineY">The text baseline in staff-space coordinates.</param>
public readonly record struct PageMeasureNumber(
    ElementId ElementId, int Number, double CenterX, double BaselineY);

/// <summary>Shapes page text and positions titles, composers, and measure numbers.</summary>
public sealed class PageTextLayouter : IDisposable
{
    private const float UnitsPerStaffSpace = 64;
    private readonly SKTypeface _typeface;
    private readonly SKShaper _shaper;

    /// <summary>Loads the OFL text face used for page text.</summary>
    /// <param name="textFontPath">The Noto Serif or other OFL TrueType/OpenType font.</param>
    public PageTextLayouter(string textFontPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textFontPath);
        _typeface = SKTypeface.FromFile(textFontPath)
            ?? throw new FileNotFoundException("The page-text font could not be loaded.", textFontPath);
        _shaper = new SKShaper(_typeface);
    }

    /// <summary>Creates positioned text primitives for the first-page header and measure numbers.</summary>
    /// <param name="title">The score title; blank text is omitted.</param>
    /// <param name="composer">The composer credit; blank text is omitted.</param>
    /// <param name="measureNumbers">The measure numbers in page order.</param>
    /// <param name="pageWidth">The page width in staff spaces.</param>
    /// <param name="titleBaselineY">The title baseline in staff spaces.</param>
    /// <param name="composerBaselineY">The composer baseline in staff spaces.</param>
    /// <param name="titleSize">The title font size in staff spaces.</param>
    /// <param name="composerSize">The composer font size in staff spaces.</param>
    /// <param name="measureNumberSize">The measure-number font size in staff spaces.</param>
    /// <returns>Text primitives with shaped widths and aligned origins.</returns>
    public ImmutableArray<Text> LayoutPageHeader(string title, string composer,
        ReadOnlySpan<PageMeasureNumber> measureNumbers, double pageWidth,
        double titleBaselineY, double composerBaselineY,
        double titleSize, double composerSize, double measureNumberSize)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(composer);
        ValidatePositive(pageWidth, nameof(pageWidth));
        ValidatePositive(titleSize, nameof(titleSize));
        ValidatePositive(composerSize, nameof(composerSize));
        ValidatePositive(measureNumberSize, nameof(measureNumberSize));
        ValidateCoordinate(titleBaselineY, nameof(titleBaselineY));
        ValidateCoordinate(composerBaselineY, nameof(composerBaselineY));

        ImmutableArray<Text>.Builder result =
            ImmutableArray.CreateBuilder<Text>(measureNumbers.Length + 2);
        if (!string.IsNullOrWhiteSpace(title))
        {
            result.Add(CreateCenteredText(title, new ElementId(Guid.Empty), pageWidth / 2,
                titleBaselineY, titleSize));
        }

        if (!string.IsNullOrWhiteSpace(composer))
        {
            result.Add(CreateCenteredText(composer, new ElementId(Guid.Empty), pageWidth / 2,
                composerBaselineY, composerSize));
        }

        foreach (PageMeasureNumber label in measureNumbers)
        {
            ValidateCoordinate(label.CenterX, nameof(measureNumbers));
            ValidateCoordinate(label.BaselineY, nameof(measureNumbers));
            if (label.Number <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(measureNumbers),
                    "Measure numbers must be positive.");
            }

            string value = label.Number.ToString(CultureInfo.InvariantCulture);
            result.Add(CreateCenteredText(value, label.ElementId, label.CenterX,
                label.BaselineY, measureNumberSize));
        }

        return result.ToImmutable();
    }

    private Text CreateCenteredText(string value, ElementId id,
        double centerX, double baselineY, double size)
    {
        using SKFont font = new(_typeface, (float)(size * UnitsPerStaffSpace));
        SKShaper.Result shaped = _shaper.Shape(value, font);
        SKFontMetrics metrics = font.Metrics;
        double width = shaped.Width / UnitsPerStaffSpace;
        double ascent = metrics.Ascent / UnitsPerStaffSpace;
        double descent = metrics.Descent / UnitsPerStaffSpace;
        double x = centerX - width / 2;
        double y = baselineY + ascent;
        DisplayBox bounds = new(x, y, width, descent - ascent);
        return new Text(id, bounds, value, new DisplayPoint(x, baselineY), size);
    }

    private static void ValidatePositive(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateCoordinate(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _shaper.Dispose();
        _typeface.Dispose();
    }
}
