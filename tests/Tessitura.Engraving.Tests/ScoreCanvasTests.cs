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
}
