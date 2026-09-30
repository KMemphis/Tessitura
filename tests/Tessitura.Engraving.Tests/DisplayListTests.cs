using System.Collections.Immutable;
using System.Text.Json;
using Tessitura.Engraving.DisplayLists;
using Xunit;
using DisplayPath = Tessitura.Engraving.DisplayLists.Path;

namespace Tessitura.Engraving.Tests;

public sealed class DisplayListTests
{
    [Fact]
    public void EveryPrimitiveRoundTripsWithItsElementIdAndBounds()
    {
        Page page = CreatePage();

        string json = JsonSerializer.Serialize(page, DisplayListJsonContext.Default.Page);
        Page? restored = JsonSerializer.Deserialize(json, DisplayListJsonContext.Default.Page);

        Assert.NotNull(restored);
        Assert.Equal(page, restored);
        Assert.Collection(restored.Primitives,
            primitive => Assert.IsType<Glyph>(primitive),
            primitive => Assert.IsType<Line>(primitive),
            primitive => Assert.IsType<DisplayPath>(primitive),
            primitive => Assert.IsType<Text>(primitive),
            primitive => Assert.IsType<Rect>(primitive));
        Assert.All(restored.Primitives, primitive =>
        {
            Assert.Equal(new ElementId(new Guid("11111111-1111-1111-1111-111111111111")), primitive.ElementId);
            Assert.Equal(new DisplayBox(1, 2, 3, 4), primitive.Bounds);
        });
    }

    [Fact]
    public void PagesAndPathsCompareByContentsRatherThanArrayIdentity()
    {
        Page first = CreatePage();
        Page second = CreatePage();

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(first.Primitives[2], second.Primitives[2]);
        Assert.Equal(first.Primitives[2].GetHashCode(), second.Primitives[2].GetHashCode());

        Page altered = new(second.Number, second.Width, second.Height,
            second.Primitives.SetItem(0, new Glyph(second.Primitives[0].ElementId,
                second.Primitives[0].Bounds, 0xE0A5, new DisplayPoint(1, 2), 4)));
        Assert.NotEqual(first, altered);

        DisplayPath originalPath = Assert.IsType<DisplayPath>(second.Primitives[2]);
        DisplayPath changedPath = originalPath with
        {
            Commands = originalPath.Commands.SetItem(1,
                new PathCommand(PathVerb.CubicTo, new DisplayPoint(1, 2),
                    new DisplayPoint(2, 3), new DisplayPoint(5, 5))),
        };
        Assert.NotEqual(originalPath, changedPath);
        Assert.NotEqual(first, new Page(second.Number, second.Width, second.Height,
            second.Primitives.SetItem(2, changedPath)));
    }

    private static Page CreatePage()
    {
        ElementId id = new(new Guid("11111111-1111-1111-1111-111111111111"));
        DisplayBox bounds = new(1, 2, 3, 4);
        DisplayPoint start = new(1, 2);
        ImmutableArray<DrawingPrimitive> primitives =
        [
            new Glyph(id, bounds, 0xE0A4, start, 4),
            new Line(id, bounds, start, new DisplayPoint(3, 4), 0.1),
            new DisplayPath(id, bounds,
            [
                new PathCommand(PathVerb.MoveTo, start, default, default),
                new PathCommand(PathVerb.CubicTo, start, new DisplayPoint(2, 3), new DisplayPoint(4, 5)),
                new PathCommand(PathVerb.Close, default, default, default),
            ], 0.12),
            new Text(id, bounds, "Allegro", start, 3),
            new Rect(id, bounds, true),
        ];
        return new Page(1, 210, 297, primitives);
    }
}
