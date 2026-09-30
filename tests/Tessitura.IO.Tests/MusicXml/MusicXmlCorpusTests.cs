using System.Xml.Linq;
using Tessitura.Core;
using Xunit;

namespace Tessitura.IO.Tests.MusicXml;

public sealed class MusicXmlCorpusTests
{
    [Fact]
    public void PublicTestSuiteIsPresentClassifiedAndReadable()
    {
        IReadOnlyList<CorpusFile> files = MusicXmlCorpus.Enumerate();
        List<CorpusFile> publicFiles = [.. files.Where(f => f.Source == "public")];

        Assert.True(publicFiles.Count >= 180, $"only {publicFiles.Count} public files");
        Assert.All(publicFiles, f => Assert.NotEqual("Otros", f.Group));
        Assert.Contains(publicFiles, f => f.InScope);
        Assert.Contains(publicFiles, f => !f.InScope);
        Assert.All(publicFiles.Where(f => !f.InScope), f => Assert.NotEmpty(f.Reason));
        Assert.Contains(publicFiles, f => f.Path.EndsWith(".mxl", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(MusicXmlCorpus.Root, "public", "LICENSE.txt")));
        foreach (CorpusFile file in publicFiles)
        {
            MusicXmlSignature signature = MusicXmlSignature.FromFile(file.Path);
            Assert.NotEmpty(signature.Events);
        }
    }

    [Fact]
    public void SignatureReadsChordsBackupDivisionsRestsGraceNotesAndTies()
    {
        XDocument document = XDocument.Parse("""
            <score-partwise version="4.0">
              <part id="P1">
                <measure number="1">
                  <attributes><divisions>2</divisions></attributes>
                  <note><pitch><step>C</step><octave>4</octave></pitch><duration>2</duration><tie type="start"/></note>
                  <note><chord/><pitch><step>E</step><alter>-1</alter><octave>4</octave></pitch><duration>2</duration></note>
                  <note><rest/><duration>2</duration></note>
                  <backup><duration>4</duration></backup>
                  <note><grace/><pitch><step>G</step><octave>3</octave></pitch><voice>2</voice></note>
                  <note><pitch><step>A</step><alter>1</alter><octave>3</octave></pitch><duration>4</duration><voice>2</voice><staff>2</staff></note>
                </measure>
                <measure number="2">
                  <attributes><divisions>1</divisions></attributes>
                  <note><pitch><step>C</step><octave>4</octave></pitch><duration>4</duration><tie type="stop"/></note>
                </measure>
              </part>
            </score-partwise>
            """);

        MusicXmlSignature signature = MusicXmlSignature.FromDocument(document);

        Assert.Equal(
        [
            "0|0|0/1|1/4|note|C4|start",
            "0|0|0/1|1/4|note|E[-1]4|",
            "0|0|1/4|1/4|rest||",
            "0|0|0/1|0/1|grace|G3|",
            "0|0|0/1|1/2|note|A[1]3|",
            "0|1|0/1|1/1|note|C4|stop",
        ], signature.Events.Select(e => e.Key));
        Assert.Equal(2, signature.Events[4].Staff);
        Assert.Equal("2", signature.Events[4].Voice);
    }

    [Fact]
    public void MetricSeparatesLosslessLossyAndUnsupportedRoundTrips()
    {
        IReadOnlyList<CorpusFile> files = [.. MusicXmlCorpus.Enumerate()
            .Where(f => f.Source == "public" && MusicXmlSignature.FromFile(f.Path).Events.Any(e => e.Kind == "note")).Take(20)];
        string work = Path.Combine(Path.GetTempPath(), $"tessitura-fidelity-{Guid.NewGuid():N}");
        try
        {
            FidelityReport identity = FidelityReport.Measure(files, new CopyRoundTrip(), work);
            FidelityReport dropping = FidelityReport.Measure(files, new DroppingRoundTrip(), work);
            FidelityReport pending = FidelityReport.Measure(files, new NotYetImplementedRoundTrip(), work);
            FidelityReport throwing = FidelityReport.Measure(files.Take(2).ToList(), new ThrowingRoundTrip(), work);

            Assert.All(identity.Entries, e => Assert.Equal("lossless", e.Status));
            Assert.Equal(1.0, identity.LosslessShareInScope);
            Assert.Equal(1.0, identity.EventShareInScope);
            Assert.All(dropping.Entries, e => Assert.Equal("lossy", e.Status));
            Assert.InRange(dropping.EventShareInScope, 0.01, 0.999);
            Assert.All(pending.Entries, e => Assert.Equal("unsupported", e.Status));
            Assert.Equal(0.0, pending.LosslessShareInScope);
            Assert.All(throwing.Entries, e => Assert.Equal("failed", e.Status));
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }
        }
    }

    [Fact]
    public void FidelityReportIsGeneratedForTheWholeCorpus()
    {
        IReadOnlyList<CorpusFile> files = MusicXmlCorpus.Enumerate();
        string work = Path.Combine(Path.GetTempPath(), $"tessitura-fidelity-{Guid.NewGuid():N}");
        try
        {
            FidelityReport report = FidelityReport.Measure(files, TessituraRoundTrip.Current, work);
            string markdown = report.ToMarkdown();
            string json = report.ToJson();

            string outputDirectory = Path.Combine(AppContext.BaseDirectory, "reports");
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "f3.1-musicxml-fidelity.md"), markdown);
            File.WriteAllText(Path.Combine(outputDirectory, "f3.1-musicxml-fidelity.json"), json);
            string? capture = Environment.GetEnvironmentVariable("TESSITURA_CAPTURE_DIR");
            if (capture is not null)
            {
                Directory.CreateDirectory(capture);
                File.WriteAllText(Path.Combine(capture, "f3.1-musicxml-fidelity.md"), markdown);
                File.WriteAllText(Path.Combine(capture, "f3.1-musicxml-fidelity.json"), json);
            }

            Assert.Equal(files.Count, report.Entries.Count);
            Assert.Contains("# Informe de fidelidad MusicXML", markdown);
            Assert.Contains("Sin pérdidas (dentro del alcance)", markdown);
            Assert.Contains("\"losslessShareInScope\"", json);
            Assert.DoesNotContain(report.Entries, e => e.Status == "unreadable");
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }
        }
    }

    private sealed class CopyRoundTrip : IMusicXmlRoundTrip
    {
        public string Name => "copia";

        public bool TryRoundTrip(string sourcePath, string outputPath)
        {
            MusicXmlSignature.LoadDocument(sourcePath).Save(outputPath);
            return true;
        }
    }

    private sealed class DroppingRoundTrip : IMusicXmlRoundTrip
    {
        public string Name => "descarta la última nota";

        public bool TryRoundTrip(string sourcePath, string outputPath)
        {
            XDocument document = MusicXmlSignature.LoadDocument(sourcePath);
            document.Descendants().Last(e => e.Name.LocalName == "note").Remove();
            document.Save(outputPath);
            return true;
        }
    }

    private sealed class ThrowingRoundTrip : IMusicXmlRoundTrip
    {
        public string Name => "falla";

        public bool TryRoundTrip(string sourcePath, string outputPath) =>
            throw new InvalidOperationException("boom");
    }
}
