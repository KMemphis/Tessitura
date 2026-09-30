using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.IO.Tess;
using Xunit;

namespace Tessitura.IO.Tests;

public sealed class TessFileTests
{
    [Fact]
    public void SaveAndOpenReturnAnIdenticalScore()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "piece.tess");
        Score score = CreateScore();
        Style style = CreateStyle() with { StemLength = 3.75 };

        TessFile.Save(path, score, style, "1.2.3");
        TessDocument opened = TessFile.Open(path);

        Assert.Equal(style, opened.Style);
        Assert.Equal(new TessManifest(TessMigrator.CurrentVersion, "1.2.3"), opened.Manifest);
        AssertSameScore(score, opened.Score);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void PartViewsSurviveATessRoundTripWithoutChangingTheFormatVersion()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "parts.tess");
        ScorePartView part = new("Piano", [0]);
        Score score = CreateScore() with { Parts = [part] };

        TessFile.Save(path, score, CreateStyle());
        TessDocument opened = TessFile.Open(path);

        Assert.Equal(TessMigrator.CurrentVersion, opened.Manifest.FormatVersion);
        ScorePartView saved = Assert.Single(opened.Score.Parts);
        Assert.Equal("Piano", saved.Name);
        Assert.Equal([0], saved.InstrumentIndices.ToArray());
    }

    [Fact]
    public void InstrumentTranspositionSurvivesATessRoundTripWithoutChangingTheFormatVersion()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "transposing.tess");
        Score score = CreateScore() with
        {
            Instruments = [new Instrument("Clarinet in B-flat", [new Staff("Clarinet")], new Interval(-1, -2))],
        };

        TessFile.Save(path, score, CreateStyle());
        TessDocument opened = TessFile.Open(path);

        Assert.Equal(new Interval(-1, -2), Assert.Single(opened.Score.Instruments).Transposition);
        Assert.Equal(TessMigrator.CurrentVersion, opened.Manifest.FormatVersion);
    }

    [Fact]
    public void ScoreJsonWithoutInstrumentTranspositionFieldsOpensAsUnison()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "older-instruments.tess");
        TessFile.Save(path, CreateScore(), CreateStyle());

        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.GetEntry("score.json")!;
            JsonObject node;
            using (Stream stream = entry.Open())
            {
                node = JsonNode.Parse(stream)!.AsObject();
            }

            JsonObject instrument = node["Instruments"]!.AsArray()[0]!.AsObject();
            instrument.Remove("TranspositionDiatonicSteps");
            instrument.Remove("TranspositionSemitones");
            entry.Delete();
            ZipArchiveEntry replacement = archive.CreateEntry("score.json", CompressionLevel.Optimal);
            using Stream output = replacement.Open();
            System.Text.Json.JsonSerializer.Serialize(output, node,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        Assert.Equal(default, Assert.Single(TessFile.Open(path).Score.Instruments).Transposition);
    }

    [Fact]
    public void ScoreJsonWithoutPartViewsStillOpensAtTheCurrentFormatVersion()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "older-score.tess");
        Score score = CreateScore() with { Parts = [new ScorePartView("Piano", [0])] };
        TessFile.Save(path, score, CreateStyle());

        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.GetEntry("score.json")!;
            JsonObject node;
            using (Stream stream = entry.Open())
            {
                node = JsonNode.Parse(stream)!.AsObject();
            }

            node.Remove("Parts");
            entry.Delete();
            ZipArchiveEntry replacement = archive.CreateEntry("score.json", CompressionLevel.Optimal);
            using Stream output = replacement.Open();
            System.Text.Json.JsonSerializer.Serialize(output, node,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        TessDocument opened = TessFile.Open(path);

        Assert.Empty(opened.Score.PartList);
        Assert.Equal(TessMigrator.CurrentVersion, opened.Manifest.FormatVersion);
    }

    [Fact]
    public void SaveReplacesAnExistingFileAtomically()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "piece.tess");
        TessFile.Save(path, CreateScore(), CreateStyle());
        Score second = CreateScore() with { Metadata = new ScoreMetadata("Second", "B") };

        TessFile.Save(path, second, CreateStyle());

        Assert.Equal("Second", TessFile.Open(path).Score.Metadata.Title);
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public void OlderFormatVersionIsMigratedToTheCurrentVersion()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "old.tess");
        string current = Path.Combine(directory.Path, "current.tess");
        TessFile.Save(current, CreateScore(), CreateStyle());
        // Simulate a version-0 archive whose score used "Author" instead of "Composer".
        using (ZipArchive source = ZipFile.OpenRead(current))
        using (FileStream stream = File.Create(path))
        using (ZipArchive target = new(stream, ZipArchiveMode.Create))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                using StreamReader reader = new(entry.Open());
                string text = reader.ReadToEnd();
                if (entry.Name == "manifest.json")
                {
                    text = text.Replace($"\"FormatVersion\": {TessMigrator.CurrentVersion}", "\"FormatVersion\": 0");
                }
                else if (entry.Name == "score.json")
                {
                    text = text.Replace("\"Composer\"", "\"Author\"");
                }

                using StreamWriter writer = new(target.CreateEntry(entry.Name).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }

        TessMigrator migrator = new(TessMigrator.CurrentVersion, [new TessMigration(0, score =>
        {
            score["Composer"] = score["Author"]!.DeepClone();
            score.Remove("Author");
        })]);

        TessDocument opened = TessFile.Open(path, migrator);

        Assert.Equal("Bach", opened.Score.Metadata.Composer);
        Assert.Throws<InvalidDataException>(() => TessFile.Open(path));
    }

    [Fact]
    public void NewerFormatVersionIsRejectedWithoutLosingData()
    {
        JsonObject score = new();
        TessMigrator migrator = new();

        Assert.Throws<InvalidDataException>(() =>
            migrator.Migrate(score, TessMigrator.CurrentVersion + 1));
    }

    [Fact]
    public async Task AutosaveWritesARecoverableCopyAndCanDiscardIt()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "piece.tess");
        Score score = CreateScore();
        Style style = CreateStyle();
        string recovery = RecoveryAutosave.GetRecoveryPath(path);
        using RecoveryAutosave autosave = new(path, () => (score, style), TimeSpan.FromMilliseconds(30));

        for (int attempt = 0; attempt < 100 && !File.Exists(recovery); attempt++)
        {
            await Task.Delay(30);
        }

        Assert.True(File.Exists(recovery));
        Assert.Equal(TimeSpan.FromMinutes(2), RecoveryAutosave.DefaultInterval);
        autosave.Dispose();
        AssertSameScore(score, TessFile.Open(recovery).Score);
        autosave.Discard();
        Assert.False(File.Exists(recovery));
    }

    private static void AssertSameScore(Score expected, Score actual)
    {
        Assert.Equal(expected.Metadata, actual.Metadata);
        Assert.Equal(expected.Instruments.Length, actual.Instruments.Length);
        for (int i = 0; i < expected.Instruments.Length; i++)
        {
            Assert.Equal(expected.Instruments[i].Name, actual.Instruments[i].Name);
            Assert.Equal(expected.Instruments[i].Staves.AsEnumerable(), actual.Instruments[i].Staves.AsEnumerable());
            Assert.Equal(expected.Instruments[i].Transposition, actual.Instruments[i].Transposition);
        }

        Assert.Equal(expected.Measures.AsEnumerable(), actual.Measures.AsEnumerable());
        Assert.Equal(expected.Content.Count, actual.Content.Count);
        foreach ((StaffMeasureKey key, StaffMeasure measure) in expected.Content)
        {
            StaffMeasure other = actual.Content[key];
            Assert.Equal(measure.Voices.Length, other.Voices.Length);
            for (int v = 0; v < measure.Voices.Length; v++)
            {
                Assert.Equal(measure.Voices[v].Number, other.Voices[v].Number);
                Assert.Equal(measure.Voices[v].Events.Length, other.Voices[v].Events.Length);
                for (int e = 0; e < measure.Voices[v].Events.Length; e++)
                {
                    MusicEvent a = measure.Voices[v].Events[e];
                    MusicEvent b = other.Voices[v].Events[e];
                    Assert.Equal(a.GetType(), b.GetType());
                    Assert.Equal((a.Id, a.Onset, a.Duration), (b.Id, b.Onset, b.Duration));
                    if (a is Chord ca)
                    {
                        Chord cb = (Chord)b;
                        Assert.Equal(ca.Stem, cb.Stem);
                        Assert.Equal(ca.Notes.AsEnumerable(), cb.Notes.AsEnumerable());
                    }
                }
            }
        }
    }

    private static Style CreateStyle() => new()
    {
        StaffLineThickness = 0.13,
        StemThickness = 0.12,
        BeamThickness = 0.5,
        BeamSpacing = 0.25,
        LedgerLineThickness = 0.16,
        LedgerLineExtension = 0.4,
        StemLength = 3.5,
        MinimumAccidentalGap = 0.25,
        MinimumRhythmicGap = 0.5,
    };

    private static Score CreateScore()
    {
        EventId first = new(Guid.NewGuid());
        Chord chord = new(first, Fraction.Zero, new Duration(NoteValue.Quarter, 1),
            [new Note(new Pitch(Step.F, 1, 3), true), new Note(new Pitch(Step.A, -1, 4))], StemDirection.Down);
        Rest rest = new(new EventId(Guid.NewGuid()), new Fraction(3, 8), new Duration(NoteValue.Eighth, 0));
        Rest wholeRest = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Half, 0));
        Rest wholeRest2 = new(new EventId(Guid.NewGuid()), new Fraction(1, 2), new Duration(NoteValue.Half, 0));
        return new Score(new ScoreMetadata("Título ñ", "Bach"),
            [new Instrument("Piano", [new Staff("Right"), new Staff("Left", Clef.Bass)])],
            [new Measure(1, new TimeSignature(3, 4), new KeySignature(-3))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1,
                    [chord, rest, new Rest(new EventId(Guid.NewGuid()), new Fraction(1, 2), new Duration(NoteValue.Quarter, 0))])]))
                .Add(new StaffMeasureKey(1, 0), new StaffMeasure([new Voice(1, [wholeRest, wholeRest2])])));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() => Directory.CreateDirectory(Path);

        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"tessitura-tess-{Guid.NewGuid():N}");

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
