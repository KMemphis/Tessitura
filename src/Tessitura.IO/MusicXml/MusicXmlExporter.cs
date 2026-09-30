using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Tessitura.Core;

namespace Tessitura.IO.MusicXml;

/// <summary>Exports a score as MusicXML 4.0 score-partwise, plain (.musicxml) or compressed (.mxl).</summary>
public static class MusicXmlExporter
{
    /// <summary>Builds the MusicXML document of a score.</summary>
    /// <param name="score">The score to export.</param>
    /// <param name="appVersion">The application version written to the encoding software element.</param>
    /// <returns>A MusicXML 4.0 score-partwise document.</returns>
    public static XDocument ToDocument(Score score, string appVersion = "0.0.0")
    {
        ArgumentNullException.ThrowIfNull(score);
        int divisions = ChooseDivisions(score);
        XElement root = new("score-partwise", new XAttribute("version", "4.0"));
        if (score.Metadata.Title.Length > 0)
        {
            root.Add(new XElement("work", new XElement("work-title", score.Metadata.Title)));
        }

        XElement identification = new("identification");
        if (score.Metadata.Composer.Length > 0)
        {
            identification.Add(new XElement("creator", new XAttribute("type", "composer"), score.Metadata.Composer));
        }

        identification.Add(new XElement("encoding",
            new XElement("software", $"Tessitura {appVersion}")));
        root.Add(identification);

        XElement partList = new("part-list");
        for (int index = 0; index < score.Instruments.Length; index++)
        {
            partList.Add(new XElement("score-part", new XAttribute("id", PartId(index)),
                new XElement("part-name", score.Instruments[index].Name)));
        }

        root.Add(partList);
        int staffBase = 0;
        for (int index = 0; index < score.Instruments.Length; index++)
        {
            root.Add(BuildPart(score, index, staffBase, divisions));
            staffBase += score.Instruments[index].Staves.Length;
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("score-partwise", "-//Recordare//DTD MusicXML 4.0 Partwise//EN",
                "http://www.musicxml.org/dtds/partwise.dtd", null),
            root);
    }

    /// <summary>Writes a score to a file; a <c>.mxl</c> extension produces the compressed container.</summary>
    /// <param name="path">The destination path.</param>
    /// <param name="score">The score to export.</param>
    /// <param name="appVersion">The application version written to the file.</param>
    public static void Save(string path, Score score, string appVersion = "0.0.0")
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        XDocument document = ToDocument(score, appVersion);
        string fullPath = Path.GetFullPath(path);
        string temporary = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            if (fullPath.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase))
            {
                string entryName = $"{Path.GetFileNameWithoutExtension(fullPath)}.musicxml";
                using FileStream stream = new(temporary, FileMode.CreateNew);
                using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    ZipArchiveEntry container = archive.CreateEntry("META-INF/container.xml");
                    using (StreamWriter writer = new(container.Open(), new UTF8Encoding(false)))
                    {
                        writer.Write($"""
                            <?xml version="1.0" encoding="UTF-8"?>
                            <container><rootfiles><rootfile full-path="{entryName}" media-type="application/vnd.recordare.musicxml+xml"/></rootfiles></container>
                            """);
                    }

                    ZipArchiveEntry score1 = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    using Stream entryStream = score1.Open();
                    Write(document, entryStream);
                }

                stream.Flush(flushToDisk: true);
            }
            else
            {
                using FileStream stream = new(temporary, FileMode.CreateNew);
                Write(document, stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void Write(XDocument document, Stream stream)
    {
        XmlWriterSettings settings = new() { Indent = true, Encoding = new UTF8Encoding(false) };
        using XmlWriter writer = XmlWriter.Create(stream, settings);
        document.Save(writer);
    }

    private static XElement BuildPart(Score score, int instrumentIndex, int staffBase, int divisions)
    {
        Instrument instrument = score.Instruments[instrumentIndex];
        XElement part = new("part", new XAttribute("id", PartId(instrumentIndex)));
        int staffCount = instrument.Staves.Length;

        // Voices beyond the first that only ever hold rests are layout leftovers, not music.
        List<(int Staff, int Voice)> voices = [];
        for (int staff = 0; staff < staffCount; staff++)
        {
            SortedSet<int> numbers = [];
            for (int measure = 0; measure < score.Measures.Length; measure++)
            {
                if (!score.Content.TryGetValue(new StaffMeasureKey(staffBase + staff, measure), out StaffMeasure? content))
                {
                    continue;
                }

                foreach (Voice voice in content.Voices)
                {
                    if (voice.Number == 1 || voice.Events.Any(e => e is Chord))
                    {
                        numbers.Add(voice.Number);
                    }
                }
            }

            if (numbers.Count == 0)
            {
                numbers.Add(1);
            }

            foreach (int number in numbers)
            {
                voices.Add((staff, number));
            }
        }

        Dictionary<(int Staff, int Voice), HashSet<Pitch>> openTies = [];
        TimeSignature? lastTime = null;
        KeySignature? lastKey = null;
        for (int measureIndex = 0; measureIndex < score.Measures.Length; measureIndex++)
        {
            Measure measure = score.Measures[measureIndex];
            XElement measureElement = new("measure", new XAttribute("number", measure.Number.ToString(CultureInfo.InvariantCulture)));
            bool first = measureIndex == 0;
            if (first || lastTime != measure.TimeSignature || lastKey != measure.KeySignature)
            {
                measureElement.Add(BuildAttributes(instrument, measure, divisions, first,
                    first || lastKey != measure.KeySignature, first || lastTime != measure.TimeSignature));
            }

            lastTime = measure.TimeSignature;
            lastKey = measure.KeySignature;
            bool firstList = true;
            foreach ((int staff, int voiceNumber) in voices)
            {
                if (!firstList)
                {
                    measureElement.Add(new XElement("backup",
                        new XElement("duration", ToDivisions(measure.TimeSignature.Length, divisions))));
                }

                firstList = false;
                if (!openTies.TryGetValue((staff, voiceNumber), out HashSet<Pitch>? ties))
                {
                    ties = [];
                    openTies[(staff, voiceNumber)] = ties;
                }

                Voice? voice = score.Content.TryGetValue(new StaffMeasureKey(staffBase + staff, measureIndex), out StaffMeasure? content)
                    ? content.Voices.FirstOrDefault(v => v.Number == voiceNumber) : null;
                if (voice is null)
                {
                    measureElement.Add(new XElement("forward",
                        new XElement("duration", ToDivisions(measure.TimeSignature.Length, divisions))));
                    continue;
                }

                foreach (MusicEvent musicEvent in voice.Events)
                {
                    AddEvent(measureElement, musicEvent, staff, voiceNumber, staffCount, divisions, ties);
                }
            }

            part.Add(measureElement);
        }

        return part;
    }

    private static XElement BuildAttributes(Instrument instrument, Measure measure, int divisions,
        bool first, bool includeKey, bool includeTime)
    {
        XElement attributes = new("attributes");
        if (first)
        {
            attributes.Add(new XElement("divisions", divisions));
        }

        if (includeKey)
        {
            attributes.Add(new XElement("key", new XElement("fifths", measure.KeySignature.Fifths)));
        }

        if (includeTime)
        {
            attributes.Add(new XElement("time",
                new XElement("beats", measure.TimeSignature.Numerator),
                new XElement("beat-type", measure.TimeSignature.Denominator)));
        }

        if (first)
        {
            if (instrument.Staves.Length > 1)
            {
                attributes.Add(new XElement("staves", instrument.Staves.Length));
            }

            for (int staff = 0; staff < instrument.Staves.Length; staff++)
            {
                (string sign, int line) = instrument.Staves[staff].InitialClef switch
                {
                    Clef.Bass => ("F", 4),
                    Clef.Alto => ("C", 3),
                    Clef.Tenor => ("C", 4),
                    _ => ("G", 2),
                };
                XElement clef = new("clef", new XElement("sign", sign), new XElement("line", line));
                if (instrument.Staves.Length > 1)
                {
                    clef.Add(new XAttribute("number", staff + 1));
                }

                attributes.Add(clef);
            }
        }

        return attributes;
    }

    private static void AddEvent(XElement measure, MusicEvent musicEvent, int staff, int voiceNumber,
        int staffCount, int divisions, HashSet<Pitch> openTies)
    {
        string voiceLabel = (staff * 4 + voiceNumber).ToString(CultureInfo.InvariantCulture);
        int duration = ToDivisions(musicEvent.Duration.Length, divisions);
        if (musicEvent is Rest)
        {
            measure.Add(BuildNote(new XElement("rest"), duration, musicEvent.Duration, voiceLabel, staff, staffCount, false, false, false));
            openTies.Clear();
            return;
        }

        Chord chord = (Chord)musicEvent;
        HashSet<Pitch> nextTies = [];
        for (int index = 0; index < chord.Notes.Length; index++)
        {
            Note note = chord.Notes[index];
            bool stop = openTies.Contains(note.Pitch);
            XElement pitch = new("pitch", new XElement("step", note.Pitch.Step.ToString()));
            if (note.Pitch.Alter != 0)
            {
                pitch.Add(new XElement("alter", note.Pitch.Alter));
            }

            pitch.Add(new XElement("octave", note.Pitch.Octave));
            measure.Add(BuildNote(pitch, duration, chord.Duration, voiceLabel, staff, staffCount, index > 0, stop, note.TiedToNext));
            if (note.TiedToNext)
            {
                nextTies.Add(note.Pitch);
            }
        }

        openTies.Clear();
        openTies.UnionWith(nextTies);
    }

    private static XElement BuildNote(XElement content, int duration, Duration notated, string voice,
        int staff, int staffCount, bool isChord, bool tieStop, bool tieStart)
    {
        XElement note = new("note");
        if (isChord)
        {
            note.Add(new XElement("chord"));
        }

        note.Add(content, new XElement("duration", duration));
        if (tieStop)
        {
            note.Add(new XElement("tie", new XAttribute("type", "stop")));
        }

        if (tieStart)
        {
            note.Add(new XElement("tie", new XAttribute("type", "start")));
        }

        note.Add(new XElement("voice", voice), new XElement("type", TypeName(notated.Value)));
        for (int dot = 0; dot < notated.Dots; dot++)
        {
            note.Add(new XElement("dot"));
        }

        if (staffCount > 1)
        {
            note.Add(new XElement("staff", staff + 1));
        }

        if (tieStop || tieStart)
        {
            XElement notations = new("notations");
            if (tieStop)
            {
                notations.Add(new XElement("tied", new XAttribute("type", "stop")));
            }

            if (tieStart)
            {
                notations.Add(new XElement("tied", new XAttribute("type", "start")));
            }

            note.Add(notations);
        }

        return note;
    }

    private static string TypeName(NoteValue value) => value switch
    {
        NoteValue.Whole => "whole",
        NoteValue.Half => "half",
        NoteValue.Quarter => "quarter",
        NoteValue.Eighth => "eighth",
        NoteValue.Sixteenth => "16th",
        NoteValue.ThirtySecond => "32nd",
        NoteValue.SixtyFourth => "64th",
        NoteValue.HundredTwentyEighth => "128th",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static int ToDivisions(Fraction length, int divisions)
    {
        Fraction scaled = length * new Fraction(4L * divisions, 1);
        return checked((int)(scaled.Num / scaled.Den));
    }

    // The smallest divisions-per-quarter value that makes every event duration an integer.
    private static int ChooseDivisions(Score score)
    {
        long divisions = 1;
        foreach (StaffMeasure content in score.Content.Values)
        {
            foreach (Voice voice in content.Voices)
            {
                foreach (MusicEvent musicEvent in voice.Events)
                {
                    Fraction quarter = musicEvent.Duration.Length * new Fraction(4, 1);
                    divisions = Lcm(divisions, quarter.Den);
                }
            }
        }

        foreach (Measure measure in score.Measures)
        {
            divisions = Lcm(divisions, (measure.TimeSignature.Length * new Fraction(4, 1)).Den);
        }

        return checked((int)divisions);
    }

    private static long Lcm(long a, long b)
    {
        long x = a;
        long y = b;
        while (y != 0)
        {
            (x, y) = (y, x % y);
        }

        return a / x * b;
    }

    private static string PartId(int index) => string.Create(CultureInfo.InvariantCulture, $"P{index + 1}");
}
