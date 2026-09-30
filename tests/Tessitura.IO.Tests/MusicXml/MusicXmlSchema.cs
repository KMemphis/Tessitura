using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Tessitura.IO.Tests.MusicXml;

/// <summary>Validates documents against the official MusicXML 4.0 XSD, using the local copies.</summary>
public static class MusicXmlSchema
{
    private static readonly Lazy<XmlSchemaSet> Schemas = new(Load);

    /// <summary>Validates a document.</summary>
    /// <param name="document">The MusicXML document.</param>
    /// <returns>The validation errors, empty when the document is valid.</returns>
    public static IReadOnlyList<string> Validate(XDocument document)
    {
        List<string> errors = [];
        document.Validate(Schemas.Value, (_, e) => errors.Add($"{e.Severity}: {e.Message}"));
        return errors;
    }

    private static XmlSchemaSet Load()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Schema");
        XmlSchemaSet set = new() { XmlResolver = new LocalResolver(directory) };
        // musicxml.xsd imports xml.xsd and xlink.xsd through their official URLs, which the resolver maps to local files.
        using XmlReader reader = XmlReader.Create(Path.Combine(directory, "musicxml.xsd"),
            new XmlReaderSettings { XmlResolver = new LocalResolver(directory), DtdProcessing = DtdProcessing.Ignore });
        set.Add(null, reader);

        set.Compile();
        return set;
    }

    private sealed class LocalResolver(string directory) : XmlUrlResolver
    {
        public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
        {
            if (relativeUri is not null && relativeUri.StartsWith("http://www.musicxml.org/xsd/", StringComparison.Ordinal))
            {
                return new Uri(Path.Combine(directory, Path.GetFileName(relativeUri)));
            }

            return base.ResolveUri(baseUri, relativeUri)!;
        }
    }
}
