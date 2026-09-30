using Tessitura.IO.MusicXml;

namespace Tessitura.IO.Tests.MusicXml;

/// <summary>Imports a file with Tessitura and exports it again as MusicXML 4.0.</summary>
public sealed class TessituraMusicXmlRoundTrip : IMusicXmlRoundTrip
{
    /// <inheritdoc />
    public string Name => "Tessitura (importador F3.2 y exportador F3.3)";

    /// <inheritdoc />
    public bool TryRoundTrip(string sourcePath, string outputPath)
    {
        MusicXmlExporter.Save(outputPath, MusicXmlImporter.Import(sourcePath).Score);
        return true;
    }
}

/// <summary>Selects the round trip the CI report measures.</summary>
public static class TessituraRoundTrip
{
    /// <summary>Gets the implementation used for the corpus report.</summary>
    public static IMusicXmlRoundTrip Current { get; } = new TessituraMusicXmlRoundTrip();
}
