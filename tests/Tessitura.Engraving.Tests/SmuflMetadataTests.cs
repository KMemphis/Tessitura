using System.Text.Json;
using Tessitura.Smufl;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class SmuflMetadataTests
{
    [Fact]
    public void BlackNoteheadStemAnchorMatchesSourceJson()
    {
        SmuflMetadata metadata = LoadMetadata();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(MetadataPath));
        JsonElement anchor = document.RootElement
            .GetProperty("glyphsWithAnchors")
            .GetProperty("noteheadBlack")
            .GetProperty("stemUpSE");

        SmuflPoint parsed = metadata.GetAnchor("noteheadBlack", "stemUpSE");
        Assert.Equal(anchor[0].GetDouble(), parsed.X);
        Assert.Equal(anchor[1].GetDouble(), parsed.Y);
    }

    [Fact]
    public void LoadsDefaultsBoundingBoxesAndGlyphNames()
    {
        SmuflMetadata metadata = LoadMetadata();

        Assert.True(metadata.GetEngravingDefault("stemThickness") > 0);
        Assert.True(metadata.GetBoundingBox("noteheadBlack").NorthEast.X > 0);
        Assert.Equal(0xE0A4, metadata.GetGlyphCodepoint("noteheadBlack"));
        Assert.True(File.Exists(Path.Combine(Root, "assets", "fonts", "Bravura.otf")));
    }

    private static SmuflMetadata LoadMetadata() => SmuflMetadata.Load(
        MetadataPath,
        Path.Combine(Root, "assets", "fonts", "smufl_glyph_names.json"));

    private static string MetadataPath => Path.Combine(Root, "assets", "fonts", "Bravura.json");

    private static string Root
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Tessitura.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Tessitura.sln was not found above the test directory.");
        }
    }
}
