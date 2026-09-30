using System.Collections.Immutable;
using Tessitura.Engraving;
using Tessitura.Engraving.DisplayLists;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class PageSpatialIndexTests
{
    [Fact]
    public void HitTestPrefersGlyphOverOverlappingStaffLine()
    {
        ElementId lineId = new(Guid.NewGuid());
        ElementId noteId = new(Guid.NewGuid());
        DisplayBox bounds = new(10, 10, 3, 3);
        Page page = new(1, 100, 100,
        [
            new Line(lineId, bounds, new DisplayPoint(10, 11), new DisplayPoint(13, 11), 0.2),
            new Glyph(noteId, bounds, 0xE0A4, new DisplayPoint(10, 12), 4),
        ]);
        PageSpatialIndex index = new(page);

        Assert.Equal(noteId, index.HitTest(new DisplayPoint(11, 11)));
    }

    [Fact]
    public void HitTestReturnsNearestGlyphWithinTolerance()
    {
        ElementId nearId = new(Guid.NewGuid());
        ElementId farId = new(Guid.NewGuid());
        Page page = new(1, 100, 100,
        [
            new Glyph(farId, new DisplayBox(16, 10, 2, 2), 0xE0A4, new DisplayPoint(16, 12), 4),
            new Glyph(nearId, new DisplayBox(10, 10, 2, 2), 0xE0A4, new DisplayPoint(10, 12), 4),
        ]);
        PageSpatialIndex index = new(page, hitTolerance: 3);

        Assert.Equal(nearId, index.HitTest(new DisplayPoint(13, 11)));
    }

    [Fact]
    public void HitTestReturnsNullOutsideTolerance()
    {
        Page page = new(1, 100, 100,
        [
            new Glyph(new ElementId(Guid.NewGuid()), new DisplayBox(10, 10, 2, 2),
                0xE0A4, new DisplayPoint(10, 12), 4),
        ]);
        PageSpatialIndex index = new(page, hitTolerance: 2);

        Assert.Null(index.HitTest(new DisplayPoint(30, 30)));
    }

    [Fact]
    public void ElementBoundsCombineAllPrimitivesFromThatScoreEvent()
    {
        ElementId eventId = new(Guid.NewGuid());
        Page page = new(1, 100, 100,
        [
            new Glyph(eventId, new DisplayBox(10, 10, 2, 2), 0xE0A4, new DisplayPoint(10, 12), 4),
            new Line(eventId, new DisplayBox(10, 5, 0.2, 6), new DisplayPoint(10, 5),
                new DisplayPoint(10, 11), 0.2),
        ]);
        PageSpatialIndex index = new(page);

        Assert.True(index.TryGetBounds(eventId, out DisplayBox bounds));
        Assert.Equal(new DisplayBox(10, 5, 2, 7), bounds);
    }
}
