using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tessitura.Engraving.DisplayLists;

/// <summary>Identifies the score element that produced a drawing primitive.</summary>
/// <param name="Value">The stable element identifier.</param>
public readonly record struct ElementId(Guid Value);

/// <summary>Locates a point in page coordinates measured in staff spaces.</summary>
/// <param name="X">The horizontal coordinate.</param>
/// <param name="Y">The vertical coordinate.</param>
public readonly record struct DisplayPoint(double X, double Y);

/// <summary>Bounds a drawing primitive in page coordinates measured in staff spaces.</summary>
/// <param name="X">The left coordinate.</param>
/// <param name="Y">The top coordinate.</param>
/// <param name="Width">The horizontal extent.</param>
/// <param name="Height">The vertical extent.</param>
public readonly record struct DisplayBox(double X, double Y, double Width, double Height);

/// <summary>Provides the source element and bounds shared by display primitives.</summary>
/// <param name="ElementId">The originating score element.</param>
/// <param name="Bounds">The bounds in page coordinates.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Glyph), "glyph")]
[JsonDerivedType(typeof(Line), "line")]
[JsonDerivedType(typeof(Path), "path")]
[JsonDerivedType(typeof(Text), "text")]
[JsonDerivedType(typeof(Rect), "rect")]
public abstract record DrawingPrimitive(ElementId ElementId, DisplayBox Bounds);

/// <summary>Places a SMuFL glyph on a page.</summary>
/// <param name="ElementId">The originating score element.</param>
/// <param name="Bounds">The occupied bounds.</param>
/// <param name="Codepoint">The SMuFL Unicode codepoint.</param>
/// <param name="Origin">The glyph baseline origin.</param>
/// <param name="Size">The glyph size in staff spaces.</param>
public sealed record Glyph(ElementId ElementId, DisplayBox Bounds, int Codepoint,
    DisplayPoint Origin, double Size) : DrawingPrimitive(ElementId, Bounds);

/// <summary>Draws a straight stroked segment.</summary>
/// <param name="ElementId">The originating score element.</param>
/// <param name="Bounds">The occupied bounds.</param>
/// <param name="Start">The first endpoint.</param>
/// <param name="End">The second endpoint.</param>
/// <param name="StrokeWidth">The stroke thickness in staff spaces.</param>
public sealed record Line(ElementId ElementId, DisplayBox Bounds, DisplayPoint Start,
    DisplayPoint End, double StrokeWidth) : DrawingPrimitive(ElementId, Bounds);

/// <summary>Names a supported vector path command.</summary>
public enum PathVerb
{
    /// <summary>Move to the first point without drawing.</summary>
    MoveTo,
    /// <summary>Draw a straight segment to the first point.</summary>
    LineTo,
    /// <summary>Draw a cubic Bézier using all three points.</summary>
    CubicTo,
    /// <summary>Close the current contour.</summary>
    Close,
}

/// <summary>Stores one vector path command and up to three points.</summary>
/// <param name="Verb">The command kind.</param>
/// <param name="Point1">The endpoint or first control point.</param>
/// <param name="Point2">The second control point.</param>
/// <param name="Point3">The cubic endpoint.</param>
public readonly record struct PathCommand(PathVerb Verb, DisplayPoint Point1,
    DisplayPoint Point2, DisplayPoint Point3);

/// <summary>Draws an immutable vector path.</summary>
/// <param name="ElementId">The originating score element.</param>
/// <param name="Bounds">The occupied bounds.</param>
/// <param name="Commands">The ordered path commands.</param>
/// <param name="StrokeWidth">The stroke thickness in staff spaces.</param>
public sealed record Path(ElementId ElementId, DisplayBox Bounds,
    ImmutableArray<PathCommand> Commands, double StrokeWidth) : DrawingPrimitive(ElementId, Bounds)
{
    /// <summary>Compares path geometry by value, including its command sequence.</summary>
    public bool Equals(Path? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || ElementId != other.ElementId || Bounds != other.Bounds ||
            StrokeWidth != other.StrokeWidth || Commands.Length != other.Commands.Length)
        {
            return false;
        }

        for (int index = 0; index < Commands.Length; index++)
        {
            if (Commands[index] != other.Commands[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(ElementId);
        hash.Add(Bounds);
        hash.Add(StrokeWidth);
        foreach (PathCommand command in Commands)
        {
            hash.Add(command);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Places a text run on a page.</summary>
/// <param name="ElementId">The originating score element.</param>
/// <param name="Bounds">The occupied bounds.</param>
/// <param name="Content">The text to draw.</param>
/// <param name="Origin">The baseline origin.</param>
/// <param name="Size">The text size in staff spaces.</param>
public sealed record Text(ElementId ElementId, DisplayBox Bounds, string Content,
    DisplayPoint Origin, double Size) : DrawingPrimitive(ElementId, Bounds);

/// <summary>Draws a filled or outlined rectangle.</summary>
/// <param name="ElementId">The originating score element.</param>
/// <param name="Bounds">The rectangle bounds.</param>
/// <param name="Filled">Whether the rectangle is filled.</param>
public sealed record Rect(ElementId ElementId, DisplayBox Bounds, bool Filled)
    : DrawingPrimitive(ElementId, Bounds);

/// <summary>Contains the immutable display list for one score page.</summary>
public sealed class Page : IEquatable<Page>
{
    /// <summary>Creates one page in staff-space coordinates.</summary>
    /// <param name="number">The one-based page number.</param>
    /// <param name="width">The page width in staff spaces.</param>
    /// <param name="height">The page height in staff spaces.</param>
    /// <param name="primitives">The ordered drawing primitives.</param>
    [JsonConstructor]
    public Page(int number, double width, double height, ImmutableArray<DrawingPrimitive> primitives)
    {
        Number = number;
        Width = width;
        Height = height;
        Primitives = primitives.IsDefault ? ImmutableArray<DrawingPrimitive>.Empty : primitives;
    }

    /// <summary>Gets the one-based page number.</summary>
    public int Number { get; }

    /// <summary>Gets the page width in staff spaces.</summary>
    public double Width { get; }

    /// <summary>Gets the page height in staff spaces.</summary>
    public double Height { get; }

    /// <summary>Gets the ordered drawing primitives.</summary>
    public ImmutableArray<DrawingPrimitive> Primitives { get; }

    /// <inheritdoc />
    public bool Equals(Page? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Number != other.Number || Width != other.Width ||
            Height != other.Height || Primitives.Length != other.Primitives.Length)
        {
            return false;
        }

        for (int index = 0; index < Primitives.Length; index++)
        {
            if (Primitives[index] != other.Primitives[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Page other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Number);
        hash.Add(Width);
        hash.Add(Height);
        foreach (DrawingPrimitive primitive in Primitives)
        {
            hash.Add(primitive);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Provides source-generated JSON metadata for display lists.</summary>
[JsonSerializable(typeof(Page))]
public partial class DisplayListJsonContext : JsonSerializerContext;
