using System.Collections.Immutable;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Tessitura.Core;
using Tessitura.Editing;
using Tessitura.IO.MusicXml;
using Chord = Tessitura.Core.Chord;
using ScoreNote = Tessitura.Core.Note;
using TimeSignature = Tessitura.Core.TimeSignature;
using MidiNote = Melanchall.DryWetMidi.Interaction.Note;

namespace Tessitura.IO.Midi;

/// <summary>Sets how the timing of a MIDI file is snapped to the notation grid.</summary>
/// <param name="GridDivisor">The grid is one <c>1/GridDivisor</c> of a whole note: 16 snaps to sixteenths.</param>
public sealed record MidiImportOptions(int GridDivisor = 16);

/// <summary>Imports Standard MIDI Files as scores, quantizing onsets and durations to a grid.</summary>
public static class MidiImporter
{
    /// <summary>Imports a file.</summary>
    /// <param name="path">The .mid path.</param>
    /// <param name="options">The quantization options; sixteenth notes by default.</param>
    /// <returns>The score with the notices to show the user.</returns>
    /// <exception cref="InvalidDataException">The file is not a readable Standard MIDI File.</exception>
    public static MusicXmlImportResult Import(string path, MidiImportOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        try
        {
            return Import(MidiFile.Read(path), Path.GetFileNameWithoutExtension(path), options);
        }
        catch (Exception exception) when (exception is MidiException or IOException && exception is not FileNotFoundException)
        {
            throw new InvalidDataException($"The file is not a readable MIDI file: {exception.Message}", exception);
        }
    }

    /// <summary>Imports an already parsed file.</summary>
    /// <param name="file">The MIDI file.</param>
    /// <param name="title">The title given to the score.</param>
    /// <param name="options">The quantization options.</param>
    /// <returns>The score with its notices.</returns>
    public static MusicXmlImportResult Import(MidiFile file, string title = "", MidiImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        options ??= new MidiImportOptions();
        if (options.GridDivisor is not (1 or 2 or 4 or 8 or 16 or 32 or 64 or 128))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The grid must be a power of two from 1 to 128.");
        }

        if (file.TimeDivision is not TicksPerQuarterNoteTimeDivision division)
        {
            throw new InvalidDataException("SMPTE time division is not supported.");
        }

        List<MusicXmlImportWarning> warnings = [];
        long ticksPerWhole = 4L * division.TicksPerQuarterNote;
        Fraction grid = new(1, options.GridDivisor);

        // Meters and key signatures, keyed by tick.
        SortedDictionary<long, TimeSignature> meters = [];
        SortedDictionary<long, KeySignature> keys = [];
        foreach (TimedEvent timed in file.GetTimedEvents())
        {
            if (timed.Event is TimeSignatureEvent time && time.Numerator > 0 && time.Denominator is 1 or 2 or 4 or 8 or 16 or 32 or 64 or 128)
            {
                meters[timed.Time] = new TimeSignature(time.Numerator, time.Denominator);
            }
            else if (timed.Event is KeySignatureEvent key && key.Key is >= -7 and <= 7)
            {
                keys[timed.Time] = new KeySignature(key.Key);
            }
        }

        // Tracks (or channels of a type-0 file) that carry pitched notes become parts.
        List<(string Name, List<MidiNote> Notes)> parts = [];
        int trackNumber = 0;
        foreach (TrackChunk track in file.GetTrackChunks())
        {
            trackNumber++;
            List<MidiNote> pitched = [];
            int skippedPercussion = 0;
            foreach (MidiNote note in track.GetNotes())
            {
                if (note.Channel == 9)
                {
                    skippedPercussion++;
                }
                else
                {
                    pitched.Add(note);
                }
            }

            if (skippedPercussion > 0)
            {
                warnings.Add(new MusicXmlImportWarning($"track {trackNumber}", $"{skippedPercussion} percussion notes (channel 10) were skipped"));
            }

            if (pitched.Count == 0)
            {
                continue;
            }

            string name = track.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text.Trim() ?? "";
            IEnumerable<IGrouping<int, MidiNote>> channels = pitched.GroupBy(n => (int)n.Channel).OrderBy(g => g.Key);
            foreach (IGrouping<int, MidiNote> channel in channels)
            {
                string partName = name.Length > 0 ? name : $"Parte {parts.Count + 1}";
                parts.Add((channels.Count() > 1 ? $"{partName} (canal {channel.Key + 1})" : partName, [.. channel]));
            }
        }

        if (parts.Count == 0)
        {
            throw new InvalidDataException("The MIDI file contains no pitched notes.");
        }

        if (keys.Count == 0)
        {
            keys[0] = default;
        }

        if (meters.Count == 0)
        {
            meters[0] = new TimeSignature(4, 4);
            warnings.Add(new MusicXmlImportWarning("file", "no time signature: 4/4 assumed"));
        }

        long lastTick = 0;
        foreach ((_, List<MidiNote> notes) in parts)
        {
            lastTick = Math.Max(lastTick, notes.Max(n => n.Time + n.Length));
        }

        // Measure grid: a meter change that does not fall on a barline applies from the next barline.
        List<Measure> measures = [];
        List<long> measureTicks = [0];
        TimeSignature currentTime = meters.First().Value;
        KeySignature currentKey = keys.First().Value;
        long tick = 0;
        while (tick < lastTick || measures.Count == 0)
        {
            // A meter or key event applies from the first barline at or after its tick.
            foreach ((long at, TimeSignature value) in meters)
            {
                if (at <= tick)
                {
                    currentTime = value;
                }
            }

            foreach ((long at, KeySignature value) in keys)
            {
                if (at <= tick)
                {
                    currentKey = value;
                }
            }

            measures.Add(new Measure(measures.Count + 1, currentTime, currentKey));
            tick += MeasureTicks(currentTime, ticksPerWhole);
            measureTicks.Add(tick);
        }

        if (meters.Count > 1)
        {
            warnings.Add(new MusicXmlImportWarning("file", "time signature changes are applied at the next barline"));
        }

        Score score = BuildSkeleton(title, parts, measures);
        for (int part = 0; part < parts.Count; part++)
        {
            score = PlaceNotes(score, part, parts[part].Notes, measures, measureTicks, ticksPerWhole, grid, warnings);
        }

        return new MusicXmlImportResult(score, [.. warnings]);
    }

    private sealed record Sounding(Fraction Start, Fraction End, int Midi);

    private static Score BuildSkeleton(string title, List<(string Name, List<MidiNote> Notes)> parts, List<Measure> measures)
    {
        ImmutableArray<Instrument>.Builder instruments = ImmutableArray.CreateBuilder<Instrument>();
        foreach ((string name, List<MidiNote> notes) in parts)
        {
            double median = notes.Select(n => (double)n.NoteNumber).Order().ElementAt(notes.Count / 2);
            instruments.Add(new Instrument(name, [new Staff(name, median < 55 ? Clef.Bass : Clef.Treble)]));
        }

        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        for (int staff = 0; staff < parts.Count; staff++)
        {
            for (int m = 0; m < measures.Count; m++)
            {
                content[new StaffMeasureKey(staff, m)] = new StaffMeasure(
                    [new Voice(1, NewScoreFactory.CreateMeasureRests(measures[m].TimeSignature))]);
            }
        }

        return new Score(new ScoreMetadata(title, ""), instruments.ToImmutable(), [.. measures], content.ToImmutable());
    }

    private static Score PlaceNotes(Score score, int staff, List<MidiNote> notes, List<Measure> measures,
        List<long> measureTicks, long ticksPerWhole, Fraction grid, List<MusicXmlImportWarning> warnings)
    {
        // Quantize, then group notes that start and end together into chords.
        SortedDictionary<(Fraction Start, Fraction End), List<int>> groups = new(Comparer<(Fraction Start, Fraction End)>.Create(
            (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End)));
        foreach (MidiNote note in notes)
        {
            Fraction start = Quantize(new Fraction(note.Time, ticksPerWhole), grid);
            Fraction end = Quantize(new Fraction(note.Time + note.Length, ticksPerWhole), grid);
            if (end <= start)
            {
                end = start + grid;
            }

            if (!groups.TryGetValue((start, end), out List<int>? pitches))
            {
                pitches = [];
                groups[(start, end)] = pitches;
            }

            if (!pitches.Contains(note.NoteNumber))
            {
                pitches.Add(note.NoteNumber);
            }
        }

        Fraction[] starts = new Fraction[measures.Count + 1];
        for (int m = 0; m < measures.Count; m++)
        {
            starts[m + 1] = starts[m] + measures[m].TimeSignature.Length;
        }

        // Voice assignment: the first of four voices that is free at the start of the group.
        Fraction[] voiceEnds = new Fraction[4];
        List<List<MusicEvent>>[] voices = [.. Enumerable.Range(0, 4).Select(_ => Enumerable.Range(0, measures.Count).Select(_ => new List<MusicEvent>()).ToList())];
        int dropped = 0;
        foreach (((Fraction start, Fraction end), List<int> pitches) in groups)
        {
            int voice = Array.FindIndex(voiceEnds, e => e <= start);
            if (voice < 0)
            {
                dropped += pitches.Count;
                continue;
            }

            voiceEnds[voice] = end;
            AddSegments(voices[voice], measures, starts, start, end, pitches);
        }

        if (dropped > 0)
        {
            warnings.Add(new MusicXmlImportWarning($"part {staff + 1}", $"{dropped} notes did not fit in four voices and were skipped"));
        }

        for (int voice = 0; voice < 4; voice++)
        {
            if (voices[voice].All(l => l.Count == 0))
            {
                continue;
            }

            Score? written = VoiceWriter.Write(score, staff, voice + 1, voices[voice], out string? failure);
            if (written is null)
            {
                warnings.Add(new MusicXmlImportWarning($"part {staff + 1}", $"voice {voice + 1} could not be laid out: {failure}"));
            }
            else
            {
                score = written;
            }
        }

        return score;
    }

    // Splits one sounding chord at barlines and into notated values, tying the pieces together.
    private static void AddSegments(List<List<MusicEvent>> perMeasure, List<Measure> measures, Fraction[] starts,
        Fraction start, Fraction end, List<int> pitches)
    {
        Fraction position = start;
        while (position < end)
        {
            int measure = 0;
            while (measure + 1 < measures.Count && starts[measure + 1] <= position)
            {
                measure++;
            }

            Fraction limit = end < starts[measure + 1] ? end : starts[measure + 1];
            if (position >= starts[measure + 1])
            {
                return;
            }

            foreach (Duration piece in Decompose(limit - position))
            {
                bool lastPiece = position + piece.Length >= end;
                Key[] keyed = [.. pitches.Order().Select(p => new Key(p))];
                ImmutableArray<ScoreNote>.Builder notes = ImmutableArray.CreateBuilder<ScoreNote>();
                foreach (Key key in keyed)
                {
                    notes.Add(new ScoreNote(Spell(key.Midi, measures[measure].KeySignature), !lastPiece));
                }

                perMeasure[measure].Add(new Chord(new EventId(Guid.NewGuid()), position - starts[measure], piece,
                    notes.ToImmutable(), StemDirection.Auto));
                position += piece.Length;
            }
        }
    }

    private readonly record struct Key(int Midi);

    // Greedy split of a length into the fewest notated values (dots up to two).
    private static List<Duration> Decompose(Fraction length)
    {
        List<Duration> pieces = [];
        Fraction remaining = length;
        while (remaining > Fraction.Zero)
        {
            Duration? best = null;
            foreach (int value in new[] { 1, 2, 4, 8, 16, 32, 64, 128 })
            {
                for (int dots = 2; dots >= 0; dots--)
                {
                    Duration candidate = new((NoteValue)value, dots);
                    if (candidate.Length <= remaining && (best is null || candidate.Length > best.Value.Length))
                    {
                        best = candidate;
                    }
                }
            }

            if (best is not Duration piece)
            {
                break;
            }

            pieces.Add(piece);
            remaining -= piece.Length;
        }

        return pieces;
    }

    private static Pitch Spell(int midi, KeySignature key)
    {
        int octave = midi / 12 - 1;
        (Step Step, int Alter)[] sharps =
            [(Step.C, 0), (Step.C, 1), (Step.D, 0), (Step.D, 1), (Step.E, 0), (Step.F, 0), (Step.F, 1), (Step.G, 0), (Step.G, 1), (Step.A, 0), (Step.A, 1), (Step.B, 0)];
        (Step Step, int Alter)[] flats =
            [(Step.C, 0), (Step.D, -1), (Step.D, 0), (Step.E, -1), (Step.E, 0), (Step.F, 0), (Step.G, -1), (Step.G, 0), (Step.A, -1), (Step.A, 0), (Step.B, -1), (Step.B, 0)];
        (Step step, int alter) = (key.Fifths >= 0 ? sharps : flats)[midi % 12];
        return new Pitch(step, alter, octave);
    }

    private static Fraction Quantize(Fraction value, Fraction grid)
    {
        Fraction steps = value / grid;
        long rounded = (2 * steps.Num + steps.Den) / (2 * steps.Den);
        return grid * new Fraction(rounded, 1);
    }

    private static long MeasureTicks(TimeSignature time, long ticksPerWhole) =>
        ticksPerWhole * time.Numerator / time.Denominator;
}
