namespace Tessitura.IO.Tests.MusicXml;

/// <summary>Selects the round trip the CI report measures. F3.2 and F3.3 replace it with the real one.</summary>
public static class TessituraRoundTrip
{
    /// <summary>Gets the implementation used for the corpus report.</summary>
    public static IMusicXmlRoundTrip Current { get; } = new NotYetImplementedRoundTrip();
}
