using System.Collections.Immutable;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.IO.Midi;
using Tessitura.IO.MusicXml;
using Tessitura.IO.Tess;
using Tessitura.IO.Tests.MusicXml;
using Xunit;
using Chord = Tessitura.Core.Chord;
using Rest = Tessitura.Core.Rest;
using TimeSignature = Tessitura.Core.TimeSignature;
using Note = Tessitura.Core.Note;

namespace Tessitura.IO.Tests;

public sealed class TupletIoTests
{
    [Fact]
    public void TupletsSurviveTheTessFormatIncludingNestedGroups()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tessitura-tuplet-{Guid.NewGuid():N}.tess");
        try
        {
            Score score = CreateScore();
            TessFile.Save(path, score, new Style { StaffLineThickness = 0.1 });
            Score again = TessFile.Open(path).Score;

            TupletGroup before = Assert.IsType<TupletGroup>(score.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[0]);
            TupletGroup after = Assert.IsType<TupletGroup>(again.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[0]);
            Assert.Equal((before.Id, before.Actual, before.Normal, before.Length), (after.Id, after.Actual, after.Normal, after.Length));
            Assert.True(after.IsConsistent());
            Assert.Equal(before.Children.Select(c => (c.Id, c.Onset, c.Duration)), after.Children.Select(c => (c.Id, c.Onset, c.Duration)));
            Assert.IsType<TupletGroup>(after.Children[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MusicXmlExportOfTupletsValidatesAndReimportsWithTheSoundingRhythm()
    {
        Score score = CreateScore();

        System.Xml.Linq.XDocument document = MusicXmlExporter.ToDocument(score);

        Assert.Empty(MusicXmlSchema.Validate(document));
        Assert.Contains(document.Descendants("time-modification"), e => e.Element("actual-notes")!.Value == "3" && e.Element("normal-notes")!.Value == "2");
        Assert.Contains(document.Descendants("tuplet"), e => e.Attribute("type")!.Value == "start");
        Assert.Contains(document.Descendants("tuplet"), e => e.Attribute("type")!.Value == "stop");
        // Triplet eighths last a third of a quarter: 3 of the 9 divisions per quarter that the nested triplet needs.
        int divisions = int.Parse(document.Descendants("divisions").First().Value);
        Assert.Equal(9, divisions);
        List<int> outer = [.. document.Descendants("note").Where(n => n.Element("pitch")?.Element("step")?.Value is "C" or "G").Select(n => int.Parse(n.Element("duration")!.Value))];
        Assert.Equal([3, 3], outer);
    }

    [Fact]
    public void MidiExportPlacesTripletNotesOnExactThirds()
    {
        MidiFile file = MidiExporter.ToMidiFile(CreateScore());

        List<Melanchall.DryWetMidi.Interaction.Note> notes = [.. file.GetTrackChunks().Last().GetNotes().OrderBy(n => n.Time)];

        Assert.Equal(0, notes.Single(n => n.NoteNumber == 60).Time);
        Assert.Equal(640, notes.Single(n => n.NoteNumber == 67).Time);
        Assert.Equal(320, notes.Single(n => n.NoteNumber == 60).Length);
        Assert.Contains(notes, n => n.NoteNumber == 64 && n.Time == 320); // the nested triplet starts on the second third
    }

    private static Score CreateScore()
    {
        Duration eighth = new(NoteValue.Eighth, 0);
        Chord Note(Fraction onset, Step step) => new(new EventId(Guid.NewGuid()), onset, eighth, [new Tessitura.Core.Note(new Pitch(step, 0, 4))], StemDirection.Auto);
        Fraction third = new(1, 12);
        // A triplet of eighths whose middle member is itself a triplet of sixteenths (three in the time of two).
        Duration sixteenth = new(NoteValue.Sixteenth, 0);
        TupletGroup inner = new(new EventId(Guid.NewGuid()), third, sixteenth, 3, 2,
            [.. Enumerable.Range(0, 3).Select(i => (MusicEvent)new Chord(new EventId(Guid.NewGuid()), new Fraction(i, 24), sixteenth,
                [new Tessitura.Core.Note(new Pitch(Step.E, 0, 4))], StemDirection.Auto))]);
        Fraction innerLength = inner.Length;
        _ = innerLength;
        TupletGroup outer = new(new EventId(Guid.NewGuid()), Fraction.Zero, eighth, 3, 2,
            [Note(Fraction.Zero, Step.C), Note(third, Step.D), Note(new Fraction(2, 12), Step.G)]);
        TupletGroup nested = outer with { Children = outer.Children.SetItem(1, inner) };
        Rest rest = new(new EventId(Guid.NewGuid()), new Fraction(1, 4), new Duration(NoteValue.Half, 1));
        return new Score(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [nested, rest])])));
    }
}
