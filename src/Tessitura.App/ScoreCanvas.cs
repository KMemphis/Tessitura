using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Tessitura.Core;
using Tessitura.Rendering;

namespace Tessitura.App;

/// <summary>Displays a zoomable and pannable score page.</summary>
public sealed class ScoreCanvas : Control
{
    private readonly MusicPreviewRenderer? _musicPreview;
    private ActionRegistry? _actionRegistry;
    private ScoreInputController? _scoreInputController;
    private Point? _dragPointer;
    private bool _userAdjusted;

    /// <summary>Creates a canvas that initially fits the page in its view.</summary>
    public ScoreCanvas(MusicPreviewRenderer? musicPreview = null)
    {
        _musicPreview = musicPreview;
        Focusable = true;
        SizeChanged += (_, args) =>
        {
            if (_userAdjusted || args.NewSize.Width <= 80 || args.NewSize.Height <= 80)
            {
                return;
            }

            FitPageToBounds();
        };
    }

    /// <summary>Gets the page zoom factor.</summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>Gets the page offset in view coordinates.</summary>
    public Vector PanOffset { get; private set; }

    /// <summary>Gets or sets the musical input controller shown on this canvas.</summary>
    public ScoreInputController? ScoreInputController
    {
        get => _scoreInputController;
        set
        {
            if (ReferenceEquals(_scoreInputController, value))
            {
                return;
            }

            if (_scoreInputController is not null)
            {
                _scoreInputController.StateChanged -= OnScoreInputStateChanged;
            }

            _scoreInputController = value;
            if (_scoreInputController is not null)
            {
                _scoreInputController.StateChanged += OnScoreInputStateChanged;
            }

            InvalidateVisual();
        }
    }

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

    /// <summary>Changes zoom around the center of the canvas.</summary>
    /// <param name="factor">The positive zoom multiplier.</param>
    public void ZoomBy(double factor) =>
        ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), factor);

    /// <summary>Fits the page into the current canvas bounds.</summary>
    public void FitPage()
    {
        FitPageToBounds();
        if (Bounds.Width > 80 && Bounds.Height > 80)
        {
            _userAdjusted = true;
        }
    }

    internal void AttachActionRegistry(ActionRegistry actionRegistry)
    {
        ArgumentNullException.ThrowIfNull(actionRegistry);
        if (_actionRegistry is not null)
        {
            throw new InvalidOperationException("An action registry is already attached to this canvas.");
        }

        _actionRegistry = actionRegistry;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        context.Custom(new PageDrawOperation(new Rect(Bounds.Size), Zoom, PanOffset, _musicPreview));
        if (_scoreInputController is { Mode: ScoreInputMode.NoteEntry } inputController)
        {
            context.Custom(new CursorDrawOperation(
                new Rect(Bounds.Size),
                Zoom,
                PanOffset,
                GetCursorPagePoint(inputController)));
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_actionRegistry?.TryExecute(e.Key, e.KeyModifiers) == true)
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
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
        Focus();
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

    private void FitPageToBounds()
    {
        if (Bounds.Width <= 80 || Bounds.Height <= 80)
        {
            return;
        }

        Zoom = Math.Clamp(
            Math.Min((Bounds.Width - 80) / 595, (Bounds.Height - 80) / 842),
            0.25,
            1);
        PanOffset = new Vector(
            (Bounds.Width - 595 * Zoom) / 2 - 80 * Zoom,
            (Bounds.Height - 842 * Zoom) / 2 - 40 * Zoom);
        InvalidateVisual();
    }

    private static Point GetCursorPagePoint(ScoreInputController controller)
    {
        Score score = controller.CurrentScore;
        ScoreInputCursor cursor = controller.Cursor;
        Fraction measureStart = Fraction.Zero;
        int measureIndex = Math.Max(0, score.Measures.Length - 1);
        Fraction measurePosition = Fraction.Zero;
        Fraction measureLength = new(1, 1);
        bool found = false;

        for (int index = 0; index < score.Measures.Length; index++)
        {
            Fraction currentLength = score.Measures[index].TimeSignature.Length;
            Fraction measureEnd = measureStart + currentLength;
            if (cursor.Position < measureEnd || index == score.Measures.Length - 1)
            {
                measureIndex = index;
                measureLength = currentLength;
                measurePosition = cursor.Position - measureStart;
                if (measurePosition < Fraction.Zero)
                {
                    measurePosition = Fraction.Zero;
                }
                else if (measurePosition > measureLength)
                {
                    measurePosition = measureLength;
                }

                found = true;
                break;
            }

            measureStart = measureEnd;
        }

        if (!found && score.Measures.IsDefaultOrEmpty)
        {
            measurePosition = Fraction.Zero;
        }

        double beatPosition = (double)measurePosition.Num / measurePosition.Den;
        double beatsInMeasure = (double)measureLength.Num / measureLength.Den;
        double measureFraction = beatsInMeasure == 0 ? 0 : beatPosition / beatsInMeasure;
        double x = 250 + (measureIndex % 4) * 74 + measureFraction * 74;
        double y = 190 + cursor.StaffIndex * 90;
        return new Point(x, y);
    }

    private void OnScoreInputStateChanged(object? sender, EventArgs args) => InvalidateVisual();

    private sealed class PageDrawOperation(
        Rect bounds,
        double zoom,
        Vector panOffset,
        MusicPreviewRenderer? musicPreview) : ICustomDrawOperation
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
            PagePreviewRenderer.Draw(canvas, Bounds.Width, Bounds.Height, zoom, panOffset.X, panOffset.Y, musicPreview);
        }

        public bool HitTest(Point point) => Bounds.Contains(point);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }
    }

    private sealed class CursorDrawOperation(
        Rect bounds,
        double zoom,
        Vector panOffset,
        Point pagePoint) : ICustomDrawOperation
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
            canvas.Save();
            canvas.Translate((float)panOffset.X, (float)panOffset.Y);
            canvas.Scale((float)zoom);
            using SKPaint paint = new()
            {
                Color = new SKColor(26, 132, 214),
                StrokeWidth = 1.5f,
                IsAntialias = true,
            };
            canvas.DrawLine(
                (float)pagePoint.X,
                (float)pagePoint.Y - 14,
                (float)pagePoint.X,
                (float)pagePoint.Y + 50,
                paint);
            canvas.Restore();
        }

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }
    }
}
