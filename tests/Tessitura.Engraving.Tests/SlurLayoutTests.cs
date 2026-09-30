using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Smufl;
using Xunit;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;
using DisplayPath = Tessitura.Engraving.DisplayLists.Path;
using DisplayPrimitive = Tessitura.Engraving.DisplayLists.DrawingPrimitive;

namespace Tessitura.Engraving.Tests;

public sealed class SlurLayoutTests
{
    private static readonly SmuflMetadata Metadata = LoadMetadata();

    [Fact]
    public void ShortSlurIsAFilledCurveBesideTheHeadsOnTheSideOppositeTheStems()
    {
        Chord[] high = [Note(0, Step.B, 5), Note(1, Step.A, 5)]; // stems down: curve above
        Chord[] low = [Note(0, Step.C, 4), Note(1, Step.D, 4)]; // stems up: curve below

        DisplayPath above = Slur(Compose(Score(high, new Spanner(high[0].Id, high[1].Id, SpannerKind.Slur)), 0));
        DisplayPath below = Slur(Compose(Score(low, new Spanner(low[0].Id, low[1].Id, SpannerKind.Slur)), 0));

        Assert.Equal(0, above.StrokeWidth);
        Assert.Equal(DisplayLists.PathVerb.CubicTo, above.Commands[1].Verb);
        double highHeadTop = Head(Compose(Score(high, new Spanner(high[0].Id, high[1].Id, SpannerKind.Slur)), 0), high[0]).Bounds.Y;
        Assert.True(above.Commands[0].Point1.Y < highHeadTop, "the slur starts above the notehead");
        Assert.True(above.Commands[1].Point1.Y < above.Commands[0].Point1.Y, "the arch rises above the endpoints");
        Assert.True(below.Commands[1].Point1.Y > below.Commands[0].Point1.Y, "the arch hangs below the endpoints");
    }

    [Fact]
    public void LongSlurRisesWithItsSpanAndClearsTheNotesUnderIt()
    {
        Chord[] notes = [Note(0, Step.G, 4), Note(1, Step.A, 4), Note(2, Step.G, 6), Note(3, Step.B, 4), Note(4, Step.C, 5)];
        Chord[] pair = [Note(0, Step.G, 4), Note(1, Step.A, 4)];
        ImmutableArray<DisplayPrimitive> longPage = Compose(Score(notes, new Spanner(notes[0].Id, notes[4].Id, SpannerKind.Slur)), 0);
        ImmutableArray<DisplayPrimitive> shortPage = Compose(Score(pair, new Spanner(pair[0].Id, pair[1].Id, SpannerKind.Slur)), 0);

        DisplayPath longSlur = Slur(longPage);
        DisplayPath shortSlur = Slur(shortPage);

        Assert.True(Rise(longSlur) > Rise(shortSlur), "a longer slur arches higher");
        // Sample the outer edge: no point may lie inside a notehead, stem or accidental of the notes under the slur.
        DisplayLists.PathCommand curve = longSlur.Commands[1];
        DisplayLists.DisplayPoint p0 = longSlur.Commands[0].Point1;
        List<DisplayLists.DisplayBox> obstacles = [.. longPage.Where(p => p.ElementId.Value != Guid.Empty && p is not DisplayPath
            && !(p is DisplayLine l && l.Start.Y == l.End.Y)).Select(p => p.Bounds)];
        for (int i = 1; i < 32; i++)
        {
            double t = i / 32.0;
            double u = 1 - t;
            double x = u * u * u * p0.X + 3 * u * u * t * curve.Point1.X + 3 * u * t * t * curve.Point2.X + t * t * t * curve.Point3.X;
            double y = u * u * u * p0.Y + 3 * u * u * t * curve.Point1.Y + 3 * u * t * t * curve.Point2.Y + t * t * t * curve.Point3.Y;
            foreach (DisplayLists.DisplayBox box in obstacles)
            {
                bool inside = x > box.X && x < box.X + box.Width && y > box.Y && y < box.Y + box.Height;
                Assert.False(inside, $"the slur passes through an element at t={t:F2}");
            }
        }
    }

    [Fact]
    public void SlurAcrossSystemsIsSplitAndOpenAtTheSystemEdges()
    {
        Chord[] notes = [.. Enumerable.Range(0, 40).Select(i => Note(i, i % 2 == 0 ? Step.E : Step.G, 5))];
        Score score = Score(notes, new Spanner(notes[1].Id, notes[38].Id, SpannerKind.Slur), measures: 10);
        Style style = Style.CreateDefault(Metadata);
        ScorePageComposer composer = new(Metadata, style);
        ScoreLayoutResult layout = new IncrementalScoreLayouter(Metadata).Layout(score, style, composer.GetAvailableWidth(score));
        Assert.True(layout.Systems.Length >= 2, "the test needs at least two systems");

        // Both parts may be on one page or on two: gather the curves of both pages and order them top to bottom.
        List<DisplayPath> parts = [.. composer.Compose(score, layout, 0).Page.Primitives.OfType<DisplayPath>()];
        foreach (DisplayPath part in composer.Compose(score, layout, score.Measures.Length - 1).Page.Primitives.OfType<DisplayPath>())
        {
            if (!parts.Contains(part))
            {
                parts.Add(part);
            }
        }

        Assert.Equal(2, parts.Count);
        double startHeadX = Head(composer.Compose(score, layout, 0).Page.Primitives, notes[1]).Origin.X;
        DisplayPath first = parts.Single(p => p.Commands[0].Point1.X >= startHeadX && p.Commands[0].Point1.X <= startHeadX + 2);
        DisplayPath last = parts.Single(p => !ReferenceEquals(p, first));

        double headStartX = Head(composer.Compose(score, layout, 0).Page.Primitives, notes[1]).Origin.X;
        Assert.InRange(first.Commands[0].Point1.X, headStartX, headStartX + 2);
        Assert.True(first.Commands[1].Point3.X > first.Commands[0].Point1.X + 10, "the first part runs on to the end of the system");
        Assert.True(last.Commands[0].Point1.X < last.Commands[1].Point3.X, "the last part runs from the start of its system to the note");
        DisplayLists.DisplayPoint end = last.Commands[1].Point3;
        double headEndX = Head(composer.Compose(score, layout, score.Measures.Length - 1).Page.Primitives, notes[38]).Origin.X;
        Assert.InRange(end.X, headEndX, headEndX + 2);
        Assert.Equal(notes[1].Id.Value, first.ElementId.Value);
        Assert.Equal(notes[1].Id.Value, last.ElementId.Value);
    }

    private static double Rise(DisplayPath slur) => Math.Abs(slur.Commands[1].Point1.Y - slur.Commands[0].Point1.Y);

    private static DisplayPath Slur(ImmutableArray<DisplayPrimitive> primitives) => primitives.OfType<DisplayPath>().Single();

    private static DisplayGlyph Head(ImmutableArray<DisplayPrimitive> primitives, Chord chord) =>
        primitives.OfType<DisplayGlyph>().First(g => g.ElementId.Value == chord.Id.Value && g.Codepoint == Metadata.GetGlyphCodepoint("noteheadBlack"));

    private static Chord Note(int index, Step step, int octave) =>
        new(new EventId(Guid.NewGuid()), new Fraction(index % 4, 4), new Duration(NoteValue.Quarter, 0), [new Note(new Pitch(step, 0, octave))], StemDirection.Auto);

    private static Score Score(Chord[] notes, Spanner slur, int measures = 1)
    {
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content = ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        for (int m = 0; m < measures; m++)
        {
            List<MusicEvent> events = [];
            for (int beat = 0; beat < 4; beat++)
            {
                int index = m * 4 + beat;
                events.Add(index < notes.Length ? notes[index] : new Rest(new EventId(Guid.NewGuid()), new Fraction(beat, 4), new Duration(NoteValue.Quarter, 0)));
            }

            content[new StaffMeasureKey(0, m)] = new StaffMeasure([new Voice(1, [.. events.Select((e, i) => e.WithOnset(new Fraction(i, 4)))])]);
        }

        return new Score(new ScoreMetadata("S", ""), [new Instrument("I", [new Staff("S")])],
            [.. Enumerable.Range(0, measures).Select(m => new Measure(m + 1, new TimeSignature(4, 4)))], content.ToImmutable(),
            default, [slur]);
    }

    private static ImmutableArray<DisplayPrimitive> Compose(Score score, int measure)
    {
        Style style = Style.CreateDefault(Metadata);
        ScorePageComposer composer = new(Metadata, style);
        ScoreLayoutResult layout = new IncrementalScoreLayouter(Metadata).Layout(score, style, composer.GetAvailableWidth(score));
        return composer.Compose(score, layout, measure).Page.Primitives;
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
