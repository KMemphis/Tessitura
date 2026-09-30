using System.Collections.Immutable;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Tessitura.Core;
using Tessitura.IO.Midi;
using Tessitura.IO.MusicXml;
using Tessitura.IO.Tests.MusicXml;
using Xunit;
using Chord = Tessitura.Core.Chord;
using TimeSignature = Tessitura.Core.TimeSignature;
using MidiNote = Melanchall.DryWetMidi.Interaction.Note;
using ScoreNote = Tessitura.Core.Note;

namespace Tessitura.IO.Tests.Midi;

public sealed class MidiFileTests
{
    [Fact]
    public void ExportedScoreReimportedKeepsPitchesAndRhythmsForTheWholeInScopeCorpus()
    {
        List<string> problems = [];
        int compared = 0;
        foreach (CorpusFile file in MusicXmlCorpus.Enumerate().Where(f => f.InScope))
        {
            Score original = MusicXmlImporter.Import(file.Path).Score;
            List<string> expected = Sounding(original);
            if (expected.Count == 0)
            {
                continue;
            }

            Score again = MidiImporter.Import(MidiExporter.ToMidiFile(original), "", new MidiImportOptions(GridDivisor: 128)).Score;
            List<string> actual = Sounding(again);
            compared++;
            if (!expected.SequenceEqual(actual))
            {
                problems.Add($"{file.RelativePath}: {expected.Count} sounding notes, {actual.Count} after the round trip, {expected.Except(actual).Count()} lost");
            }
        }

        Assert.True(compared >= 30, $"only {compared} files compared");
        Assert.Empty(problems);
    }

    [Fact]
    public void ExportsStandardMidiFileTypeOneWithConductorAndInstrumentTracks()
    {
        Score score = CreateScore();

        using MemoryStream stream = new();
        MidiExporter.ToMidiFile(score, tempo: 96).Write(stream);
        stream.Position = 0;
        MidiFile file = MidiFile.Read(stream);

        Assert.Equal(MidiFileFormat.MultiTrack, file.OriginalFormat);
        Assert.Equal(new TicksPerQuarterNoteTimeDivision(960), file.TimeDivision);
        List<TrackChunk> tracks = [.. file.GetTrackChunks()];
        Assert.Equal(3, tracks.Count);
        Assert.Contains(tracks[0].Events, e => e is SetTempoEvent t && t.MicrosecondsPerQuarterNote == 625_000);
        Assert.Contains(tracks[0].Events, e => e is TimeSignatureEvent t && t.Numerator == 3 && t.Denominator == 4);
        Assert.Contains(tracks[0].Events, e => e is KeySignatureEvent k && k.Key == -2);
        Assert.Equal("Flauta", tracks[1].Events.OfType<SequenceTrackNameEvent>().Single().Text);
        Assert.Equal("Fagot", tracks[2].Events.OfType<SequenceTrackNameEvent>().Single().Text);

        List<MidiNote> flute = [.. tracks[1].GetNotes().OrderBy(n => n.Time)];
        // C5 quarter, then a B-flat 4 half note tied into the next measure's dotted half: one sounding note.
        Assert.Equal(2, flute.Count);
        Assert.Equal((0L, 960L, 72), (flute[0].Time, flute[0].Length, (int)flute[0].NoteNumber));
        Assert.Equal((960L, 1920L + 2880L, 70), (flute[1].Time, flute[1].Length, (int)flute[1].NoteNumber));
        Assert.All(tracks[1].GetNotes(), n => Assert.Equal(0, n.Channel));
        Assert.All(tracks[2].GetNotes(), n => Assert.Equal(1, n.Channel));
    }

    [Fact]
    public void ImportQuantizesHumanizedTimingToTheChosenGrid()
    {
        MidiFile file = CreateFile(
            (60, 5, 470), (62, 490, 480), (64, 975, 950), (65, 1930, 240), (67, 2410, 15));

        Score sixteenths = MidiImporter.Import(file, "q", new MidiImportOptions(GridDivisor: 16)).Score;
        Score quarters = MidiImporter.Import(file, "q", new MidiImportOptions(GridDivisor: 4)).Score;

        Assert.Equal(new List<string> { "0 1/4 C4", "1/4 1/4 D4", "1/2 1/2 E4", "1 1/8 F4", "5/4 1/16 G4" }, Rhythm(sixteenths));
        Assert.Equal(new List<string> { "0 1/4 C4", "1/4 1/4 D4", "1/2 1/2 E4", "1 1/4 F4", "5/4 1/4 G4" }, Rhythm(quarters));
    }

    [Fact]
    public void ImportSpellsBlackKeysByTheKeySignatureAndSplitsLongNotesWithTies()
    {
        MidiFile sharpFile = CreateFile((61, 0, 960 * 5));
        MidiFile flatFile = CreateFile((61, 0, 960));
        TrackChunk conductor = new();
        conductor.Events.Add(new KeySignatureEvent(-3, 0));
        flatFile.Chunks.Insert(0, conductor);

        Score sharp = MidiImporter.Import(sharpFile).Score;
        Score flat = MidiImporter.Import(flatFile).Score;

        Chord[] sharpChords = [.. AllChords(sharp)];
        Assert.All(sharpChords, c => Assert.Equal(new Pitch(Step.C, 1, 4), c.Notes[0].Pitch));
        // Five quarter notes cross the barline: 4/4 whole note tied to a quarter.
        Assert.True(sharpChords.Length >= 2);
        Assert.True(sharpChords[0].Notes[0].TiedToNext);
        Assert.False(sharpChords[^1].Notes[0].TiedToNext);
        Assert.Equal(new Pitch(Step.D, -1, 4), AllChords(flat).First().Notes[0].Pitch);
        Assert.Equal(new KeySignature(-3), flat.Measures[0].KeySignature);
    }

    [Fact]
    public void ImportSplitsOverlappingNotesIntoVoicesAndGroupsChords()
    {
        MidiFile file = CreateFile((60, 0, 1920), (64, 0, 1920), (67, 0, 960), (72, 960, 960));

        Score score = MidiImporter.Import(file).Score;

        StaffMeasure measure = score.Content[new StaffMeasureKey(0, 0)];
        Assert.True(measure.Voices.Length >= 2);
        Assert.Contains(measure.Voices.SelectMany(v => v.Events).OfType<Chord>(), c => c.Notes.Length == 2);
        Assert.All(score.Content, e => Assert.True(ScoreValidator.IsMeasureValid(score.Measures[e.Key.MeasureIndex], e.Value)));
    }

    [Fact]
    public void PercussionAndBrokenFilesAreHandledWithoutCrashing()
    {
        TrackChunk drums = new();
        using (var manager = drums.ManageNotes())
        {
            manager.Objects.Add(new MidiNote((SevenBitNumber)36, 480, 0) { Channel = (FourBitNumber)9 });
            manager.Objects.Add(new MidiNote((SevenBitNumber)60, 480, 480) { Channel = (FourBitNumber)0 });
        }

        MusicXmlImportResult result = MidiImporter.Import(new MidiFile(drums) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) });
        Assert.Contains(result.Warnings, w => w.Message.Contains("percussion", StringComparison.Ordinal));
        Assert.Single(AllChords(result.Score));

        Assert.Throws<InvalidDataException>(() => MidiImporter.Import(new MidiFile(new TrackChunk())));
        string path = Path.Combine(Path.GetTempPath(), $"tessitura-notmidi-{Guid.NewGuid():N}.mid");
        try
        {
            File.WriteAllText(path, "this is not a midi file");
            Assert.Throws<InvalidDataException>(() => MidiImporter.Import(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveWritesAReadableFileAtomically()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tessitura-midi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "a.mid");
            Score score = CreateScore();

            MidiExporter.Save(path, score);
            Score again = MidiImporter.Import(path, new MidiImportOptions(GridDivisor: 128)).Score;

            Assert.Equal(Sounding(score), Sounding(again));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Assert.Equal("a", again.Metadata.Title);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MidiFile CreateFile(params (int Note, long Time, long Length)[] notes)
    {
        TrackChunk track = new();
        using (var manager = track.ManageNotes())
        {
            foreach ((int note, long time, long length) in notes)
            {
                manager.Objects.Add(new MidiNote((SevenBitNumber)note, length, time) { Channel = (FourBitNumber)0 });
            }
        }

        return new MidiFile(track) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };
    }

    private static IEnumerable<Chord> AllChords(Score score) =>
        score.Content.OrderBy(e => e.Key.StaffIndex).ThenBy(e => e.Key.MeasureIndex)
            .SelectMany(e => e.Value.Voices).SelectMany(v => v.Events).OfType<Chord>();

    private static List<string> Rhythm(Score score)
    {
        List<string> lines = [];
        Fraction start = Fraction.Zero;
        for (int m = 0; m < score.Measures.Length; m++)
        {
            foreach (Voice voice in score.Content[new StaffMeasureKey(0, m)].Voices)
            {
                foreach (Chord chord in voice.Events.OfType<Chord>())
                {
                    Fraction onset = start + chord.Onset;
                    lines.Add($"{onset} {chord.Duration.Length} {chord.Notes[0].Pitch.Step}{chord.Notes[0].Pitch.Octave}");
                }
            }

            start += score.Measures[m].TimeSignature.Length;
        }

        return lines;
    }

    // Sounding notes as "part|midi|start|end" with tied notes merged, sorted for comparison.
    private static List<string> Sounding(Score score)
    {
        int staffBase = 0;
        List<string> keys = [];
        for (int part = 0; part < score.Instruments.Length; part++)
        {
            List<(int Midi, Fraction Start, Fraction End)> notes = [];
            for (int staff = staffBase; staff < staffBase + score.Instruments[part].Staves.Length; staff++)
            {
                for (int voiceNumber = 1; voiceNumber <= 4; voiceNumber++)
                {
                    Dictionary<int, int> open = [];
                    Fraction measureStart = Fraction.Zero;
                    for (int m = 0; m < score.Measures.Length; m++)
                    {
                        Voice? voice = score.Content[new StaffMeasureKey(staff, m)].Voices.FirstOrDefault(v => v.Number == voiceNumber);
                        if (voice is not null)
                        {
                            foreach (MusicEvent e in voice.Events)
                            {
                                Fraction start = measureStart + e.Onset;
                                Dictionary<int, int> next = [];
                                if (e is Chord chord)
                                {
                                    foreach (ScoreNote note in chord.Notes)
                                    {
                                        int midi = note.Pitch.MidiNumber;
                                        if (open.TryGetValue(midi, out int index) && notes[index].End == start)
                                        {
                                            notes[index] = notes[index] with { End = start + e.Duration.Length };
                                        }
                                        else
                                        {
                                            notes.Add((midi, start, start + e.Duration.Length));
                                            index = notes.Count - 1;
                                        }

                                        if (note.TiedToNext)
                                        {
                                            next[midi] = index;
                                        }
                                    }
                                }

                                open = next;
                            }
                        }

                        measureStart += score.Measures[m].TimeSignature.Length;
                    }
                }
            }

            keys.AddRange(notes.Select(n => $"{n.Midi}|{n.Start}|{n.End}"));
            staffBase += score.Instruments[part].Staves.Length;
        }

        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    private static Score CreateScore()
    {
        EventId Id() => new(Guid.NewGuid());
        Chord Note(Fraction onset, Duration duration, Step step, int alter, int octave, bool tie = false) =>
            new(Id(), onset, duration, [new ScoreNote(new Pitch(step, alter, octave), tie)], StemDirection.Auto);
        ImmutableDictionary<StaffMeasureKey, StaffMeasure> content = ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
            .Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1,
            [
                Note(Fraction.Zero, new Duration(NoteValue.Quarter, 0), Step.C, 0, 5),
                Note(new Fraction(1, 4), new Duration(NoteValue.Half, 0), Step.B, -1, 4, tie: true),
            ])]))
            .Add(new StaffMeasureKey(0, 1), new StaffMeasure([new Voice(1,
            [
                Note(Fraction.Zero, new Duration(NoteValue.Half, 1), Step.B, -1, 4),
            ])]))
            .Add(new StaffMeasureKey(1, 0), new StaffMeasure([new Voice(1,
                [Note(Fraction.Zero, new Duration(NoteValue.Half, 1), Step.E, -1, 2)])]))
            .Add(new StaffMeasureKey(1, 1), new StaffMeasure([new Voice(1,
                [Note(Fraction.Zero, new Duration(NoteValue.Half, 1), Step.E, -1, 2)])]));
        return new Score(new ScoreMetadata("Trio", ""),
            [new Instrument("Flauta", [new Staff("Fl")]), new Instrument("Fagot", [new Staff("Fg", Clef.Bass)])],
            [new Measure(1, new TimeSignature(3, 4), new KeySignature(-2)), new Measure(2, new TimeSignature(3, 4), new KeySignature(-2))],
            content);
    }
}
