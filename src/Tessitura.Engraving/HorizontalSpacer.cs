using System.Collections.Immutable;
using System.Globalization;
using Tessitura.Core;
using Tessitura.Smufl;

namespace Tessitura.Engraving;

/// <summary>Describes one rhythmic column's duration and horizontal ink extents.</summary>
/// <param name="Duration">The shortest exact duration represented by the column.</param>
/// <param name="LeftExtent">The distance from its anchor to leftmost ink.</param>
/// <param name="RightExtent">The distance from its anchor to rightmost ink.</param>
public readonly record struct SpacingColumn(Fraction Duration, double LeftExtent, double RightExtent);

/// <summary>Locates one rhythmic column in staff-space coordinates.</summary>
/// <param name="Index">The source column index.</param>
/// <param name="X">The horizontal anchor position.</param>
public readonly record struct PositionedColumn(int Index, double X);

/// <summary>Names a horizontal part of the system header.</summary>
public enum HeaderPart
{
    /// <summary>The initial clef.</summary>
    Clef,
    /// <summary>One key-signature accidental.</summary>
    KeySignature,
    /// <summary>One numerator digit.</summary>
    MeterNumerator,
    /// <summary>One denominator digit.</summary>
    MeterDenominator,
}

/// <summary>Locates one SMuFL header glyph horizontally.</summary>
/// <param name="Part">The header region.</param>
/// <param name="GlyphName">The SMuFL glyph name.</param>
/// <param name="Codepoint">The SMuFL codepoint.</param>
/// <param name="X">The horizontal position in staff spaces.</param>
/// <param name="Width">The glyph width in staff spaces.</param>
public readonly record struct HeaderSymbol(HeaderPart Part, string GlyphName,
    int Codepoint, double X, double Width);

/// <summary>Contains the horizontally positioned system header.</summary>
/// <param name="Symbols">The clef, key signature, and meter symbols.</param>
/// <param name="MusicStartX">The first available music position after the header.</param>
public sealed record SystemHeaderLayout(ImmutableArray<HeaderSymbol> Symbols, double MusicStartX);

/// <summary>Computes ideal rhythm widths and collision-safe horizontal positions.</summary>
public sealed class HorizontalSpacer
{
    /// <summary>Gets the logarithmic ideal width for one duration.</summary>
    /// <param name="duration">The exact musical duration of the column.</param>
    /// <param name="shortest">The shortest exact duration in the system.</param>
    /// <param name="minimumWidth">The minimum geometric width in staff spaces.</param>
    /// <returns>The ideal geometric width in staff spaces.</returns>
    public double IdealWidth(Fraction duration, Fraction shortest, double minimumWidth)
    {
        if (duration <= Fraction.Zero || shortest <= Fraction.Zero ||
            duration < shortest || minimumWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        // Behind Bars, Ground Rules > Rhythmic spacing; Tessitura project definition:
        // a logarithmic optical width, with alpha 0.6, keeps a half near 1.6 quarters.
        // The exact musical ratio stays a Fraction; only the geometric log is double.
        Fraction ratio = duration / shortest;
        return minimumWidth * (1 + 0.6 * Math.Log2((double)ratio.Num / ratio.Den));
    }

    /// <summary>Positions columns using ideal rhythm widths and minimum ink clearance.</summary>
    /// <param name="columns">The columns in musical order.</param>
    /// <param name="style">The engraving style.</param>
    /// <param name="startX">The horizontal position after the system header.</param>
    /// <returns>One anchor position per column.</returns>
    public ImmutableArray<PositionedColumn> Layout(
        ReadOnlySpan<SpacingColumn> columns, Style style, double startX)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (columns.IsEmpty)
        {
            return ImmutableArray<PositionedColumn>.Empty;
        }

        Fraction shortest = columns[0].Duration;
        foreach (SpacingColumn column in columns)
        {
            if (column.Duration <= Fraction.Zero || column.LeftExtent < 0 || column.RightExtent < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columns));
            }

            if (column.Duration < shortest)
            {
                shortest = column.Duration;
            }
        }

        ImmutableArray<PositionedColumn>.Builder result =
            ImmutableArray.CreateBuilder<PositionedColumn>(columns.Length);
        double x = startX + columns[0].LeftExtent;
        result.Add(new PositionedColumn(0, x));
        for (int index = 1; index < columns.Length; index++)
        {
            SpacingColumn previous = columns[index - 1];
            SpacingColumn current = columns[index];
            double ideal = IdealWidth(previous.Duration, shortest, 2);
            double collisionMinimum = previous.RightExtent + current.LeftExtent +
                style.MinimumAccidentalGap;
            x += Math.Max(ideal, collisionMinimum);
            result.Add(new PositionedColumn(index, x));
        }

        return result.MoveToImmutable();
    }

    /// <summary>Measures and positions clef, key signature, and time signature glyphs.</summary>
    /// <param name="metadata">The SMuFL font metrics and glyph map.</param>
    /// <param name="clefGlyphName">The clef's SMuFL glyph name.</param>
    /// <param name="key">The conventional key signature.</param>
    /// <param name="meter">The time signature.</param>
    /// <param name="style">The engraving style.</param>
    /// <returns>The horizontally laid out header.</returns>
    public SystemHeaderLayout BuildHeader(SmuflMetadata metadata, string clefGlyphName,
        KeySignature key, TimeSignature meter, Style style)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(style);
        ImmutableArray<HeaderSymbol>.Builder symbols = ImmutableArray.CreateBuilder<HeaderSymbol>();
        double x = 0;
        AddSymbol(symbols, metadata, HeaderPart.Clef, clefGlyphName, x);
        x += GlyphWidth(metadata, clefGlyphName) + style.MinimumRhythmicGap;

        // Behind Bars, Accidentals and Key Signatures > Key Signatures:
        // reserve one glyph cell for each accidental in the signature.
        int accidentalCount = Math.Abs(key.Fifths);
        string accidentalName = key.Fifths >= 0 ? "accidentalSharp" : "accidentalFlat";
        for (int index = 0; index < accidentalCount; index++)
        {
            AddSymbol(symbols, metadata, HeaderPart.KeySignature, accidentalName, x);
            x += GlyphWidth(metadata, accidentalName) + style.MinimumAccidentalGap;
        }

        if (accidentalCount > 0)
        {
            x += style.MinimumRhythmicGap;
        }

        // Behind Bars, Metre > Time signatures: place numerator and denominator
        // in a shared horizontal cell after the clef and key signature.
        string numerator = meter.Numerator.ToString(CultureInfo.InvariantCulture);
        string denominator = meter.Denominator.ToString(CultureInfo.InvariantCulture);
        double numeratorWidth = DigitRowWidth(metadata, numerator);
        double denominatorWidth = DigitRowWidth(metadata, denominator);
        double meterWidth = Math.Max(numeratorWidth, denominatorWidth);
        AddDigitRow(symbols, metadata, HeaderPart.MeterNumerator,
            numerator, x + (meterWidth - numeratorWidth) / 2);
        AddDigitRow(symbols, metadata, HeaderPart.MeterDenominator,
            denominator, x + (meterWidth - denominatorWidth) / 2);
        return new SystemHeaderLayout(symbols.ToImmutable(),
            x + meterWidth + style.MinimumRhythmicGap);
    }

    private static void AddDigitRow(ImmutableArray<HeaderSymbol>.Builder symbols,
        SmuflMetadata metadata, HeaderPart part, string digits, double startX)
    {
        double x = startX;
        foreach (char digit in digits)
        {
            string name = $"timeSig{digit}";
            AddSymbol(symbols, metadata, part, name, x);
            x += GlyphWidth(metadata, name);
        }
    }

    private static double DigitRowWidth(SmuflMetadata metadata, string digits)
    {
        double width = 0;
        foreach (char digit in digits)
        {
            width += GlyphWidth(metadata, $"timeSig{digit}");
        }

        return width;
    }

    private static void AddSymbol(ImmutableArray<HeaderSymbol>.Builder symbols,
        SmuflMetadata metadata, HeaderPart part, string name, double x) =>
        symbols.Add(new HeaderSymbol(part, name, metadata.GetGlyphCodepoint(name),
            x, GlyphWidth(metadata, name)));

    private static double GlyphWidth(SmuflMetadata metadata, string name)
    {
        SmuflBoundingBox box = metadata.GetBoundingBox(name);
        return box.NorthEast.X - box.SouthWest.X;
    }
}
