using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Smufl;
using Xunit;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayPrimitive = Tessitura.Engraving.DisplayLists.DrawingPrimitive;

namespace Tessitura.Engraving.Tests;

public sealed class ArticulationLayoutTests
{
    private static readonly SmuflMetadata Metadata = LoadMetadata();

    [Fact]
    public void StemUpNoteGetsMarksBelowInStackingOrderAndInsideTheStaffTheySitInSpaces()
    {
        Chord note = Note(Step.G, 4, NoteValue.Quarter); // G4, second line: stem up
        ImmutableArray<DisplayPrimitive> primitives = Compose(note, ArticulationKind.Marcato, ArticulationKind.Staccato, ArticulationKind.Accent);

        DisplayGlyph head = Head(primitives, note);
        DisplayGlyph staccato = Mark(primitives, note, "articStaccatoBelow");
        DisplayGlyph accent = Mark(primitives, note, "articAccentBelow");
        DisplayGlyph marcato = Mark(primitives, note, "articMarcatoBelow");
        Assert.True(staccato.Origin.Y > head.Origin.Y, "marks go on the notehead side, below a stem-up note");
        Assert.True(head.Origin.Y < staccato.Origin.Y && staccato.Origin.Y < accent.Origin.Y && accent.Origin.Y < marcato.Origin.Y,
            "staccato nearest the head, then accent, then marcato");
        Assert.False(Overlap(staccato, accent) || Overlap(accent, marcato) || Overlap(staccato, marcato));
        double staffTop = primitives.OfType<DisplayLists.Line>().First(l => l.ElementId.Value == Guid.Empty).Start.Y;
        // A mark inside the staff never sits on a line: its centre is half a space off a line.
        double offset = (staccato.Origin.Y - staffTop) % 1.0;
        Assert.InRange(Math.Abs(offset - 0.5), 0, 1e-6);
    }

    [Fact]
    public void StemDownNoteAndWholeNoteGetMarksAbove()
    {
        Chord down = Note(Step.B, 4, NoteValue.Quarter);
        Chord whole = Note(Step.C, 4, NoteValue.Whole);

        DisplayGlyph downMark = Mark(Compose(down, ArticulationKind.Tenuto), down, "articTenutoAbove");
        DisplayGlyph wholeMark = Mark(Compose(whole, ArticulationKind.Accent), whole, "articAccentAbove");

        Assert.True(downMark.Origin.Y < Head(Compose(down), down).Origin.Y);
        Assert.NotNull(wholeMark);
    }

    [Fact]
    public void OrnamentsAndFermataSitAboveTheStaffFermataHighest()
    {
        Chord note = Note(Step.C, 5, NoteValue.Half);
        ImmutableArray<DisplayPrimitive> primitives = Compose(note, ArticulationKind.Fermata, ArticulationKind.Trill);

        double staffTop = primitives.OfType<DisplayLists.Line>().First(l => l.ElementId.Value == Guid.Empty).Start.Y;
        DisplayGlyph trill = Mark(primitives, note, "ornamentTrill");
        DisplayGlyph fermata = Mark(primitives, note, "fermataAbove");
        Assert.True(trill.Origin.Y < staffTop);
        Assert.True(fermata.Origin.Y < trill.Origin.Y, "the fermata is the outermost mark");
        Assert.False(Overlap(trill, fermata));
    }

    private static bool Overlap(DisplayGlyph a, DisplayGlyph b) =>
        a.Bounds.X < b.Bounds.X + b.Bounds.Width && b.Bounds.X < a.Bounds.X + a.Bounds.Width &&
        a.Bounds.Y < b.Bounds.Y + b.Bounds.Height - 1e-6 && b.Bounds.Y < a.Bounds.Y + a.Bounds.Height - 1e-6;

    private static DisplayGlyph Head(ImmutableArray<DisplayPrimitive> primitives, Chord chord) =>
        primitives.OfType<DisplayGlyph>().First(g => g.ElementId.Value == chord.Id.Value &&
            (g.Codepoint == Metadata.GetGlyphCodepoint("noteheadBlack") || g.Codepoint == Metadata.GetGlyphCodepoint("noteheadHalf") || g.Codepoint == Metadata.GetGlyphCodepoint("noteheadWhole")));

    private static DisplayGlyph Mark(ImmutableArray<DisplayPrimitive> primitives, Chord chord, string glyph) =>
        primitives.OfType<DisplayGlyph>().Single(g => g.ElementId.Value == chord.Id.Value && g.Codepoint == Metadata.GetGlyphCodepoint(glyph));

    private static Chord Note(Step step, int octave, NoteValue value) =>
        new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(value, 0), [new Note(new Pitch(step, 0, octave))], StemDirection.Auto);

    private static ImmutableArray<DisplayPrimitive> Compose(Chord chord, params ArticulationKind[] marks)
    {
        Fraction end = chord.Length;
        List<MusicEvent> events = [chord];
        if (end < Fraction.One)
        {
            events.Add(new Rest(new EventId(Guid.NewGuid()), end, new Duration(end == new Fraction(1, 4) ? NoteValue.Half : NoteValue.Quarter, end == new Fraction(1, 4) ? 1 : 0)));
        }

        Score score = new(new ScoreMetadata("A", ""), [new Instrument("I", [new Staff("S")])], [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [.. events])])),
            [.. marks.Select(m => (Attachment)new ArticulationAttachment(chord.Id, m))]);
        Style style = Style.CreateDefault(Metadata);
        ScorePageComposer composer = new(Metadata, style);
        ScoreLayoutResult layout = new IncrementalScoreLayouter(Metadata).Layout(score, style, composer.GetAvailableWidth(score));
        return composer.Compose(score, layout, 0).Page.Primitives;
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
