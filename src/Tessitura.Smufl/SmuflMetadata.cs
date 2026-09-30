using System.Collections.Immutable;
using System.Text.Json;

namespace Tessitura.Smufl;

/// <summary>Represents a point in staff-space units.</summary>
/// <param name="X">The horizontal coordinate.</param>
/// <param name="Y">The vertical coordinate.</param>
public readonly record struct SmuflPoint(double X, double Y);

/// <summary>Represents a glyph's bounding box in staff-space units.</summary>
/// <param name="SouthWest">The south-west corner.</param>
/// <param name="NorthEast">The north-east corner.</param>
public readonly record struct SmuflBoundingBox(SmuflPoint SouthWest, SmuflPoint NorthEast);

/// <summary>Provides the metrics and standard glyph map for one SMuFL font.</summary>
public sealed class SmuflMetadata
{
    private readonly Dictionary<string, double> _engravingDefaults;
    private readonly Dictionary<string, SmuflBoundingBox> _boundingBoxes;
    private readonly Dictionary<string, Dictionary<string, SmuflPoint>> _anchors;
    private readonly Dictionary<string, int> _glyphCodepoints;

    private SmuflMetadata(
        string fontName,
        string fontVersion,
        ImmutableArray<string> textFontFamilies,
        Dictionary<string, double> engravingDefaults,
        Dictionary<string, SmuflBoundingBox> boundingBoxes,
        Dictionary<string, Dictionary<string, SmuflPoint>> anchors,
        Dictionary<string, int> glyphCodepoints)
    {
        FontName = fontName;
        FontVersion = fontVersion;
        TextFontFamilies = textFontFamilies;
        _engravingDefaults = engravingDefaults;
        _boundingBoxes = boundingBoxes;
        _anchors = anchors;
        _glyphCodepoints = glyphCodepoints;
    }

    /// <summary>Gets the font's declared name.</summary>
    public string FontName { get; }

    /// <summary>Gets the font's declared version.</summary>
    public string FontVersion { get; }

    /// <summary>Gets the recommended text font families in order.</summary>
    public ImmutableArray<string> TextFontFamilies { get; }

    /// <summary>Loads font metrics and the standard glyph name map from JSON files.</summary>
    /// <param name="metadataPath">The font-specific SMuFL metadata file.</param>
    /// <param name="glyphNamesPath">The standard glyph name to codepoint map.</param>
    /// <returns>The parsed font metadata.</returns>
    public static SmuflMetadata Load(string metadataPath, string glyphNamesPath)
    {
        using FileStream metadataStream = File.OpenRead(metadataPath);
        using JsonDocument metadataDocument = JsonDocument.Parse(metadataStream);
        JsonElement root = metadataDocument.RootElement;

        Dictionary<string, double> defaults = new(StringComparer.Ordinal);
        ImmutableArray<string>.Builder families = ImmutableArray.CreateBuilder<string>();
        foreach (JsonProperty property in root.GetProperty("engravingDefaults").EnumerateObject())
        {
            if (property.NameEquals("textFontFamily"))
            {
                foreach (JsonElement family in property.Value.EnumerateArray())
                {
                    families.Add(family.GetString() ?? throw new JsonException("A text font family is null."));
                }
            }
            else
            {
                defaults.Add(property.Name, property.Value.GetDouble());
            }
        }

        Dictionary<string, SmuflBoundingBox> boxes = new(StringComparer.Ordinal);
        foreach (JsonProperty glyph in root.GetProperty("glyphBBoxes").EnumerateObject())
        {
            boxes.Add(glyph.Name, new SmuflBoundingBox(
                ParsePoint(glyph.Value.GetProperty("bBoxSW")),
                ParsePoint(glyph.Value.GetProperty("bBoxNE"))));
        }

        Dictionary<string, Dictionary<string, SmuflPoint>> anchors = new(StringComparer.Ordinal);
        foreach (JsonProperty glyph in root.GetProperty("glyphsWithAnchors").EnumerateObject())
        {
            Dictionary<string, SmuflPoint> glyphAnchors = new(StringComparer.Ordinal);
            foreach (JsonProperty anchor in glyph.Value.EnumerateObject())
            {
                glyphAnchors.Add(anchor.Name, ParsePoint(anchor.Value));
            }

            anchors.Add(glyph.Name, glyphAnchors);
        }

        using FileStream namesStream = File.OpenRead(glyphNamesPath);
        using JsonDocument namesDocument = JsonDocument.Parse(namesStream);
        Dictionary<string, int> codepoints = new(StringComparer.Ordinal);
        foreach (JsonProperty glyph in namesDocument.RootElement.EnumerateObject())
        {
            codepoints.Add(glyph.Name, glyph.Value.GetInt32());
        }

        return new SmuflMetadata(
            root.GetProperty("fontName").GetString() ?? throw new JsonException("The font name is null."),
            root.GetProperty("fontVersion").ToString(),
            families.ToImmutable(),
            defaults,
            boxes,
            anchors,
            codepoints);
    }

    /// <summary>Gets a numeric engraving default by name.</summary>
    /// <param name="name">The SMuFL engraving default name.</param>
    /// <returns>The value in staff-space units.</returns>
    public double GetEngravingDefault(string name) => _engravingDefaults[name];

    /// <summary>Gets a glyph's bounding box.</summary>
    /// <param name="glyphName">The standard SMuFL glyph name.</param>
    /// <returns>The bounding box in staff-space units.</returns>
    public SmuflBoundingBox GetBoundingBox(string glyphName) => _boundingBoxes[glyphName];

    /// <summary>Gets a glyph anchor by name.</summary>
    /// <param name="glyphName">The standard SMuFL glyph name.</param>
    /// <param name="anchorName">The SMuFL anchor name.</param>
    /// <returns>The anchor in staff-space units.</returns>
    public SmuflPoint GetAnchor(string glyphName, string anchorName) => _anchors[glyphName][anchorName];

    /// <summary>Gets the Unicode codepoint assigned to a standard glyph name.</summary>
    /// <param name="glyphName">The standard SMuFL glyph name.</param>
    /// <returns>The Unicode codepoint.</returns>
    public int GetGlyphCodepoint(string glyphName) => _glyphCodepoints[glyphName];

    private static SmuflPoint ParsePoint(JsonElement array)
    {
        if (array.GetArrayLength() != 2)
        {
            throw new JsonException("A SMuFL point must have two coordinates.");
        }

        return new SmuflPoint(array[0].GetDouble(), array[1].GetDouble());
    }
}
