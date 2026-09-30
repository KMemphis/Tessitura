using System.Xml.Linq;
using Tessitura.Core;
using Tessitura.IO.MusicXml;
using Xunit;

namespace Tessitura.IO.Tests.MusicXml;

public sealed class MusicXmlImporterTests
{
    [Fact]
    public void ImportsEveryInScopeCorpusFileWithoutExceptions()
    {
        List<string> failures = [];
        int imported = 0;
        foreach (CorpusFile file in MusicXmlCorpus.Enumerate().Where(f => f.InScope))
        {
            try
            {
                _ = MusicXmlImporter.Import(file.Path);
                imported++;
            }
            catch (Exception exception)
            {
                failures.Add($"{file.RelativePath}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        Assert.Empty(failures);
        Assert.True(imported >= 40, $"only {imported} in-scope files imported");
    }

    [Fact]
    public void InScopeFilesKeepEveryNoteAndProduceValidMeasures()
    {
        List<string> problems = [];
        foreach (CorpusFile file in MusicXmlCorpus.Enumerate().Where(f => f.InScope))
        {
            MusicXmlImportResult result = MusicXmlImporter.Import(file.Path);
            (int matched, int missing, int extra) = ImportSignature.Compare(file.Path, result.Score);
            if (missing != 0 || extra != 0)
            {
                problems.Add($"{file.RelativePath}: {matched} kept, {missing} missing, {extra} extra");
            }

            Score score = result.Score;
            int staffCount = score.Instruments.Sum(i => i.Staves.Length);
            for (int staff = 0; staff < staffCount; staff++)
            {
                for (int measure = 0; measure < score.Measures.Length; measure++)
                {
                    if (!ScoreValidator.IsMeasureValid(score.Measures[measure], score.Content[new StaffMeasureKey(staff, measure)]))
                    {
                        problems.Add($"{file.RelativePath}: staff {staff} measure {measure + 1} is not fully covered");
                    }
                }
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void KeepsPartsStavesClefsKeysMetersVoicesAndTies()
    {
        Score piano = Import("43a-PianoStaff.musicxml").Score;
        Assert.Single(piano.Instruments);
        Assert.Equal([Clef.Treble, Clef.Bass], piano.Instruments[0].Staves.Select(s => s.InitialClef));

        Score keys = Import("13a-KeySignatures.musicxml").Score;
        Assert.Contains(keys.Measures, m => m.KeySignature.Fifths == 7);
        Assert.Contains(keys.Measures, m => m.KeySignature.Fifths == -7);

        Score meters = Import("11a-TimeSignatures.musicxml").Score;
        Assert.True(meters.Measures.Select(m => m.TimeSignature).Distinct().Count() >= 4);

        Score voices = Import("21i-Chord-DifferentVoices.musicxml").Score;
        Assert.Contains(voices.Content.Values, m => m.Voices.Length >= 2);

        Score ties = Import("33k-Tie-Types.musicxml").Score;
        Assert.Contains(ties.Content.Values.SelectMany(m => m.Voices).SelectMany(v => v.Events)
            .OfType<Chord>().SelectMany(c => c.Notes), n => n.TiedToNext);

        Score parts = Import("41a-MultiParts-Partorder.musicxml").Score;
        Assert.True(parts.Instruments.Length >= 2);
        Assert.All(parts.Instruments, i => Assert.False(string.IsNullOrWhiteSpace(i.Name)));
    }

    [Fact]
    public void ImportsCompressedFilesAndAdditiveMetersWithWarnings()
    {
        MusicXmlImportResult compressed = Import("90a-Compressed-MusicXML.mxl");
        Assert.NotEmpty(compressed.Score.Instruments);

        MusicXmlImportResult additive = Import("11c-TimeSignatures-Complex.musicxml");
        Assert.Contains(additive.Warnings, w => w.Message.Contains("additive", StringComparison.Ordinal));
    }

    [Fact]
    public void OutOfScopeMaterialIsSkippedWithWarningsInsteadOfFailing()
    {
        MusicXmlImportResult grace = Import("24a-GraceNotes.musicxml");
        Assert.Contains(grace.Warnings, w => w.Message.Contains("grace", StringComparison.Ordinal));
        Assert.NotEmpty(grace.Score.Content);

        int failures = 0;
        foreach (CorpusFile file in MusicXmlCorpus.Enumerate().Where(f => !f.InScope))
        {
            try
            {
                _ = MusicXmlImporter.Import(file.Path);
            }
            catch (InvalidDataException)
            {
                failures++;
            }
        }

        // Only files that are not usable MusicXML at all may be refused, and always with InvalidDataException.
        Assert.True(failures <= 6, $"{failures} out-of-scope files were refused");
    }

    [Fact]
    public void RejectsFilesThatAreNotMusicXml()
    {
        Assert.Throws<InvalidDataException>(() => MusicXmlImporter.Import(XDocument.Parse("<score-timewise/>")));
        Assert.Throws<InvalidDataException>(() => MusicXmlImporter.Import(XDocument.Parse("<score-partwise/>")));
        string path = Path.Combine(Path.GetTempPath(), $"tessitura-bad-{Guid.NewGuid():N}.musicxml");
        try
        {
            File.WriteAllText(path, "<score-partwise><part>");
            Assert.Throws<InvalidDataException>(() => MusicXmlImporter.Import(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MusicXmlImportResult Import(string name) =>
        MusicXmlImporter.Import(Path.Combine(MusicXmlCorpus.Root, "public", name));
}
