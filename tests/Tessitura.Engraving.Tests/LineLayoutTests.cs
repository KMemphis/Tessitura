using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Editing;
using Tessitura.Smufl;
using Xunit;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;
using DisplayPrimitive = Tessitura.Engraving.DisplayLists.DrawingPrimitive;

namespace Tessitura.Engraving.Tests;

public sealed class LineLayoutTests
{
    private static readonly SmuflMetadata Metadata = LoadMetadata();

    [Fact]
    public void HairpinsOpenTowardTheLoudEndBelowTheStaff()
    {
        Chord[] notes = Notes(4);
        double staffTop = 0;
        List<DisplayLine> cresc = Lines(Compose(Build(notes, new Spanner(notes[0].Id, notes[3].Id, SpannerKind.Crescendo)), 0), notes[0], out staffTop);
        List<DisplayLine> dim = Lines(Compose(Build(notes, new Spanner(notes[0].Id, notes[3].Id, SpannerKind.Diminuendo)), 0), notes[0], out _);

        Assert.Equal(2, cresc.Count);
        DisplayLine upper = cresc.OrderBy(l => Math.Min(l.Start.Y, l.End.Y)).First();
        DisplayLine lower = cresc.OrderBy(l => Math.Min(l.Start.Y, l.End.Y)).Last();
        Assert.Equal(upper.Start.Y, lower.Start.Y, precision: 6); // closed at the start
        Assert.True(lower.End.Y - upper.End.Y > 1.0, "open at the loud end");
        Assert.True(upper.Start.Y > staffTop + 4, "hairpins sit below the staff");
        DisplayLine dimUpper = dim.OrderBy(l => Math.Min(l.Start.Y, l.End.Y)).First();
        DisplayLine dimLower = dim.OrderBy(l => Math.Min(l.Start.Y, l.End.Y)).Last();
        Assert.True(dimLower.Start.Y - dimUpper.Start.Y > 1.0, "the diminuendo starts open");
        Assert.Equal(dimUpper.End.Y, dimLower.End.Y, precision: 6);
    }

    [Fact]
    public void OctaveLineHasLabelDashesAndHookAboveTheStaffAndPedalHasItsSigns()
    {
        Chord[] notes = Notes(4);

        ImmutableArray<DisplayPrimitive> octave = Compose(Build(notes, new Spanner(notes[0].Id, notes[3].Id, SpannerKind.OctaveUp)), 0);
        ImmutableArray<DisplayPrimitive> pedal = Compose(Build(notes, new Spanner(notes[0].Id, notes[3].Id, SpannerKind.Pedal)), 0);

        double staffTop = octave.OfType<DisplayLine>().First(l => l.ElementId.Value == Guid.Empty).Start.Y;
        DisplayGlyph label = octave.OfType<DisplayGlyph>().Single(g => g.Codepoint == Metadata.GetGlyphCodepoint("ottavaAlta"));
        Assert.True(label.Origin.Y < staffTop);
        List<DisplayLine> dashes = [.. octave.OfType<DisplayLine>().Where(l => l.ElementId.Value == notes[0].Id.Value && l.Start.Y == l.End.Y)];
        Assert.True(dashes.Count >= 3);
        Assert.All(dashes, d => Assert.True(d.Start.Y < staffTop));
        Assert.True(octave.OfType<DisplayLine>().Any(l => l.ElementId.Value == notes[0].Id.Value && l.Start.X == l.End.X), "the end hook");
        Assert.Contains(pedal.OfType<DisplayGlyph>(), g => g.Codepoint == Metadata.GetGlyphCodepoint("keyboardPedalPed"));
        Assert.Contains(pedal.OfType<DisplayGlyph>(), g => g.Codepoint == Metadata.GetGlyphCodepoint("keyboardPedalUp"));
    }

    [Fact]
    public void LinesAreSplitAtSystemBreaksAndKeepDrawingAfterEditsToTheAnchoredMusic()
    {
        Chord[] notes = [.. Enumerable.Range(0, 40).Select(i => Note(i))];
        Score score = Build(notes, new Spanner(notes[1].Id, notes[38].Id, SpannerKind.Crescendo), measures: 10);
        Style style = Style.CreateDefault(Metadata);
        ScorePageComposer composer = new(Metadata, style);
        ScoreLayoutResult layout = new IncrementalScoreLayouter(Metadata).Layout(score, style, composer.GetAvailableWidth(score));
        Assert.True(layout.Systems.Length >= 2);

        Assert.True(HairpinCount(composer, score, layout, 0) >= 2);
        Assert.True(HairpinCount(composer, score, layout, score.Measures.Length - 1) >= 2);

        // Edit the anchored notes: change the end note's pitch and duration, then the start note's pitch.
        EditContext endContext = new(0, 9, 1);
        Score edited = new ChangePitchCommand(notes[38].Id, 0, new Pitch(Step.A, 0, 5)).Apply(score, endContext);
        edited = new ChangeDurationCommand(notes[38].Id, new Duration(NoteValue.Eighth, 0)).Apply(edited, endContext);
        edited = new ChangePitchCommand(notes[1].Id, 0, new Pitch(Step.D, 0, 5)).Apply(edited, new EditContext(0, 0, 1));
        ScoreLayoutResult editedLayout = new IncrementalScoreLayouter(Metadata).Layout(edited, style, composer.GetAvailableWidth(edited));
        Assert.Equal(score.SpannerList, edited.SpannerList);
        Assert.True(HairpinCount(composer, edited, editedLayout, 0) >= 2);

        // Deleting an anchor leaves the spanner in the model but draws nothing and does not fail.
        Score orphan = edited with { Spanners = [new Spanner(new EventId(Guid.NewGuid()), new EventId(Guid.NewGuid()), SpannerKind.Crescendo)] };
        Assert.Equal(0, HairpinCount(composer, orphan, editedLayout, 0));
    }

    private static int HairpinCount(ScorePageComposer composer, Score score, ScoreLayoutResult layout, int measure) =>
        composer.Compose(score, layout, measure).Page.Primitives.OfType<DisplayLine>()
            .Count(l => score.SpannerList.Any(s => s.Start.Value == l.ElementId.Value) && l.Start.X != l.End.X);

    private static List<DisplayLine> Lines(ImmutableArray<DisplayPrimitive> primitives, Chord start, out double staffTop)
    {
        staffTop = primitives.OfType<DisplayLine>().First(l => l.ElementId.Value == Guid.Empty).Start.Y;
        return [.. primitives.OfType<DisplayLine>().Where(l => l.ElementId.Value == start.Id.Value && l.Start.X != l.End.X)];
    }

    private static Chord[] Notes(int count) => [.. Enumerable.Range(0, count).Select(Note)];

    private static Chord Note(int index) =>
        new(new EventId(Guid.NewGuid()), new Fraction(index % 4, 4), new Duration(NoteValue.Quarter, 0), [new Note(new Pitch(Step.E, 0, 4))], StemDirection.Auto);

    private static Score Build(Chord[] notes, Spanner spanner, int measures = 1)
    {
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content = ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        for (int m = 0; m < measures; m++)
        {
            content[new StaffMeasureKey(0, m)] = new StaffMeasure([new Voice(1, [.. Enumerable.Range(0, 4).Select(b => (MusicEvent)notes[m * 4 + b])])]);
        }

        return new Score(new ScoreMetadata("L", ""), [new Instrument("I", [new Staff("S")])],
            [.. Enumerable.Range(0, measures).Select(m => new Measure(m + 1, new TimeSignature(4, 4)))], content.ToImmutable(), default, [spanner]);
    }

    private static ImmutableArray<DisplayPrimitive> Compose(Score score, int measure)
    {
        Style style = Style.CreateDefault(Metadata);
        ScorePageComposer composer = new(Metadata, style);
        return composer.Compose(score, new IncrementalScoreLayouter(Metadata).Layout(score, style, composer.GetAvailableWidth(score)), measure).Page.Primitives;
    }

    private static SmuflMetadata LoadMetadata()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Tessitura.sln")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        return SmuflMetadata.Load(Path.Combine(root, "assets", "fonts", "Bravura.json"), Path.Combine(root, "assets", "fonts", "smufl_glyph_names.json"));
    }
}
