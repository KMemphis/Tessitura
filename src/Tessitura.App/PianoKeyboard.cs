using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Tessitura.App;

/// <summary>Draws a read-only piano keyboard that highlights the last written pitch.</summary>
public sealed class PianoKeyboard : Control
{
    /// <summary>Defines the <see cref="HighlightedMidi"/> property.</summary>
    public static readonly StyledProperty<int?> HighlightedMidiProperty =
        AvaloniaProperty.Register<PianoKeyboard, int?>(nameof(HighlightedMidi));

    /// <summary>Defines the <see cref="HighlightBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> HighlightBrushProperty =
        AvaloniaProperty.Register<PianoKeyboard, IBrush?>(nameof(HighlightBrush), Brushes.CornflowerBlue);

    private const int FirstMidi = 36;
    private const int LastMidi = 96;
    private static readonly IBrush WhiteKey = new SolidColorBrush(Color.Parse("#F7F7F8"));
    private static readonly IBrush BlackKey = new SolidColorBrush(Color.Parse("#1B1D22"));
    private static readonly IPen KeyEdge = new Pen(new SolidColorBrush(Color.Parse("#9AA0AA")), 1);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#7A808B"));

    static PianoKeyboard()
    {
        AffectsRender<PianoKeyboard>(HighlightedMidiProperty, HighlightBrushProperty);
    }

    /// <summary>Gets or sets the MIDI number of the key to highlight.</summary>
    public int? HighlightedMidi
    {
        get => GetValue(HighlightedMidiProperty);
        set => SetValue(HighlightedMidiProperty, value);
    }

    /// <summary>Gets or sets the brush used for the highlighted key.</summary>
    public IBrush? HighlightBrush
    {
        get => GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        int whiteCount = 0;
        for (int midi = FirstMidi; midi <= LastMidi; midi++)
        {
            if (!IsBlack(midi))
            {
                whiteCount++;
            }
        }

        double width = Bounds.Width / whiteCount;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        IBrush highlight = HighlightBrush ?? Brushes.CornflowerBlue;
        int index = 0;
        for (int midi = FirstMidi; midi <= LastMidi; midi++)
        {
            if (IsBlack(midi))
            {
                continue;
            }

            Rect key = new(index * width, 0, width, height);
            context.DrawRectangle(midi == HighlightedMidi ? highlight : WhiteKey, KeyEdge, key, 0, 0);
            if (midi % 12 == 0)
            {
                FormattedText label = new($"Do{midi / 12 - 1}", System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, Typeface.Default, Math.Min(10, width * 0.45), LabelBrush);
                context.DrawText(label, new Point(key.X + (width - label.Width) / 2, height - label.Height - 4));
            }

            index++;
        }

        index = 0;
        for (int midi = FirstMidi; midi <= LastMidi; midi++)
        {
            if (!IsBlack(midi))
            {
                index++;
                continue;
            }

            double blackWidth = width * 0.6;
            Rect key = new(index * width - blackWidth / 2, 0, blackWidth, height * 0.62);
            context.DrawRectangle(midi == HighlightedMidi ? highlight : BlackKey, null, key, 2, 2);
        }
    }

    private static bool IsBlack(int midi) => (midi % 12) is 1 or 3 or 6 or 8 or 10;
}
