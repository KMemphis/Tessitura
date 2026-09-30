using System.Collections.Immutable;
using System.Xml.Linq;
using Tessitura.Core;
using Tessitura.IO.MusicXml;
using Xunit;

namespace Tessitura.IO.Tests.MusicXml;

public sealed class MusicXmlExporterTests
{
    [Fact]
    public void ExportedScoreValidatesAgainstTheMusicXml40Schema()
    {
        XDocument document = MusicXmlExporter.ToDocument(CreatePianoScore(), "1.0.0");

        Assert.Empty(MusicXmlSchema.Validate(document));
        Assert.Equal("4.0", document.Root!.Attribute("version")!.Value);
        Assert.Equal("score-partwise", document.Root.Name.LocalName);
        Assert.Equal("Sonatina", document.Root.Element("work")!.Element("work-title")!.Value);
        Assert.Equal("Tessitura 1.0.0", document.Root.Element("identification")!.Element("encoding")!.Element("software")!.Value);
    }

    [Fact]
    public void ExportWritesChordsTiesStavesVoicesAndExactDurations()
    {
        Score score = CreatePianoScore();

        Score again = MusicXmlImporter.Import(MusicXmlExporter.ToDocument(score)).Score;

        Assert.Equal(ImportSignature.Notes(score).Order(), ImportSignature.Notes(again).Order());
        Assert.Equal(2, again.Instruments[0].Staves.Length);
        Assert.Equal([Clef.Treble, Clef.Bass], again.Instruments[0].Staves.Select(s => s.InitialClef));
        Assert.Equal(new TimeSignature(3, 4), again.Measures[0].TimeSignature);
        Assert.Equal(new KeySignature(-2), again.Measures[0].KeySignature);
        Assert.Equal("Sonatina", again.Metadata.Title);
        Assert.Equal("Anon", again.Metadata.Composer);
        Assert.Contains(again.Content.Values.SelectMany(m => m.Voices).SelectMany(v => v.Events).OfType<Chord>(),
            c => c.Notes.Length == 2);
        Assert.Contains(again.Content.Values.SelectMany(m => m.Voices).Where(v => v.Number == 2), v => v.Events.OfType<Chord>().Any());
    }

    [Fact]
    public void TiesAcrossBarlinesGetStartAndStopMarks()
    {
        XDocument document = MusicXmlExporter.ToDocument(CreatePianoScore());

        List<string> ties = [.. document.Descendants("tie").Select(e => e.Attribute("type")!.Value)];
        Assert.Contains("start", ties);
        Assert.Contains("stop", ties);
        Assert.Equal(ties.Count(t => t == "start"), ties.Count(t => t == "stop"));
    }

    [Fact]
    public void SavesPlainAndCompressedFilesAtomically()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"tessitura-mxl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            Score score = CreatePianoScore();
            string plain = Path.Combine(directory, "a.musicxml");
            string compressed = Path.Combine(directory, "a.mxl");

            MusicXmlExporter.Save(plain, score);
            MusicXmlExporter.Save(compressed, score);

            Assert.Empty(MusicXmlSchema.Validate(MusicXmlSignature.LoadDocument(plain)));
            Assert.Empty(MusicXmlSchema.Validate(MusicXmlSignature.LoadDocument(compressed)));
            Assert.Equal(ImportSignature.Notes(score).Order(), ImportSignature.Notes(MusicXmlImporter.Import(compressed).Score).Order());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EveryCorpusFileExportsToValidMusicXml()
    {
        List<string> invalid = [];
        int exported = 0;
        foreach (CorpusFile file in MusicXmlCorpus.Enumerate())
        {
            MusicXmlImportResult result;
            try
            {
                result = MusicXmlImporter.Import(file.Path);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            IReadOnlyList<string> errors = MusicXmlSchema.Validate(MusicXmlExporter.ToDocument(result.Score));
            exported++;
            if (errors.Count > 0)
            {
                invalid.Add($"{file.RelativePath}: {errors[0]}");
            }
        }

        Assert.True(invalid.Count == 0, string.Join(Environment.NewLine, invalid));
        Assert.True(exported >= 170, $"only {exported} files were exported");
    }

    [Fact]
    public void RoundTripKeepsAtLeastNinetyPercentOfTheInScopeCorpus()
    {
        string work = Path.Combine(Path.GetTempPath(), $"tessitura-roundtrip-{Guid.NewGuid():N}");
        try
        {
            FidelityReport report = FidelityReport.Measure(MusicXmlCorpus.Enumerate(), new TessituraMusicXmlRoundTrip(), work);

            Assert.DoesNotContain(report.Entries, e => e.File.InScope && e.Status is "failed" or "unreadable");
            Assert.True(report.LosslessShareInScope >= 0.9, $"lossless share {report.LosslessShareInScope:P1}");
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }
        }
    }

    private static Score CreatePianoScore()
    {
        EventId Id() => new(Guid.NewGuid());
        Duration quarter = new(NoteValue.Quarter, 0);
        Chord Note(Fraction onset, Duration duration, Step step, int alter, int octave, bool tie = false, int? second = null) =>
            new(Id(), onset, duration,
                second is int extra
                    ? [new Note(new Pitch(step, alter, octave), tie), new Note(new Pitch(step, alter, octave + extra))]
                    : [new Note(new Pitch(step, alter, octave), tie)],
                StemDirection.Auto);
        ImmutableDictionary<StaffMeasureKey, StaffMeasure> content = ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
            .Add(new StaffMeasureKey(0, 0), new StaffMeasure([
                new Voice(1,
                [
                    Note(Fraction.Zero, quarter, Step.C, 0, 5),
                    Note(new Fraction(1, 4), new Duration(NoteValue.Half, 0), Step.B, -1, 4, tie: true, second: 1),
                ]),
                new Voice(2, [new Rest(Id(), Fraction.Zero, new Duration(NoteValue.Half, 1))]),
            ]))
            .Add(new StaffMeasureKey(0, 1), new StaffMeasure([
                new Voice(1,
                [
                    Note(Fraction.Zero, new Duration(NoteValue.Quarter, 1), Step.B, -1, 4),
                    Note(new Fraction(3, 8), new Duration(NoteValue.Eighth, 0), Step.A, 0, 4),
                    new Rest(Id(), new Fraction(1, 2), quarter),
                ]),
                new Voice(2,
                [
                    Note(Fraction.Zero, new Duration(NoteValue.Half, 1), Step.G, 0, 4),
                ]),
            ]))
            .Add(new StaffMeasureKey(1, 0), new StaffMeasure([new Voice(1,
                [Note(Fraction.Zero, new Duration(NoteValue.Half, 1), Step.E, -1, 2)])]))
            .Add(new StaffMeasureKey(1, 1), new StaffMeasure([new Voice(1,
                [Note(Fraction.Zero, new Duration(NoteValue.Half, 1), Step.E, -1, 2)])]));
        return new Score(new ScoreMetadata("Sonatina", "Anon"),
            [new Instrument("Piano", [new Staff("RH"), new Staff("LH", Clef.Bass)])],
            [new Measure(1, new TimeSignature(3, 4), new KeySignature(-2)), new Measure(2, new TimeSignature(3, 4), new KeySignature(-2))],
            content);
    }
}
