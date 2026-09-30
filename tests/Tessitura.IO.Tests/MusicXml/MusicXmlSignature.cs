using System.IO.Compression;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Tessitura.Core;

namespace Tessitura.IO.Tests.MusicXml;

/// <summary>One sounding or silent event read straight from a MusicXML file, independent of Tessitura's model.</summary>
/// <param name="Part">The zero-based part index in file order.</param>
/// <param name="Measure">The zero-based measure index in file order.</param>
/// <param name="Staff">The staff number within the part.</param>
/// <param name="Voice">The voice label.</param>
/// <param name="Onset">The onset in whole notes from the start of the measure.</param>
/// <param name="Duration">The duration in whole notes (zero for grace notes).</param>
/// <param name="Kind">"note", "rest", "grace" or "unpitched".</param>
/// <param name="Pitch">The spelled pitch such as "C#4", empty for rests.</param>
/// <param name="Tie">"start", "stop", "both" or empty.</param>
public sealed record MusicXmlEvent(
    int Part, int Measure, int Staff, string Voice, Fraction Onset, Fraction Duration,
    string Kind, string Pitch, string Tie)
{
    /// <summary>Gets the comparison key. Staff and voice numbering may legitimately change on export.</summary>
    public string Key => string.Create(CultureInfo.InvariantCulture,
        $"{Part}|{Measure}|{Onset.Num}/{Onset.Den}|{Duration.Num}/{Duration.Den}|{Kind}|{Pitch}|{Tie}");
}

/// <summary>The musical content of a MusicXML file as a comparable list of events.</summary>
/// <param name="Events">The events in file order.</param>
/// <param name="Notes">Reasons the extraction had to guess or skip something.</param>
public sealed record MusicXmlSignature(IReadOnlyList<MusicXmlEvent> Events, IReadOnlyList<string> Notes)
{
    /// <summary>Loads a MusicXML document from a plain or compressed (.mxl) file.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The parsed document.</returns>
    public static XDocument LoadDocument(string path)
    {
        if (path.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase))
        {
            using ZipArchive archive = ZipFile.OpenRead(path);
            ZipArchiveEntry container = archive.GetEntry("META-INF/container.xml")
                ?? throw new InvalidDataException("The .mxl has no META-INF/container.xml.");
            string rootPath;
            using (Stream stream = container.Open())
            {
                rootPath = Parse(stream).Descendants().First(e => e.Name.LocalName == "rootfile")
                    .Attribute("full-path")?.Value ?? throw new InvalidDataException("No rootfile path.");
            }

            ZipArchiveEntry root = archive.GetEntry(rootPath)
                ?? throw new InvalidDataException($"The .mxl has no {rootPath}.");
            using Stream rootStream = root.Open();
            return Parse(rootStream);
        }

        using FileStream file = File.OpenRead(path);
        return Parse(file);
    }

    /// <summary>Reads a signature from a file.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The signature.</returns>
    public static MusicXmlSignature FromFile(string path) => FromDocument(LoadDocument(path));

    /// <summary>Extracts the signature of a score-partwise document.</summary>
    /// <param name="document">The parsed document.</param>
    /// <returns>The signature.</returns>
    public static MusicXmlSignature FromDocument(XDocument document)
    {
        List<MusicXmlEvent> events = [];
        List<string> notes = [];
        XElement root = document.Root ?? throw new InvalidDataException("The document is empty.");
        if (root.Name.LocalName != "score-partwise")
        {
            throw new InvalidDataException($"Only score-partwise is supported, not {root.Name.LocalName}.");
        }

        int partIndex = 0;
        foreach (XElement part in root.Elements().Where(e => e.Name.LocalName == "part"))
        {
            int divisions = 0;
            int measureIndex = 0;
            foreach (XElement measure in part.Elements().Where(e => e.Name.LocalName == "measure"))
            {
                long cursor = 0;
                long lastOnset = 0;
                foreach (XElement element in measure.Elements())
                {
                    switch (element.Name.LocalName)
                    {
                        case "attributes":
                            if (element.Elements().FirstOrDefault(e => e.Name.LocalName == "divisions") is { } d)
                            {
                                divisions = int.Parse(d.Value, CultureInfo.InvariantCulture);
                            }

                            break;
                        case "backup":
                            cursor -= ReadDuration(element);
                            break;
                        case "forward":
                            cursor += ReadDuration(element);
                            break;
                        case "note":
                            if (divisions == 0)
                            {
                                divisions = 1;
                                notes.Add("no <divisions>: assumed 1");
                            }

                            ReadNote(element, partIndex, measureIndex, divisions, ref cursor, ref lastOnset,
                                events, notes);
                            break;
                    }
                }

                measureIndex++;
            }

            partIndex++;
        }

        return new MusicXmlSignature(events, notes);
    }

    /// <summary>Compares two signatures as multisets of events.</summary>
    /// <param name="expected">The signature of the original file.</param>
    /// <param name="actual">The signature of the round-tripped file.</param>
    /// <returns>Matched, missing and extra event counts.</returns>
    public static (int Matched, int Missing, int Extra) Compare(MusicXmlSignature expected, MusicXmlSignature actual)
    {
        Dictionary<string, int> remaining = [];
        foreach (MusicXmlEvent e in expected.Events)
        {
            remaining[e.Key] = remaining.GetValueOrDefault(e.Key) + 1;
        }

        int matched = 0;
        int extra = 0;
        foreach (MusicXmlEvent e in actual.Events)
        {
            if (remaining.TryGetValue(e.Key, out int count) && count > 0)
            {
                remaining[e.Key] = count - 1;
                matched++;
            }
            else
            {
                extra++;
            }
        }

        return (matched, expected.Events.Count - matched, extra);
    }

    private static XDocument Parse(Stream stream)
    {
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader);
    }

    private static long ReadDuration(XElement element) =>
        long.Parse(element.Elements().First(e => e.Name.LocalName == "duration").Value.Trim(),
            CultureInfo.InvariantCulture);

    private static void ReadNote(XElement note, int part, int measure, int divisions, ref long cursor,
        ref long lastOnset, List<MusicXmlEvent> events, List<string> notes)
    {
        bool isChord = note.Elements().Any(e => e.Name.LocalName == "chord");
        bool isGrace = note.Elements().Any(e => e.Name.LocalName == "grace");
        long onset = isChord ? lastOnset : cursor;
        long duration = 0;
        if (!isGrace && note.Elements().FirstOrDefault(e => e.Name.LocalName == "duration") is { } durationElement)
        {
            duration = long.Parse(durationElement.Value.Trim(), CultureInfo.InvariantCulture);
        }

        string kind = isGrace ? "grace" : "note";
        string pitchText = "";
        if (note.Elements().FirstOrDefault(e => e.Name.LocalName == "rest") is not null)
        {
            kind = "rest";
        }
        else if (note.Elements().FirstOrDefault(e => e.Name.LocalName == "unpitched") is not null)
        {
            kind = isGrace ? "grace" : "unpitched";
        }
        else if (note.Elements().FirstOrDefault(e => e.Name.LocalName == "pitch") is { } pitch)
        {
            string step = pitch.Elements().First(e => e.Name.LocalName == "step").Value.Trim();
            string octave = pitch.Elements().First(e => e.Name.LocalName == "octave").Value.Trim();
            string alter = pitch.Elements().FirstOrDefault(e => e.Name.LocalName == "alter")?.Value.Trim() ?? "0";
            pitchText = $"{step}{(alter == "0" ? "" : $"[{alter}]")}{octave}";
        }

        bool tieStart = note.Elements().Any(e => e.Name.LocalName == "tie" && e.Attribute("type")?.Value == "start");
        bool tieStop = note.Elements().Any(e => e.Name.LocalName == "tie" && e.Attribute("type")?.Value == "stop");
        string tie = tieStart && tieStop ? "both" : tieStart ? "start" : tieStop ? "stop" : "";
        int staff = int.Parse(note.Elements().FirstOrDefault(e => e.Name.LocalName == "staff")?.Value.Trim() ?? "1",
            CultureInfo.InvariantCulture);
        string voice = note.Elements().FirstOrDefault(e => e.Name.LocalName == "voice")?.Value.Trim() ?? "1";
        if (note.Elements().Any(e => e.Name.LocalName == "time-modification"))
        {
            notes.Add("tuplet: duration taken from <duration>");
        }

        events.Add(new MusicXmlEvent(part, measure, staff, voice,
            new Fraction(onset, divisions * 4L), new Fraction(duration, divisions * 4L), kind, pitchText, tie));
        if (!isChord && !isGrace)
        {
            lastOnset = onset;
            cursor += duration;
        }
    }
}
