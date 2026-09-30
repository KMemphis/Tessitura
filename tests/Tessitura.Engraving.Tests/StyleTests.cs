using System.Text.Json;
using Tessitura.Engraving;
using Tessitura.Smufl;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class StyleTests
{
    [Fact]
    public void LoadsFontDefaultsAndDocumentedSpacingDefaults()
    {
        SmuflMetadata metadata = LoadMetadata();

        Style style = Style.CreateDefault(metadata);

        Assert.Equal(metadata.GetEngravingDefault("staffLineThickness"), style.StaffLineThickness);
        Assert.Equal(metadata.GetEngravingDefault("stemThickness"), style.StemThickness);
        Assert.Equal(metadata.GetEngravingDefault("beamThickness"), style.BeamThickness);
        Assert.Equal(metadata.GetEngravingDefault("beamSpacing"), style.BeamSpacing);
        Assert.Equal(metadata.GetEngravingDefault("legerLineThickness"), style.LedgerLineThickness);
        Assert.Equal(metadata.GetEngravingDefault("legerLineExtension"), style.LedgerLineExtension);
        Assert.Equal(3.5, style.StemLength);
        Assert.Equal(0.25, style.MinimumAccidentalGap);
        Assert.Equal(0.5, style.MinimumRhythmicGap);
    }

    [Fact]
    public void StyleCanBeCustomizedAndRoundTrippedAsJson()
    {
        Style original = Style.CreateDefault(LoadMetadata()) with
        {
            StemLength = 4,
            MinimumAccidentalGap = 0.3,
        };

        string json = JsonSerializer.Serialize(original, StyleJsonContext.Default.Style);
        Style? restored = JsonSerializer.Deserialize(json, StyleJsonContext.Default.Style);

        Assert.Equal(original, restored);
        Assert.Contains("\"StemLength\":4", json, StringComparison.Ordinal);
    }

    private static SmuflMetadata LoadMetadata() => SmuflMetadata.Load(
        Path.Combine(Root, "assets", "fonts", "Bravura.json"),
        Path.Combine(Root, "assets", "fonts", "smufl_glyph_names.json"));

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
