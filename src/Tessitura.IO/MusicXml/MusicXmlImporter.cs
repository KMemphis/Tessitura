using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Tessitura.Core;
using Tessitura.Editing;

namespace Tessitura.IO.MusicXml;

/// <summary>Reports something the importer skipped, guessed or approximated.</summary>
/// <param name="Location">Where it happened, such as "part P1, measure 3".</param>
/// <param name="Message">What was done about it.</param>
public sealed record MusicXmlImportWarning(string Location, string Message);

/// <summary>The imported score and everything that was lost or approximated on the way.</summary>
/// <param name="Score">The imported score.</param>
/// <param name="Warnings">The notices to show the user.</param>
public sealed record MusicXmlImportResult(Score Score, ImmutableArray<MusicXmlImportWarning> Warnings);

/// <summary>Imports MusicXML score-partwise files (.musicxml, .xml and compressed .mxl), tolerating errors.</summary>
public static class MusicXmlImporter
{
    /// <summary>Imports a file.</summary>
    /// <param name="path">The .musicxml, .xml or .mxl path.</param>
    /// <returns>The score with its warnings.</returns>
    /// <exception cref="InvalidDataException">The file is not readable MusicXML.</exception>
    public static MusicXmlImportResult Import(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            return Import(LoadDocument(path));
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"The file is not well-formed XML: {exception.Message}", exception);
        }
    }

    /// <summary>Imports an already parsed document.</summary>
    /// <param name="document">A score-partwise document.</param>
    /// <returns>The score with its warnings.</returns>
    /// <exception cref="InvalidDataException">The document is not a score-partwise with at least one part.</exception>
    public static MusicXmlImportResult Import(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Importer importer = new();
        return importer.Run(document);
    }

    private static XDocument LoadDocument(string path)
    {
        if (path.EndsWith(".mxl", StringComparison.OrdinalIgnoreCase))
        {
            using ZipArchive archive = ZipFile.OpenRead(path);
            ZipArchiveEntry container = archive.GetEntry("META-INF/container.xml")
                ?? throw new InvalidDataException("The .mxl has no META-INF/container.xml.");
            string rootPath;
            using (Stream stream = container.Open())
            {
                rootPath = Parse(stream).Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")
                    ?.Attribute("full-path")?.Value ?? throw new InvalidDataException("The .mxl has no root file.");
            }

            ZipArchiveEntry root = archive.GetEntry(rootPath)
                ?? throw new InvalidDataException($"The .mxl has no {rootPath}.");
            using Stream rootStream = root.Open();
            return Parse(rootStream);
        }

        using FileStream file = File.OpenRead(path);
        return Parse(file);
    }

    private static XDocument Parse(Stream stream)
    {
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader);
    }

    private sealed record RawNote(
        int Measure, int Staff, string Voice, Fraction Onset, Fraction Duration, Duration? Notated,
        bool IsRest, bool IsChordMember, Pitch? Pitch, bool TieStart);

    private sealed class Importer
    {
        private readonly List<MusicXmlImportWarning> _warnings = [];
        private readonly HashSet<string> _seenOnce = [];

        public MusicXmlImportResult Run(XDocument document)
        {
            XElement root = document.Root ?? throw new InvalidDataException("The document is empty.");
            if (root.Name.LocalName != "score-partwise")
            {
                throw new InvalidDataException($"Only score-partwise is supported, not {root.Name.LocalName}.");
            }

            List<XElement> parts = [.. root.Elements().Where(e => e.Name.LocalName == "part")];
            if (parts.Count == 0)
            {
                throw new InvalidDataException("The score has no parts.");
            }

            Dictionary<string, string> partNames = ReadPartNames(root);
            List<PartData> partData = [];
            foreach (XElement part in parts)
            {
                string id = part.Attribute("id")?.Value ?? $"part {partData.Count + 1}";
                partData.Add(ReadPart(part, id, partNames.GetValueOrDefault(id, id)));
            }

            int measureCount = partData.Max(p => p.MeasureCount);
            TimeSignature[] times = new TimeSignature[measureCount];
            KeySignature[] keys = new KeySignature[measureCount];
            TimeSignature currentTime = new(4, 4);
            KeySignature currentKey = default;
            for (int m = 0; m < measureCount; m++)
            {
                foreach (PartData part in partData)
                {
                    if (part.Times.TryGetValue(m, out TimeSignature time))
                    {
                        currentTime = time;
                        break;
                    }
                }

                foreach (PartData part in partData)
                {
                    if (part.Keys.TryGetValue(m, out KeySignature key))
                    {
                        currentKey = key;
                        break;
                    }
                }

                times[m] = currentTime;
                keys[m] = currentKey;
            }

            Fraction[] starts = new Fraction[measureCount + 1];
            for (int m = 0; m < measureCount; m++)
            {
                starts[m + 1] = starts[m] + times[m].Length;
            }

            ImmutableArray<Instrument>.Builder instruments = ImmutableArray.CreateBuilder<Instrument>();
            foreach (PartData part in partData)
            {
                ImmutableArray<Staff>.Builder staves = ImmutableArray.CreateBuilder<Staff>();
                for (int s = 1; s <= part.StaffCount; s++)
                {
                    staves.Add(new Staff(part.StaffCount == 1 ? part.Name : $"{part.Name} {s}",
                        part.Clefs.GetValueOrDefault(s, Clef.Treble)));
                }

                instruments.Add(new Instrument(part.Name, staves.ToImmutable()));
            }

            ImmutableArray<Measure>.Builder measures = ImmutableArray.CreateBuilder<Measure>(measureCount);
            for (int m = 0; m < measureCount; m++)
            {
                measures.Add(new Measure(m + 1, times[m], keys[m]));
            }

            int staffTotal = partData.Sum(p => p.StaffCount);
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
                ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
            for (int staff = 0; staff < staffTotal; staff++)
            {
                for (int m = 0; m < measureCount; m++)
                {
                    content[new StaffMeasureKey(staff, m)] = new StaffMeasure(
                        [new Voice(1, NewScoreFactory.CreateMeasureRests(times[m]))]);
                }
            }

            (string title, string composer) = ReadCredits(root);
            Score score = new(new ScoreMetadata(title, composer), instruments.ToImmutable(),
                measures.MoveToImmutable(), content.ToImmutable());

            int staffOffset = 0;
            foreach (PartData part in partData)
            {
                score = PlaceNotes(score, part, staffOffset, starts, times);
                staffOffset += part.StaffCount;
            }

            return new MusicXmlImportResult(score, [.. _warnings]);
        }

        private Score PlaceNotes(Score score, PartData part, int staffOffset, Fraction[] starts, TimeSignature[] times)
        {
            Dictionary<(int Staff, string Voice), int> voiceNumbers = [];
            Dictionary<(int Staff, int Voice), List<List<MusicEvent>>> raw = [];
            Dictionary<(int Staff, int Voice), Fraction> ends = [];
            int measureCount = starts.Length - 1;
            foreach (RawNote note in part.Notes)
            {
                int staff = Math.Clamp(note.Staff, 1, part.StaffCount) - 1 + staffOffset;
                if (!voiceNumbers.TryGetValue((staff, note.Voice), out int voiceNumber))
                {
                    int used = voiceNumbers.Keys.Count(k => k.Staff == staff);
                    if (used >= 4)
                    {
                        Warn($"{part.Id}, measure {note.Measure + 1}", $"voice {note.Voice} dropped: a staff holds at most four voices");
                        voiceNumbers[(staff, note.Voice)] = 0;
                        continue;
                    }

                    voiceNumber = used + 1;
                    voiceNumbers[(staff, note.Voice)] = voiceNumber;
                }

                if (voiceNumber == 0 || note.Measure >= measureCount)
                {
                    continue;
                }

                (int, int) key = (staff, voiceNumber);
                if (!raw.TryGetValue(key, out List<List<MusicEvent>>? perMeasure))
                {
                    perMeasure = [.. Enumerable.Range(0, measureCount).Select(_ => new List<MusicEvent>())];
                    raw[key] = perMeasure;
                }

                Duration? duration = ToDuration(note, times[note.Measure], part.Id);
                if (duration is not Duration notated)
                {
                    continue;
                }

                Fraction absolute = starts[note.Measure] + note.Onset;
                List<MusicEvent> list = perMeasure[note.Measure];
                if (note.IsChordMember && !note.IsRest && list.Count > 0 && list[^1] is Chord last &&
                    last.Onset == note.Onset && note.Pitch is Pitch chordPitch)
                {
                    list[^1] = last with { Notes = last.Notes.Add(new Note(chordPitch, note.TieStart)) };
                    continue;
                }

                if (ends.TryGetValue(key, out Fraction end) && absolute < end)
                {
                    Warn($"{part.Id}, measure {note.Measure + 1}", "overlapping notes in one voice: extra note dropped");
                    continue;
                }

                if (note.IsRest)
                {
                    list.Add(new Rest(new EventId(Guid.NewGuid()), note.Onset, notated));
                }
                else if (note.Pitch is Pitch pitch)
                {
                    list.Add(new Chord(new EventId(Guid.NewGuid()), note.Onset, notated,
                        [new Note(pitch, note.TieStart)], StemDirection.Auto));
                }

                ends[key] = absolute + notated.Length;
            }

            foreach (((int staff, int voice), List<List<MusicEvent>> perMeasure) in raw.OrderBy(e => e.Key.Staff).ThenBy(e => e.Key.Voice))
            {
                if (perMeasure.All(l => l.Count == 0))
                {
                    continue;
                }

                ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content = score.Content.ToBuilder();
                for (int m = 0; m < perMeasure.Count; m++)
                {
                    StaffMeasureKey key = new(staff, m);
                    StaffMeasure staffMeasure = content[key];
                    Voice replaced = new(voice, [.. perMeasure[m]]);
                    int index = staffMeasure.Voices.ToList().FindIndex(v => v.Number == voice);
                    content[key] = staffMeasure with
                    {
                        Voices = index >= 0 ? staffMeasure.Voices.SetItem(index, replaced) : staffMeasure.Voices.Add(replaced),
                    };
                }

                Score candidate = score with { Content = content.ToImmutable() };
                try
                {
                    score = new NormalizeVoiceCommand().Apply(candidate, new EditContext(staff, 0, voice));
                }
                catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException
                    or ArgumentOutOfRangeException or ArgumentException or OverflowException)
                {
                    Warn(part.Id, $"voice {voice} of staff {staff - staffOffset + 1} could not be laid out on the measure grid and was left empty: {exception.Message}");
                }
            }

            return score;
        }

        private Duration? ToDuration(RawNote note, TimeSignature time, string partId)
        {
            Fraction length = note.Duration;
            if (note.IsRest && length == Fraction.Zero)
            {
                length = time.Length;
            }

            foreach (int value in new[] { 1, 2, 4, 8, 16, 32, 64, 128 })
            {
                for (int dots = 0; dots <= 3; dots++)
                {
                    Fraction candidate = new(((1L << (dots + 1)) - 1), (long)value << dots);
                    if (candidate == length)
                    {
                        return new Duration((NoteValue)value, dots);
                    }
                }
            }

            if (note.Notated is Duration notated)
            {
                WarnOnce($"{partId}: tuplet", $"{partId}: irregular durations (tuplets) are written with their notated value; their exact timing is not kept");
                return notated;
            }

            WarnOnce($"{partId}: duration", $"{partId}: a duration of {length} whole notes has no notated value; note skipped");
            return null;
        }

        private PartData ReadPart(XElement part, string id, string name)
        {
            PartData data = new(id, name);
            int divisions = 0;
            int measureIndex = 0;
            foreach (XElement measure in part.Elements().Where(e => e.Name.LocalName == "measure"))
            {
                Fraction cursor = Fraction.Zero;
                Fraction lastOnset = Fraction.Zero;
                string where = $"{id}, measure {measureIndex + 1}";
                foreach (XElement element in measure.Elements())
                {
                    switch (element.Name.LocalName)
                    {
                        case "attributes":
                            ReadAttributes(element, data, measureIndex, ref divisions, where);
                            break;
                        case "backup":
                            cursor -= new Fraction(Math.Max(0, ReadLong(element, "duration")), Math.Max(1, divisions) * 4L);
                            break;
                        case "forward":
                            cursor += new Fraction(Math.Max(0, ReadLong(element, "duration")), Math.Max(1, divisions) * 4L);
                            break;
                        case "note":
                            ReadNote(element, data, measureIndex, Math.Max(1, divisions), ref cursor, ref lastOnset, where);
                            break;
                    }
                }

                measureIndex++;
            }

            data.MeasureCount = measureIndex;
            return data;
        }

        private void ReadAttributes(XElement attributes, PartData data, int measureIndex, ref int divisions, string where)
        {
            foreach (XElement element in attributes.Elements())
            {
                switch (element.Name.LocalName)
                {
                    case "divisions" when int.TryParse(element.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) && d > 0:
                        divisions = d;
                        break;
                    case "staves" when int.TryParse(element.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s):
                        data.StaffCount = Math.Max(data.StaffCount, Math.Clamp(s, 1, 16));
                        break;
                    case "key":
                        if (element.Elements().FirstOrDefault(e => e.Name.LocalName == "fifths") is { } fifths &&
                            int.TryParse(fifths.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int f))
                        {
                            data.Keys[measureIndex] = new KeySignature(Math.Clamp(f, -7, 7));
                            if (f is < -7 or > 7)
                            {
                                Warn(where, $"key signature with {f} accidentals clamped to seven");
                            }
                        }
                        else
                        {
                            Warn(where, "non-traditional key signature ignored");
                        }

                        break;
                    case "time":
                        TimeSignature? time = ReadTime(element);
                        if (time is TimeSignature parsed)
                        {
                            data.Times[measureIndex] = parsed;
                        }
                        else
                        {
                            Warn(where, "unsupported time signature ignored");
                        }

                        break;
                    case "clef":
                        ReadClef(element, data, measureIndex, where);
                        break;
                }
            }
        }

        private TimeSignature? ReadTime(XElement time)
        {
            List<string> beats = [.. time.Elements().Where(e => e.Name.LocalName == "beats").Select(e => e.Value.Trim())];
            List<string> types = [.. time.Elements().Where(e => e.Name.LocalName == "beat-type").Select(e => e.Value.Trim())];
            if (beats.Count == 0 || beats.Count != types.Count)
            {
                return null;
            }

            if (beats.Count == 1 && int.TryParse(beats[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int plain) &&
                int.TryParse(types[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int plainType) &&
                plain > 0 && IsDenominator(plainType))
            {
                return new TimeSignature(plain, plainType);
            }

            // Additive or mixed meters (2+3/8, 3/4+2/8) become one meter of the same total length.
            Fraction total = Fraction.Zero;
            int denominator = 1;
            for (int i = 0; i < beats.Count; i++)
            {
                if (!int.TryParse(types[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type) || !IsDenominator(type))
                {
                    return null;
                }

                foreach (string part in beats[i].Split('+'))
                {
                    if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count <= 0)
                    {
                        return null;
                    }

                    total += new Fraction(count, type);
                }

                denominator = Math.Max(denominator, type);
            }

            Fraction scaled = total * new Fraction(denominator, 1);
            if (scaled.Den != 1 || scaled.Num <= 0 || scaled.Num > int.MaxValue)
            {
                return null;
            }

            WarnOnce("time: additive", "additive or mixed time signatures are written as a single meter of the same length");
            return new TimeSignature((int)scaled.Num, denominator);
        }

        private static bool IsDenominator(int value) => value is 1 or 2 or 4 or 8 or 16 or 32 or 64 or 128;

        private void ReadClef(XElement clef, PartData data, int measureIndex, string where)
        {
            int number = int.TryParse(clef.Attribute("number")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 1;
            string sign = clef.Elements().FirstOrDefault(e => e.Name.LocalName == "sign")?.Value.Trim() ?? "G";
            string line = clef.Elements().FirstOrDefault(e => e.Name.LocalName == "line")?.Value.Trim() ?? "";
            Clef? mapped = (sign, line) switch
            {
                ("G", "2" or "") => Clef.Treble,
                ("F", "4" or "") => Clef.Bass,
                ("C", "3" or "") => Clef.Alto,
                ("C", "4") => Clef.Tenor,
                _ => null,
            };
            if (mapped is null)
            {
                Warn(where, $"clef {sign}{line} is not supported; treble clef used");
            }

            if (measureIndex == 0 || !data.Clefs.ContainsKey(number))
            {
                if (measureIndex > 0)
                {
                    Warn(where, "clef first given after the first measure; used as the initial clef");
                }

                data.Clefs[number] = mapped ?? Clef.Treble;
            }
            else if (data.Clefs[number] != (mapped ?? Clef.Treble))
            {
                WarnOnce($"{data.Id}: clef change", $"{data.Id}: clef changes inside the piece are not kept");
            }
        }

        private void ReadNote(XElement note, PartData data, int measureIndex, int divisions,
            ref Fraction cursor, ref Fraction lastOnset, string where)
        {
            bool isChord = note.Elements().Any(e => e.Name.LocalName == "chord");
            bool isGrace = note.Elements().Any(e => e.Name.LocalName == "grace");
            Fraction onset = isChord ? lastOnset : cursor;
            Fraction duration = isGrace ? Fraction.Zero
                : new Fraction(Math.Max(0, ReadLong(note, "duration")), divisions * 4L);
            if (!isChord && !isGrace)
            {
                lastOnset = onset;
                cursor += duration;
            }

            if (isGrace)
            {
                WarnOnce($"{data.Id}: grace", $"{data.Id}: grace notes are not supported yet and were skipped");
                return;
            }

            bool isRest = note.Elements().Any(e => e.Name.LocalName == "rest");
            Pitch? pitch = null;
            if (!isRest)
            {
                if (note.Elements().FirstOrDefault(e => e.Name.LocalName == "unpitched") is not null)
                {
                    WarnOnce($"{data.Id}: unpitched", $"{data.Id}: unpitched (percussion) notes are not supported yet and were skipped");
                    return;
                }

                pitch = ReadPitch(note, where);
                if (pitch is null)
                {
                    return;
                }
            }

            int staff = int.TryParse(note.Elements().FirstOrDefault(e => e.Name.LocalName == "staff")?.Value.Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) ? s : 1;
            data.StaffCount = Math.Max(data.StaffCount, staff);
            string voice = note.Elements().FirstOrDefault(e => e.Name.LocalName == "voice")?.Value.Trim() ?? "1";
            // Some exporters write only the notation <tied>, others only the sound <tie>.
            bool tieStart = note.Elements().Any(e => e.Name.LocalName == "tie" && e.Attribute("type")?.Value == "start") ||
                note.Elements().Where(e => e.Name.LocalName == "notations").Elements()
                    .Any(e => e.Name.LocalName == "tied" && e.Attribute("type")?.Value == "start");
            data.Notes.Add(new RawNote(measureIndex, staff, voice, onset,
                duration, ReadNotated(note), isRest, isChord, pitch, tieStart));
        }

        private Pitch? ReadPitch(XElement note, string where)
        {
            XElement? pitch = note.Elements().FirstOrDefault(e => e.Name.LocalName == "pitch");
            if (pitch is null)
            {
                Warn(where, "note without pitch skipped");
                return null;
            }

            string stepText = pitch.Elements().FirstOrDefault(e => e.Name.LocalName == "step")?.Value.Trim() ?? "";
            if (!Enum.TryParse(stepText, ignoreCase: false, out Step step) || stepText.Length != 1 ||
                !int.TryParse(pitch.Elements().FirstOrDefault(e => e.Name.LocalName == "octave")?.Value.Trim(),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out int octave))
            {
                Warn(where, "note with an invalid pitch skipped");
                return null;
            }

            int alter = 0;
            string? alterText = pitch.Elements().FirstOrDefault(e => e.Name.LocalName == "alter")?.Value.Trim();
            if (alterText is not null && double.TryParse(alterText, NumberStyles.Float, CultureInfo.InvariantCulture, out double a))
            {
                alter = (int)Math.Round(a, MidpointRounding.AwayFromZero);
                if (Math.Abs(a - alter) > 1e-9 || alter is < -2 or > 2)
                {
                    WarnOnce($"{where}: alter", $"{where}: alteration {alterText} approximated to the nearest supported semitone");
                    alter = Math.Clamp(alter, -2, 2);
                }
            }

            return new Pitch(step, alter, octave);
        }

        private static Duration? ReadNotated(XElement note)
        {
            string? type = note.Elements().FirstOrDefault(e => e.Name.LocalName == "type")?.Value.Trim();
            NoteValue? value = type switch
            {
                "whole" => NoteValue.Whole, "half" => NoteValue.Half, "quarter" => NoteValue.Quarter,
                "eighth" => NoteValue.Eighth, "16th" => NoteValue.Sixteenth, "32nd" => NoteValue.ThirtySecond,
                "64th" => NoteValue.SixtyFourth, "128th" => NoteValue.HundredTwentyEighth, _ => null,
            };
            if (value is null)
            {
                return null;
            }

            int dots = Math.Min(3, note.Elements().Count(e => e.Name.LocalName == "dot"));
            return new Duration(value.Value, dots);
        }

        private static long ReadLong(XElement element, string child) =>
            long.TryParse(element.Elements().FirstOrDefault(e => e.Name.LocalName == child)?.Value.Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;

        private static Dictionary<string, string> ReadPartNames(XElement root)
        {
            Dictionary<string, string> names = [];
            XElement? list = root.Elements().FirstOrDefault(e => e.Name.LocalName == "part-list");
            if (list is null)
            {
                return names;
            }

            foreach (XElement scorePart in list.Elements().Where(e => e.Name.LocalName == "score-part"))
            {
                string? id = scorePart.Attribute("id")?.Value;
                string? name = scorePart.Elements().FirstOrDefault(e => e.Name.LocalName == "part-name")?.Value.Trim();
                if (id is not null && !string.IsNullOrEmpty(name))
                {
                    names[id] = name;
                }
            }

            return names;
        }

        private static (string Title, string Composer) ReadCredits(XElement root)
        {
            string title = root.Elements().FirstOrDefault(e => e.Name.LocalName == "work")?.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "work-title")?.Value.Trim()
                ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "movement-title")?.Value.Trim() ?? "";
            string composer = root.Elements().FirstOrDefault(e => e.Name.LocalName == "identification")?.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "creator" && e.Attribute("type")?.Value == "composer")?.Value.Trim() ?? "";
            return (title, composer);
        }

        private void Warn(string location, string message) => _warnings.Add(new MusicXmlImportWarning(location, message));

        private void WarnOnce(string key, string message)
        {
            if (_seenOnce.Add(key))
            {
                _warnings.Add(new MusicXmlImportWarning(key.Split(':')[0], message));
            }
        }
    }

    private sealed class PartData(string id, string name)
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public int StaffCount { get; set; } = 1;

        public int MeasureCount { get; set; }

        public Dictionary<int, TimeSignature> Times { get; } = [];

        public Dictionary<int, KeySignature> Keys { get; } = [];

        public Dictionary<int, Clef> Clefs { get; } = [];

        public List<RawNote> Notes { get; } = [];
    }
}
