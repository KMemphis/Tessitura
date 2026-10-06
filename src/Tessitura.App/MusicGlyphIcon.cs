using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Tessitura.App;

/// <summary>Draws one SMuFL glyph centered on its outline bounds rather than on the font's line box.</summary>
public sealed class MusicGlyphIcon : Control
{
    /// <summary>Defines the <see cref="Glyph"/> property.</summary>
    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<MusicGlyphIcon, string>(nameof(Glyph), string.Empty);

    /// <summary>Defines the <see cref="FontSize"/> property.</summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<MusicGlyphIcon, double>(nameof(FontSize), 20);

    /// <summary>Defines the inherited <see cref="Foreground"/> property.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<MusicGlyphIcon>();

    private Geometry? _geometry;

    static MusicGlyphIcon()
    {
        AffectsRender<MusicGlyphIcon>(GlyphProperty, FontSizeProperty, ForegroundProperty);
        GlyphProperty.Changed.AddClassHandler<MusicGlyphIcon>((icon, _) => icon._geometry = null);
        FontSizeProperty.Changed.AddClassHandler<MusicGlyphIcon>((icon, _) => icon._geometry = null);
    }

    /// <summary>Gets or sets the SMuFL glyph string.</summary>
    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Gets or sets the music font size.</summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>Gets or sets the glyph brush; inherited from the surrounding text foreground.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        Geometry? geometry = GetGeometry();
        return geometry is null ? default : new Size(Math.Ceiling(geometry.Bounds.Width), Math.Ceiling(geometry.Bounds.Height));
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        Geometry? geometry = GetGeometry();
        if (geometry is null)
        {
            return;
        }

        Rect bounds = geometry.Bounds;
        Vector offset = new(
            Math.Round((Bounds.Width - bounds.Width) / 2 - bounds.X),
            Math.Round((Bounds.Height - bounds.Height) / 2 - bounds.Y));
        using (context.PushTransform(Matrix.CreateTranslation(offset)))
        {
            context.DrawGeometry(Foreground ?? Brushes.Black, null, geometry);
        }
    }

    private Geometry? GetGeometry()
    {
        if (_geometry is null && !string.IsNullOrEmpty(Glyph))
        {
            FormattedText text = new(Glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(ShellTheme.MusicFont), FontSize, Brushes.Black);
            _geometry = text.BuildGeometry(new Point(0, 0));
        }

        return _geometry;
    }
}
