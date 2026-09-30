using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Editing;
using Tessitura.IO.MusicXml;
using Tessitura.IO.Tess;
using Tessitura.IO.Tests.MusicXml;
using Xunit;
using Chord = Tessitura.Core.Chord;

namespace Tessitura.IO.Tests;

public sealed class AttachmentIoTests
{
    [Fact]
    public void ToggleAndDynamicCommandsEditAttachmentsWithoutMutatingTheSnapshot()
    {
        (Score score, Chord[] notes) = Create();
        EditContext context = new(0, 0, 1);

        Score marked = new ToggleArticulationCommand(notes[0].Id, ArticulationKind.Staccato).Apply(score, context);
        Score twice = new ToggleArticulationCommand(notes[0].Id, ArticulationKind.Staccato).Apply(marked, context);
        Score dynamic = new SetDynamicCommand(notes[1].Id, DynamicLevel.F).Apply(marked, context);
        Score replaced = new SetDynamicCommand(notes[1].Id, DynamicLevel.Pp).Apply(dynamic, context);
        Score removed = new SetDynamicCommand(notes[1].Id, null).Apply(replaced, context);

        Assert.Empty(score.AttachmentList);
        Assert.Single(marked.AttachmentList);
        Assert.Empty(twice.AttachmentList);
        Assert.Equal(2, dynamic.AttachmentList.Length);
        Assert.Equal(DynamicLevel.Pp, Assert.Single(replaced.AttachmentList.OfType<DynamicAttachment>()).Level);
        Assert.Empty(removed.AttachmentList.OfType<DynamicAttachment>());
    }

    [Fact]
    public void AttachmentsSurviveTheTessFormatAndOlderFilesStillOpen()
    {
        (Score score, Chord[] notes) = Create();
        Score marked = new ToggleArticulationCommand(notes[0].Id, ArticulationKind.Trill).Apply(
            new SetDynamicCommand(notes[1].Id, DynamicLevel.Mp).Apply(score, new EditContext(0, 0, 1)), new EditContext(0, 0, 1));
        string path = Path.Combine(Path.GetTempPath(), $"tessitura-att-{Guid.NewGuid():N}.tess");
        try
        {
            TessFile.Save(path, marked, new Tessitura.Engraving.Style { StaffLineThickness = 0.1 });
            TessFile.Save(path + "2", score, new Tessitura.Engraving.Style { StaffLineThickness = 0.1 });

            Assert.Equal(marked.AttachmentList.OrderBy(a => a.ToString()), TessFile.Open(path).Score.AttachmentList.OrderBy(a => a.ToString()));
            Assert.Empty(TessFile.Open(path + "2").Score.AttachmentList);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + "2");
        }
    }

    [Fact]
    public void MusicXmlCarriesArticulationsOrnamentsFermatasAndDynamicsBothWays()
    {
        (Score score, Chord[] notes) = Create();
        Score marked = score;
        foreach (ArticulationKind kind in Enum.GetValues<ArticulationKind>())
        {
            marked = new ToggleArticulationCommand(notes[kind == ArticulationKind.Fermata ? 1 : 0].Id, kind).Apply(marked, new EditContext(0, 0, 1));
        }

        marked = new SetDynamicCommand(notes[0].Id, DynamicLevel.Ff).Apply(marked, new EditContext(0, 0, 1));

        System.Xml.Linq.XDocument document = MusicXmlExporter.ToDocument(marked);
        Assert.Empty(MusicXmlSchema.Validate(document));
        Score again = MusicXmlImporter.Import(document).Score;

        Assert.Equal(Enum.GetValues<ArticulationKind>().Order(), again.AttachmentList.OfType<ArticulationAttachment>().Select(a => a.Kind).Order());
        Assert.Equal(DynamicLevel.Ff, Assert.Single(again.AttachmentList.OfType<DynamicAttachment>()).Level);
    }

    [Fact]
    public void TempoTextAndChordSymbolsSurviveTessAndMusicXml()
    {
        (Score score, Chord[] notes) = Create();
        EditContext context = new(0, 0, 1);
        Score marked = new AddAttachmentCommand(new TempoAttachment(notes[0].Id, new Duration(NoteValue.Eighth, 1), 66.5)).Apply(score, context);
        marked = new AddAttachmentCommand(new TextAttachment(notes[1].Id, "dolce")).Apply(marked, context);
        marked = new AddAttachmentCommand(new ChordSymbolAttachment(notes[0].Id, Step.F, 1, "m7", Step.A)).Apply(marked, context);
        string path = Path.Combine(Path.GetTempPath(), $"tessitura-att2-{Guid.NewGuid():N}.tess");
        try
        {
            TessFile.Save(path, marked, new Tessitura.Engraving.Style { StaffLineThickness = 0.1 });
            Assert.Equal(marked.AttachmentList.OrderBy(a => a.ToString()), TessFile.Open(path).Score.AttachmentList.OrderBy(a => a.ToString()));
        }
        finally
        {
            File.Delete(path);
        }

        System.Xml.Linq.XDocument document = MusicXmlExporter.ToDocument(marked);
        Assert.Empty(MusicXmlSchema.Validate(document));
        Score again = MusicXmlImporter.Import(document).Score;
        Assert.Equal(3, again.AttachmentList.Length);
        Assert.Contains(again.AttachmentList.OfType<TempoAttachment>(), t => t.Beat == new Duration(NoteValue.Eighth, 1) && t.Bpm == 66.5);
        Assert.Contains(again.AttachmentList.OfType<TextAttachment>(), t => t.Text == "dolce");
        Assert.Contains(again.AttachmentList.OfType<ChordSymbolAttachment>(), c => c.Display == "F♯m7/A");
    }

    private static (Score, Chord[]) Create()
    {
        Chord[] notes = [.. Enumerable.Range(0, 2).Select(i => new Chord(new EventId(Guid.NewGuid()), new Fraction(i, 4), new Duration(NoteValue.Quarter, 0),
            [new Note(new Pitch(Step.C, 0, 4 + i))], StemDirection.Auto))];
        Rest rest = new(new EventId(Guid.NewGuid()), new Fraction(1, 2), new Duration(NoteValue.Half, 0));
        Score score = new(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])], [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [notes[0], notes[1], rest])])));
        return (score, notes);
    }
}
