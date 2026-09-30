using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.IO.Tess;
using Xunit;

namespace Tessitura.IO.Tests;

public sealed class RepeatTessFileTests
{
    [Fact]
    public void RepetitionMarkersSurviveATessRoundTripWithoutChangingTheFormatVersion()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "repeats.tess");
        Score score = CreateScore([
            new RepeatInfo(StartRepeat: true, Endings: [1]),
            new RepeatInfo(EndRepeat: 3, Endings: [1, 2], Target: RepeatTarget.Segno,
                Jump: RepeatJump.DalSegnoAlCoda),
            new RepeatInfo(Target: RepeatTarget.Coda, Jump: RepeatJump.ToCoda),
        ]);

        TessFile.Save(path, score, new Style());

        TessDocument document = TessFile.Open(path);
        Assert.Equal(TessMigrator.CurrentVersion, document.Manifest.FormatVersion);
        Assert.Equal(score.Measures.Length, document.Score.Measures.Length);
        for (int index = 0; index < score.Measures.Length; index++)
        {
            Assert.Equal(score.Measures[index].Number, document.Score.Measures[index].Number);
            Assert.Equal(score.Measures[index].TimeSignature, document.Score.Measures[index].TimeSignature);
            Assert.Equal(score.Measures[index].KeySignature, document.Score.Measures[index].KeySignature);
            RepeatInfo expected = Assert.IsType<RepeatInfo>(score.Measures[index].Repeat);
            RepeatInfo actual = Assert.IsType<RepeatInfo>(document.Score.Measures[index].Repeat);
            Assert.Equal(expected.StartRepeat, actual.StartRepeat);
            Assert.Equal(expected.EndRepeat, actual.EndRepeat);
            Assert.Equal(expected.Endings.ToArray(), actual.Endings.ToArray());
            Assert.Equal(expected.Target, actual.Target);
            Assert.Equal(expected.Jump, actual.Jump);
        }
    }

    [Fact]
    public void OlderScoreJsonWithoutRepeatFieldsStillOpens()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "older.tess");
        Score score = CreateScore([null, null, null]);
        TessFile.Save(path, score, new Style());
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.GetEntry("score.json")!;
            JsonObject node;
            using (Stream stream = entry.Open())
            {
                node = JsonNode.Parse(stream)!.AsObject();
            }

            foreach (JsonNode? measure in node["Measures"]!.AsArray())
            {
                measure!.AsObject().Remove("Repeat");
            }

            entry.Delete();
            ZipArchiveEntry replacement = archive.CreateEntry("score.json", CompressionLevel.Optimal);
            using Stream output = replacement.Open();
            JsonSerializer.Serialize(output, node, new JsonSerializerOptions { WriteIndented = true });
        }

        TessDocument opened = TessFile.Open(path);

        Assert.All(opened.Score.Measures, static measure => Assert.Null(measure.Repeat));
        Assert.Equal(TessMigrator.CurrentVersion, opened.Manifest.FormatVersion);
    }

    private static Score CreateScore(RepeatInfo?[] repeats)
    {
        ImmutableArray<Measure> measures = [.. repeats.Select((repeat, index) =>
            new Measure(index + 1, new TimeSignature(4, 4), Repeat: repeat))];
        return new Score(new ScoreMetadata("Repeats", ""),
            [new Instrument("Piano", [new Staff("Treble")])], measures,
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tessitura-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
