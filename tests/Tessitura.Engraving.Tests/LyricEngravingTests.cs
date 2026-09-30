using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Smufl;
using Xunit;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;
using DisplayText = Tessitura.Engraving.DisplayLists.Text;

namespace Tessitura.Engraving.Tests;

public sealed class LyricEngravingTests
{
    [Fact]
    public void DrawsThreeVersesWithHyphensExtendersAndChordSymbols()
    {
        Chord[] notes = [.. Enumerable.Range(0, 4).Select(index => new Chord(
            new EventId(Guid.NewGuid()), new Fraction(index, 4), new Duration(NoteValue.Quarter, 0),
            [new Note(new Pitch(Step.C, 0, 5 + index))], StemDirection.Auto))];
        Score score = new(new ScoreMetadata("Hymn", "Composer"),
            [new Instrument("Choir", [new Staff("Soprano")])], [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0),
                new StaffMeasure([new Voice(1, [.. notes])])),
            [new LyricAttachment(notes[0].Id, 1, "Glo", LyricSyllabic.Begin, LyricExtender.Start),
             new LyricAttachment(notes[1].Id, 1, "ri", LyricSyllabic.Middle),
             new LyricAttachment(notes[2].Id, 1, "a", LyricSyllabic.End, LyricExtender.Stop),
             new LyricAttachment(notes[0].Id, 2, "O"), new LyricAttachment(notes[1].Id, 2, "sing"),
             new LyricAttachment(notes[2].Id, 2, "to"), new LyricAttachment(notes[3].Id, 2, "God"),
             new LyricAttachment(notes[0].Id, 3, "Praise"), new LyricAttachment(notes[1].Id, 3, "the"),
             new LyricAttachment(notes[2].Id, 3, "Lord"),
             new ChordSymbolAttachment(notes[0].Id, Step.C, 0, "maj7")]);
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        ScorePageComposer composer = new(metadata, style);
        ScoreLayoutResult layout = new IncrementalScoreLayouter(metadata).Layout(
            score, style, composer.GetAvailableWidth(score));

        ImmutableArray<DisplayLists.DrawingPrimitive> primitives = composer.Compose(score, layout, 0).Page.Primitives;
        DisplayText[] texts = [.. primitives.OfType<DisplayText>()];
        Assert.Contains(texts, text => text.Content == "Cmaj7");
        Assert.Contains(texts, text => text.Content == "Glo");
        Assert.Contains(texts, text => text.Content == "Praise");
        Assert.Contains(texts, text => text.Content == "God");
        Assert.Contains(texts, text => text.Content == "-");

        DisplayText[] firstNoteVerses = [.. texts.Where(text => text.ElementId.Value == notes[0].Id.Value &&
            text.Content is "Glo" or "O" or "Praise")];
        Assert.Equal(3, firstNoteVerses.Length);
        Assert.Equal(3, firstNoteVerses.Select(text => text.Origin.Y).Distinct().Count());
        Assert.Contains(primitives.OfType<DisplayLine>(), line => line.ElementId.Value == notes[0].Id.Value &&
            Math.Abs(line.Start.Y - line.End.Y) < 1e-6 && line.End.X - line.Start.X > 1.0);
    }

    private static SmuflMetadata LoadMetadata()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Tessitura.sln")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        return SmuflMetadata.Load(Path.Combine(root, "assets", "fonts", "Bravura.json"),
            Path.Combine(root, "assets", "fonts", "smufl_glyph_names.json"));
    }
}
