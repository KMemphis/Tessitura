using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Tessitura.IO.Tests.MusicXml;

/// <summary>Turns a corpus file into the file a round trip produced.</summary>
public interface IMusicXmlRoundTrip
{
    /// <summary>Gets the name shown in the report.</summary>
    string Name { get; }

    /// <summary>Imports <paramref name="sourcePath"/> and exports it again.</summary>
    /// <param name="sourcePath">The original file.</param>
    /// <param name="outputPath">Where the exported MusicXML must be written.</param>
    /// <returns>Whether the round trip is implemented for the file.</returns>
    /// <exception cref="Exception">The import or export failed.</exception>
    bool TryRoundTrip(string sourcePath, string outputPath);
}

/// <summary>The current round trip: Tessitura cannot import MusicXML until F3.2.</summary>
public sealed class NotYetImplementedRoundTrip : IMusicXmlRoundTrip
{
    /// <inheritdoc />
    public string Name => "Tessitura (importador y exportador pendientes: F3.2 y F3.3)";

    /// <inheritdoc />
    public bool TryRoundTrip(string sourcePath, string outputPath) => false;
}

/// <summary>Result for one corpus file.</summary>
/// <param name="File">The corpus file.</param>
/// <param name="Status">"lossless", "lossy", "unsupported", "failed" or "unreadable".</param>
/// <param name="Matched">Events preserved.</param>
/// <param name="Total">Events in the original.</param>
/// <param name="Extra">Events the export invented.</param>
/// <param name="Detail">A short explanation.</param>
public sealed record FidelityEntry(CorpusFile File, string Status, int Matched, int Total, int Extra, string Detail);

/// <summary>Measures and reports round-trip fidelity over the corpus.</summary>
public sealed class FidelityReport
{
    private FidelityReport(string roundTripName, IReadOnlyList<FidelityEntry> entries)
    {
        RoundTripName = roundTripName;
        Entries = entries;
    }

    /// <summary>Gets the round trip that was measured.</summary>
    public string RoundTripName { get; }

    /// <summary>Gets one entry per corpus file.</summary>
    public IReadOnlyList<FidelityEntry> Entries { get; }

    /// <summary>Runs the round trip over the files and compares each result with its original.</summary>
    /// <param name="files">The corpus files.</param>
    /// <param name="roundTrip">The implementation to measure.</param>
    /// <param name="workDirectory">A scratch directory for exported files.</param>
    /// <returns>The report.</returns>
    public static FidelityReport Measure(IReadOnlyList<CorpusFile> files, IMusicXmlRoundTrip roundTrip, string workDirectory)
    {
        Directory.CreateDirectory(workDirectory);
        List<FidelityEntry> entries = [];
        int index = 0;
        foreach (CorpusFile file in files)
        {
            MusicXmlSignature original;
            try
            {
                original = MusicXmlSignature.FromFile(file.Path);
            }
            catch (Exception exception) when (exception is InvalidDataException or System.Xml.XmlException or FormatException or InvalidOperationException or IOException)
            {
                entries.Add(new FidelityEntry(file, "unreadable", 0, 0, 0, exception.Message));
                continue;
            }

            string output = Path.Combine(workDirectory, $"{index++}-{Path.GetFileNameWithoutExtension(file.Path)}.musicxml");
            try
            {
                if (!roundTrip.TryRoundTrip(file.Path, output))
                {
                    entries.Add(new FidelityEntry(file, "unsupported", 0, original.Events.Count, 0, ""));
                    continue;
                }

                (int matched, int missing, int extra) = MusicXmlSignature.Compare(original, MusicXmlSignature.FromFile(output));
                entries.Add(new FidelityEntry(file, missing == 0 && extra == 0 ? "lossless" : "lossy",
                    matched, original.Events.Count, extra, ""));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                entries.Add(new FidelityEntry(file, "failed", 0, original.Events.Count, 0,
                    $"{exception.GetType().Name}: {exception.Message}"));
            }
        }

        return new FidelityReport(roundTrip.Name, entries);
    }

    /// <summary>Gets the share of in-scope files that came back without any loss, from 0 to 1.</summary>
    public double LosslessShareInScope => Share(entry => entry.File.InScope && entry.Status == "lossless",
        entry => entry.File.InScope && entry.Status != "unreadable");

    /// <summary>Gets the share of in-scope events that were preserved, from 0 to 1.</summary>
    public double EventShareInScope
    {
        get
        {
            long matched = 0;
            long total = 0;
            foreach (FidelityEntry entry in Entries)
            {
                if (entry.File.InScope && entry.Status != "unreadable")
                {
                    matched += entry.Matched;
                    total += entry.Total;
                }
            }

            return total == 0 ? 0 : (double)matched / total;
        }
    }

    /// <summary>Writes the report as Markdown, in Spanish.</summary>
    /// <returns>The Markdown text.</returns>
    public string ToMarkdown()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        StringBuilder text = new();
        int inScope = Entries.Count(e => e.File.InScope);
        text.Append("# Informe de fidelidad MusicXML\n\n");
        text.Append(c, $"Ida y vuelta medida: {RoundTripName}\n\n");
        text.Append("| Indicador | Valor |\n| --- | --- |\n");
        text.Append(c, $"| Archivos del corpus | {Entries.Count} |\n");
        text.Append(c, $"| Dentro del alcance de F3 | {inScope} |\n");
        text.Append(c, $"| Sin pérdidas (dentro del alcance) | {LosslessShareInScope:P1} |\n");
        text.Append(c, $"| Eventos conservados (dentro del alcance) | {EventShareInScope:P1} |\n");
        text.Append("| Objetivo de la definición | ≥ 95 % |\n\n");
        text.Append("## Por origen y grupo\n\n| Origen | Grupo | Archivos | Alcance | Sin pérdidas | Con pérdidas | No soportados | Fallos | Ilegibles |\n| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (IGrouping<(string Source, string Group), FidelityEntry> group in Entries
            .GroupBy(e => (e.File.Source, e.File.Group)).OrderBy(g => g.Key.Source, StringComparer.Ordinal).ThenBy(g => g.Key.Group, StringComparer.Ordinal))
        {
            text.Append(c, $"| {group.Key.Source} | {group.Key.Group} | {group.Count()} | {group.Count(e => e.File.InScope)} | {group.Count(e => e.Status == "lossless")} | {group.Count(e => e.Status == "lossy")} | {group.Count(e => e.Status == "unsupported")} | {group.Count(e => e.Status == "failed")} | {group.Count(e => e.Status == "unreadable")} |\n");
        }

        text.Append("\n## Archivos con problemas dentro del alcance\n\n");
        List<FidelityEntry> problems = [.. Entries.Where(e => e.File.InScope && e.Status is "lossy" or "failed" or "unreadable")];
        if (problems.Count == 0)
        {
            text.Append("Ninguno.\n");
        }

        foreach (FidelityEntry entry in problems)
        {
            text.Append(c, $"- `{entry.File.RelativePath}`: {entry.Status} ({entry.Matched}/{entry.Total} eventos, {entry.Extra} de más) {entry.Detail}\n");
        }

        text.Append("\n## Archivos fuera del alcance\n\n");
        foreach (IGrouping<string, FidelityEntry> reason in Entries.Where(e => !e.File.InScope).GroupBy(e => e.File.Reason).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.Append(c, $"- {reason.Key}: {reason.Count()}\n");
        }

        return text.ToString();
    }

    /// <summary>Writes the report as JSON.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson() => JsonSerializer.Serialize(new
    {
        roundTrip = RoundTripName,
        files = Entries.Count,
        inScope = Entries.Count(e => e.File.InScope),
        losslessShareInScope = LosslessShareInScope,
        eventShareInScope = EventShareInScope,
        entries = Entries.Select(e => new
        {
            file = e.File.RelativePath, source = e.File.Source, group = e.File.Group, inScope = e.File.InScope,
            reason = e.File.Reason, status = e.Status, matched = e.Matched, total = e.Total, extra = e.Extra, detail = e.Detail,
        }),
    }, new JsonSerializerOptions { WriteIndented = true });

    private double Share(Func<FidelityEntry, bool> numerator, Func<FidelityEntry, bool> denominator)
    {
        int total = Entries.Count(denominator);
        return total == 0 ? 0 : (double)Entries.Count(numerator) / total;
    }
}
