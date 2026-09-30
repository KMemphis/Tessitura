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
            root.Add(BuildPart(score, index, staffBase, divisions, score.AttachmentList.ToLookup(a => a.Target)));
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

    private static XElement BuildPart(Score score, int instrumentIndex, int staffBase, int divisions,
        ILookup<EventId, Attachment> attachments)
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
                    if (voice.Number == 1 || voice.Events.Flatten().Any(l => l.Event is Chord))
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
                    AddTree(measureElement, musicEvent, staff, voiceNumber, staffCount, divisions, ties, Fraction.One, 1, 1, null, attachments);
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

    // Walks tuplet groups: every leaf gets the combined time modification of its enclosing groups, and the first
    // and last leaf of a group carry the tuplet start and stop marks.
    private static void AddTree(XElement measure, MusicEvent musicEvent, int staff, int voiceNumber, int staffCount, int divisions,
        HashSet<Pitch> openTies, Fraction scale, int actual, int normal, string? bracket, ILookup<EventId, Attachment> attachments)
    {
        if (musicEvent is TupletGroup group)
        {
            int childCount = group.Children.Length;
            for (int index = 0; index < childCount; index++)
            {
                string? mark = index == 0 ? "start" : index == childCount - 1 ? "stop" : null;
                AddTree(measure, group.Children[index], staff, voiceNumber, staffCount, divisions, openTies,
                    scale * group.Ratio, actual * group.Actual, normal * group.Normal, mark, attachments);
            }

            return;
        }

        List<XElement> written = AddEvent(measure, musicEvent, staff, voiceNumber, staffCount, divisions, openTies, scale, attachments);
        if (actual == normal)
        {
            return;
        }

        // The tuplet bracket mark belongs to the first or last note of the group only.
        for (int index = 0; index < written.Count; index++)
        {
            InsertTimeModification(written[index], actual, normal, index == 0 ? bracket : null);
        }
    }

    private static void InsertTimeModification(XElement note, int actual, int normal, string? bracket)
    {
        XElement modification = new("time-modification", new XElement("actual-notes", actual), new XElement("normal-notes", normal));
        XElement? after = note.Elements().LastOrDefault(e => e.Name == "dot") ?? note.Element("type");
        after!.AddAfterSelf(modification);
        if (bracket is not null)
        {
            XElement notations = note.Element("notations") ?? new XElement("notations");
            notations.Add(new XElement("tuplet", new XAttribute("type", bracket)));
            if (notations.Parent is null)
            {
                note.Add(notations);
            }
        }
    }

    private static List<XElement> AddEvent(XElement measure, MusicEvent musicEvent, int staff, int voiceNumber,
        int staffCount, int divisions, HashSet<Pitch> openTies, Fraction scale, ILookup<EventId, Attachment> attachments)
    {
        List<XElement> written = [];
        foreach (Attachment attachment in attachments[musicEvent.Id])
        {
            if (attachment is TempoAttachment tempo)
            {
                string unit = tempo.Beat.Value switch
                {
                    NoteValue.Whole => "whole", NoteValue.Half => "half", NoteValue.Eighth => "eighth",
                    NoteValue.Sixteenth => "16th", _ => "quarter",
                };
                XElement metronome = new("metronome", new XElement("beat-unit", unit));
                if (tempo.Beat.Dots > 0)
                {
                    metronome.Add(new XElement("beat-unit-dot"));
                }

                metronome.Add(new XElement("per-minute", tempo.Bpm.ToString("0.##", CultureInfo.InvariantCulture)));
                measure.Add(new XElement("direction", new XAttribute("placement", "above"), new XElement("direction-type", metronome)));
            }
            else if (attachment is TextAttachment text)
            {
                measure.Add(new XElement("direction", new XAttribute("placement", "below"),
                    new XElement("direction-type", new XElement("words", text.Text))));
            }
            else if (attachment is ChordSymbolAttachment symbol)
            {
                XElement root = new("root", new XElement("root-step", symbol.Root.ToString()));
                if (symbol.RootAlter != 0)
                {
                    root.Add(new XElement("root-alter", symbol.RootAlter));
                }

                XElement harmony = new("harmony", root, new XElement("kind", new XAttribute("text", symbol.Quality), "other"));
                if (symbol.Bass is Step bass)
                {
                    XElement bassElement = new("bass", new XElement("bass-step", bass.ToString()));
                    if (symbol.BassAlter != 0)
                    {
                        bassElement.Add(new XElement("bass-alter", symbol.BassAlter));
                    }

                    harmony.Add(bassElement);
                }

                measure.Add(harmony);
            }
            else if (attachment is DynamicAttachment dynamic)
            {
                measure.Add(new XElement("direction", new XAttribute("placement", "below"),
                    new XElement("direction-type", new XElement("dynamics", new XElement(dynamic.Level.ToString().ToLowerInvariant()))),
                    staffCount > 1 ? new XElement("staff", staff + 1) : null));
            }
        }

        string voiceLabel = (staff * 4 + voiceNumber).ToString(CultureInfo.InvariantCulture);
        int duration = ToDivisions(musicEvent.Length * scale, divisions);
        if (musicEvent is Rest)
        {
            XElement restNote = BuildNote(new XElement("rest"), duration, musicEvent.Duration, voiceLabel, staff, staffCount, false, false, false);
            measure.Add(restNote);
            written.Add(restNote);
            openTies.Clear();
            return written;
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
            XElement noteElement = BuildNote(pitch, duration, chord.Duration, voiceLabel, staff, staffCount, index > 0, stop, note.TiedToNext);
            measure.Add(noteElement);
            written.Add(noteElement);
            if (note.TiedToNext)
            {
                nextTies.Add(note.Pitch);
            }
        }

        openTies.Clear();
        openTies.UnionWith(nextTies);
        AddMarks(written, attachments[musicEvent.Id]);
        return written;
    }

    // Articulations, ornaments and fermatas go into the notations of the first note of the event.
    private static void AddMarks(List<XElement> written, IEnumerable<Attachment> marks)
    {
        List<ArticulationAttachment> articulations = [.. marks.OfType<ArticulationAttachment>()];
        if (articulations.Count == 0 || written.Count == 0)
        {
            return;
        }

        XElement note = written[0];
        XElement notations = note.Element("notations") ?? new XElement("notations");
        XElement? articulationElement = null;
        XElement? ornamentElement = null;
        foreach (ArticulationAttachment mark in articulations)
        {
            string? name = mark.Kind switch
            {
                ArticulationKind.Staccato => "staccato",
                ArticulationKind.Staccatissimo => "staccatissimo",
                ArticulationKind.Tenuto => "tenuto",
                ArticulationKind.Accent => "accent",
                ArticulationKind.Marcato => "strong-accent",
                _ => null,
            };
            if (name is not null)
            {
                articulationElement ??= new XElement("articulations");
                articulationElement.Add(new XElement(name));
            }
            else if (mark.Kind == ArticulationKind.Fermata)
            {
                notations.Add(new XElement("fermata"));
            }
            else
            {
                ornamentElement ??= new XElement("ornaments");
                ornamentElement.Add(new XElement(mark.Kind switch
                {
                    ArticulationKind.Trill => "trill-mark",
                    ArticulationKind.Mordent => "mordent",
                    _ => "turn",
                }));
            }
        }

        if (articulationElement is not null)
        {
            notations.Add(articulationElement);
        }

        if (ornamentElement is not null)
        {
            notations.Add(ornamentElement);
        }

        if (notations.Parent is null)
        {
            note.Add(notations);
        }
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
                foreach ((MusicEvent _, Fraction _, Fraction length) in voice.Events.Flatten())
                {
                    Fraction quarter = length * new Fraction(4, 1);
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
