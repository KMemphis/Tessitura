using Avalonia;
using Tessitura.App;
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
        canvas.Arrange(new Rect(0, 0, 1000, 800));

        double left = 80 * canvas.Zoom + canvas.PanOffset.X;
        double top = 40 * canvas.Zoom + canvas.PanOffset.Y;
        Assert.InRange(left, 0, 1000);
        Assert.InRange(top, 0, 800);
        Assert.True(left + 595 * canvas.Zoom < 1000);
        Assert.True(top + 842 * canvas.Zoom < 800);
    }
}
