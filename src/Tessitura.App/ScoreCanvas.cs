using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Tessitura.Rendering;

namespace Tessitura.App;

/// <summary>Displays a zoomable and pannable score page.</summary>
public sealed class ScoreCanvas : Control
{
    private Point? _dragPointer;
    private bool _userAdjusted;

    /// <summary>Creates a canvas that initially fits the page in its view.</summary>
    public ScoreCanvas()
    {
        SizeChanged += (_, args) =>
        {
            if (_userAdjusted || args.NewSize.Width <= 80 || args.NewSize.Height <= 80)
            {
                return;
            }

            Zoom = Math.Clamp(
                Math.Min((args.NewSize.Width - 80) / 595, (args.NewSize.Height - 80) / 842),
                0.25,
                1);
            PanOffset = new Vector(
                (args.NewSize.Width - 595 * Zoom) / 2 - 80 * Zoom,
                (args.NewSize.Height - 842 * Zoom) / 2 - 40 * Zoom);
            InvalidateVisual();
        };
    }

    /// <summary>Gets the page zoom factor.</summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>Gets the page offset in view coordinates.</summary>
    public Vector PanOffset { get; private set; }

    /// <summary>Converts a view point into unscaled page coordinates.</summary>
    public Point ViewToPage(Point viewPoint) => new(
        (viewPoint.X - PanOffset.X) / Zoom,
        (viewPoint.Y - PanOffset.Y) / Zoom);

    /// <summary>Changes zoom while keeping the point under the pointer stationary.</summary>
    public void ZoomAt(Point pointer, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor));
        }

        Point anchor = ViewToPage(pointer);
        Zoom = Math.Clamp(Zoom * factor, 0.25, 4);
        PanOffset = new Vector(pointer.X - anchor.X * Zoom, pointer.Y - anchor.Y * Zoom);
        _userAdjusted = true;
        InvalidateVisual();
    }

    /// <summary>Moves the page by a view-space distance.</summary>
    public void PanBy(Vector delta)
    {
        PanOffset += delta;
        _userAdjusted = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        context.Custom(new PageDrawOperation(new Rect(Bounds.Size), Zoom, PanOffset));
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            ZoomAt(e.GetPosition(this), Math.Pow(1.1, e.Delta.Y));
        }
        else
        {
            PanBy(new Vector(e.Delta.X * 48, e.Delta.Y * 48));
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            _dragPointer = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragPointer is Point previous)
        {
            Point current = e.GetPosition(this);
            PanBy(current - previous);
            _dragPointer = current;
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragPointer is not null)
        {
            _dragPointer = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private sealed class PageDrawOperation(Rect bounds, double zoom, Vector panOffset) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;

        public void Render(ImmediateDrawingContext context)
        {
            ISkiaSharpApiLeaseFeature? feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null)
            {
                return;
            }

            using ISkiaSharpApiLease lease = feature.Lease();
            SKCanvas canvas = lease.SkCanvas;
            PagePreviewRenderer.Draw(canvas, Bounds.Width, Bounds.Height, zoom, panOffset.X, panOffset.Y);
        }

        public bool HitTest(Point point) => Bounds.Contains(point);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }
    }
}
