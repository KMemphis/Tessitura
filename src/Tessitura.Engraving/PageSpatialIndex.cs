using Tessitura.Engraving.DisplayLists;

namespace Tessitura.Engraving;

/// <summary>Indexes display-list bounds for fast page-coordinate hit testing.</summary>
public sealed class PageSpatialIndex
{
    private const double DefaultCellSize = 8;
    private const double DefaultHitTolerance = 3;

    private readonly Dictionary<Cell, int[]> _cells;
    private readonly Dictionary<ElementId, DisplayBox> _boundsByElement;
    private readonly DrawingPrimitive[] _primitives;
    private readonly double _cellSize;
    private readonly double _hitToleranceSquared;

    /// <summary>Builds a uniform-grid index for one immutable display-list page.</summary>
    /// <param name="page">The page whose primitive bounds should be indexed.</param>
    /// <param name="cellSize">The grid-cell size in staff spaces.</param>
    /// <param name="hitTolerance">The maximum hit distance from a primitive's bounds.</param>
    public PageSpatialIndex(
        Page page,
        double cellSize = DefaultCellSize,
        double hitTolerance = DefaultHitTolerance)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!double.IsFinite(cellSize) || cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        if (!double.IsFinite(hitTolerance) || hitTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hitTolerance));
        }

        _cellSize = cellSize;
        _hitToleranceSquared = hitTolerance * hitTolerance;
        _primitives = page.Primitives.ToArray();
        Dictionary<Cell, List<int>> building = [];
        _boundsByElement = [];

        for (int primitiveIndex = 0; primitiveIndex < _primitives.Length; primitiveIndex++)
        {
            DrawingPrimitive primitive = _primitives[primitiveIndex];
            if (primitive.ElementId.Value == Guid.Empty || !IsValid(primitive.Bounds))
            {
                continue;
            }

            if (_boundsByElement.TryGetValue(primitive.ElementId, out DisplayBox previousBounds))
            {
                double left = Math.Min(previousBounds.X, primitive.Bounds.X);
                double top = Math.Min(previousBounds.Y, primitive.Bounds.Y);
                double right = Math.Max(
                    previousBounds.X + previousBounds.Width,
                    primitive.Bounds.X + primitive.Bounds.Width);
                double bottom = Math.Max(
                    previousBounds.Y + previousBounds.Height,
                    primitive.Bounds.Y + primitive.Bounds.Height);
                _boundsByElement[primitive.ElementId] = new DisplayBox(left, top, right - left, bottom - top);
            }
            else
            {
                _boundsByElement.Add(primitive.ElementId, primitive.Bounds);
            }

            int minimumX = ToCell(primitive.Bounds.X, cellSize);
            int minimumY = ToCell(primitive.Bounds.Y, cellSize);
            int maximumX = ToCell(primitive.Bounds.X + primitive.Bounds.Width, cellSize);
            int maximumY = ToCell(primitive.Bounds.Y + primitive.Bounds.Height, cellSize);

            for (int cellX = minimumX; ; cellX++)
            {
                for (int cellY = minimumY; ; cellY++)
                {
                    Cell cell = new(cellX, cellY);
                    if (!building.TryGetValue(cell, out List<int>? entries))
                    {
                        entries = [];
                        building.Add(cell, entries);
                    }

                    entries.Add(primitiveIndex);
                    if (cellY == maximumY)
                    {
                        break;
                    }
                }

                if (cellX == maximumX)
                {
                    break;
                }
            }
        }

        _cells = new Dictionary<Cell, int[]>(building.Count);
        foreach ((Cell cell, List<int> entries) in building)
        {
            _cells.Add(cell, entries.ToArray());
        }
    }

    /// <summary>Returns the closest primitive at the point, preferring glyphs over other shapes.</summary>
    /// <param name="point">The page-coordinate point in staff spaces.</param>
    /// <returns>The source element identifier, or <see langword="null"/> when nothing is close enough.</returns>
    public ElementId? HitTest(DisplayPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            return null;
        }

        int centerX = ToCell(point.X, _cellSize);
        int centerY = ToCell(point.Y, _cellSize);
        int radius = checked((int)Math.Ceiling(Math.Sqrt(_hitToleranceSquared) / _cellSize));
        HashSet<int> visited = [];
        bool hasBest = false;
        int bestPriority = -1;
        double bestDistance = double.PositiveInfinity;
        ElementId bestId = default;

        for (int offsetX = -radius; offsetX <= radius; offsetX++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                Cell cell = new(checked(centerX + offsetX), checked(centerY + offsetY));
                if (!_cells.TryGetValue(cell, out int[]? candidates))
                {
                    continue;
                }

                foreach (int primitiveIndex in candidates)
                {
                    if (!visited.Add(primitiveIndex))
                    {
                        continue;
                    }

                    DrawingPrimitive primitive = _primitives[primitiveIndex];
                    double distance = DistanceSquared(point, primitive.Bounds);
                    if (distance > _hitToleranceSquared)
                    {
                        continue;
                    }

                    int priority = primitive is Glyph ? 1 : 0;
                    if (!hasBest || priority > bestPriority ||
                        (priority == bestPriority && distance < bestDistance))
                    {
                        hasBest = true;
                        bestPriority = priority;
                        bestDistance = distance;
                        bestId = primitive.ElementId;
                    }
                }
            }
        }

        return hasBest ? bestId : null;
    }

    /// <summary>Gets the combined bounds for all primitives produced by an element.</summary>
    /// <param name="elementId">The source score-element identifier.</param>
    /// <param name="bounds">The union of its primitive bounds when found.</param>
    /// <returns><see langword="true"/> when the element has drawable bounds on this page.</returns>
    public bool TryGetBounds(ElementId elementId, out DisplayBox bounds) =>
        _boundsByElement.TryGetValue(elementId, out bounds);

    private static bool IsValid(DisplayBox bounds) =>
        double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) &&
        double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) &&
        bounds.Width >= 0 && bounds.Height >= 0;

    private static int ToCell(double coordinate, double cellSize)
    {
        double cell = Math.Floor(coordinate / cellSize);
        if (cell < int.MinValue || cell > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(coordinate));
        }

        return (int)cell;
    }

    private static double DistanceSquared(DisplayPoint point, DisplayBox bounds)
    {
        double right = bounds.X + bounds.Width;
        double bottom = bounds.Y + bounds.Height;
        double horizontal = point.X < bounds.X ? bounds.X - point.X : point.X > right ? point.X - right : 0;
        double vertical = point.Y < bounds.Y ? bounds.Y - point.Y : point.Y > bottom ? point.Y - bottom : 0;
        return horizontal * horizontal + vertical * vertical;
    }

    private readonly record struct Cell(int X, int Y);
}
