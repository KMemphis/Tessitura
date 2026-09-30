using Avalonia;
using Avalonia.Input;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Editing;
using Tessitura.Engraving.DisplayLists;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class ScoreCanvasTests
{
    [Theory]
    [InlineData(Clef.Treble, Step.G, 4)]
    [InlineData(Clef.Bass, Step.B, 2)]
    [InlineData(Clef.Alto, Step.A, 3)]
    public void FirstPointerNoteUsesTheClickedStaffHeightWithoutSelectingARestFirst(
        Clef clef, Step expectedStep, int expectedOctave)
    {
        EventId restId = new(Guid.NewGuid());
        Score score = new(new ScoreMetadata("Test", ""),
            [new Instrument("Piano", [new Staff("Staff", clef)])],
            [new Measure(1, new TimeSignature(4, 4))],
            System.Collections.Immutable.ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1,
                    [new Rest(restId, Fraction.Zero, new Duration(NoteValue.Whole, 0))])])));
        ScoreInputController input = new(score);
        ScoreCanvas canvas = new() { ScoreInputController = input };
        List<DrawingPrimitive> primitives = [];
        for (int line = 0; line < 5; line++)
        {
            primitives.Add(new Tessitura.Engraving.DisplayLists.Line(new ElementId(Guid.Empty),
                new DisplayBox(5, 5 + line, 20, 0.1), new DisplayPoint(5, 5 + line),
                new DisplayPoint(25, 5 + line), 0.1));
        }

        primitives.Add(new Glyph(new ElementId(restId.Value), new DisplayBox(9.5, 7.5, 1, 1),
            0xE4E3, new DisplayPoint(10, 8), 4));
        canvas.AttachDisplayPage(new Page(1, 100, 100, [.. primitives]));
        string settings = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tessitura-pointer-{Guid.NewGuid():N}.json");
        try
        {
            ActionRegistry actions = ActionRegistry.LoadOrCreate(input.CreateActions(), settings);
            canvas.AttachActionRegistry(actions);
            Assert.True(actions.TryExecute("score.note-entry"));

            Assert.True(canvas.PlaceNoteAt(new Point(200, 136)));

            Chord note = Assert.IsType<Chord>(input.CurrentScore.Content[
                new StaffMeasureKey(0, 0)].Voices[0].Events[0]);
            Assert.Equal(new Pitch(expectedStep, 0, expectedOctave), Assert.Single(note.Notes).Pitch);
            Assert.Equal(new Fraction(1, 4), input.Cursor.Position);
            Assert.Empty(input.CurrentSelection.Items);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void ZoomKeepsPointerPositionOnPage()
    {
        ScoreCanvas canvas = new();
        Point pointer = new(420, 300);
        Point pageBefore = canvas.ViewToPage(pointer);

        canvas.ZoomAt(pointer, 2);

        Assert.Equal(2, canvas.Zoom);
        Assert.Equal(pageBefore, canvas.ViewToPage(pointer));
    }

    [Fact]
    public void ZoomIsClampedAndPanMovesPage()
    {
        ScoreCanvas canvas = new();
        canvas.ZoomAt(new Point(0, 0), 100);
        Assert.Equal(4, canvas.Zoom);

        canvas.PanBy(new Vector(25, -10));
        Assert.Equal(new Vector(25, -10), canvas.PanOffset);
    }

    [Fact]
    public void InitialPageFitsWithinView()
    {
        ScoreCanvas canvas = new();
        canvas.Measure(new Size(1000, 800));
        canvas.Arrange(new Avalonia.Rect(0, 0, 1000, 800));

        double left = 80 * canvas.Zoom + canvas.PanOffset.X;
        double top = 40 * canvas.Zoom + canvas.PanOffset.Y;
        Assert.InRange(left, 0, 1000);
        Assert.InRange(top, 0, 800);
        Assert.True(left + 595 * canvas.Zoom < 1000);
        Assert.True(top + 842 * canvas.Zoom < 800);
    }

    [Fact]
    public void ClickOnRenderedNoteHeadSelectsItsScoreNote()
    {
        EventId eventId = new(Guid.NewGuid());
        Chord chord = new(eventId, Fraction.Zero, new Duration(NoteValue.Whole, 0),
            [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Score score = new(
            new ScoreMetadata("Test", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, new TimeSignature(4, 4))],
            System.Collections.Immutable.ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [chord])])));
        ScoreInputController input = new(score);
        ScoreCanvas canvas = new() { ScoreInputController = input };
        DisplayBox noteBounds = new(5, 5, 2, 2);
        Page page = new(1, 100, 100,
        [new Glyph(new ElementId(eventId.Value), noteBounds, 0xE0A4, new DisplayPoint(5, 6), 4)]);
        canvas.AttachDisplayPage(page);

        bool selected = canvas.SelectAt(new Point(152, 112), KeyModifiers.None);

        Assert.True(selected);
        Assert.Equal(new SelectionItem(eventId, 0), Assert.Single(input.CurrentSelection.Items));
    }

    [Fact]
    public void ClickOnStaffLineDoesNotSelectANonEventIdentifier()
    {
        EventId eventId = new(Guid.NewGuid());
        Chord chord = new(eventId, Fraction.Zero, new Duration(NoteValue.Whole, 0),
            [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Score score = new(
            new ScoreMetadata("Test", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, new TimeSignature(4, 4))],
            System.Collections.Immutable.ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [chord])])));
        ScoreInputController input = new(score);
        ScoreCanvas canvas = new() { ScoreInputController = input };
        Page page = new(1, 100, 100,
        [new Tessitura.Engraving.DisplayLists.Line(new ElementId(Guid.Empty),
            new DisplayBox(5, 5.5, 20, 0.2), new DisplayPoint(5, 5.6),
            new DisplayPoint(25, 5.6), 0.2)]);
        canvas.AttachDisplayPage(page);

        bool selected = canvas.SelectAt(new Point(152, 107.2), KeyModifiers.None);

        Assert.False(selected);
        Assert.Empty(input.CurrentSelection.Items);
    }
}
