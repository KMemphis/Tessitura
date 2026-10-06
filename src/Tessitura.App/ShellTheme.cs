using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Tessitura.App;

/// <summary>Defines the application's light and dark design tokens and control styles.</summary>
public static class ShellTheme
{
    /// <summary>Gets the desk color around the paper in the dark theme.</summary>
    public static Color DarkWorkspace { get; } = Color.Parse("#25282F");

    /// <summary>Gets the desk color around the paper in the light theme.</summary>
    public static Color LightWorkspace { get; } = Color.Parse("#CBCFD6");

    /// <summary>Gets the SMuFL music font embedded in the application.</summary>
    public static FontFamily MusicFont { get; } =
        new("avares://Tessitura.App/Assets/Fonts/Bravura.otf#Bravura");

    /// <summary>Gets the interface font with platform fallbacks.</summary>
    public static FontFamily InterfaceFont { get; } =
        new("Inter, Segoe UI Variable Text, Segoe UI, SF Pro Text, Helvetica Neue, Noto Sans, Cantarell, DejaVu Sans");

    /// <summary>Installs the Fluent base theme, design tokens, and shell styles into an application.</summary>
    /// <param name="application">The application whose styles and resources are configured.</param>
    public static void Install(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        FluentTheme fluent = new() { DensityStyle = DensityStyle.Compact };
        fluent.Palettes[ThemeVariant.Dark] = new ColorPaletteResources
        {
            Accent = Color.Parse("#7C8CFF"),
            RegionColor = Color.Parse("#1D2026"),
            AltHigh = Color.Parse("#14161A"),
            BaseHigh = Color.Parse("#E6E8EC"),
            ChromeMediumLow = Color.Parse("#22252C"),
        };
        fluent.Palettes[ThemeVariant.Light] = new ColorPaletteResources
        {
            Accent = Color.Parse("#4655D6"),
            RegionColor = Color.Parse("#FAFAFB"),
            BaseHigh = Color.Parse("#1D2025"),
        };
        application.Styles.Add(fluent);
        application.Resources.MergedDictionaries.Add(CreateTokens());
        foreach (IStyle style in CreateStyles())
        {
            application.Styles.Add(style);
        }
    }

    /// <summary>Creates the theme-dependent brushes used by the shell.</summary>
    /// <returns>A dictionary with dark and light theme variants.</returns>
    public static ResourceDictionary CreateTokens()
    {
        ResourceDictionary tokens = new();
        tokens.ThemeDictionaries[ThemeVariant.Dark] = CreatePalette(
            chrome: "#16181D", panel: "#1C1F25", raised: "#23262E", border: "#2C3039",
            text: "#E6E8EC", muted: "#8E95A2", accent: "#7C8CFF", accentSoft: "#2A3060",
            entry: "#F2B84B", entrySoft: "#43361A", hover: "#2A2E37");
        tokens.ThemeDictionaries[ThemeVariant.Light] = CreatePalette(
            chrome: "#F1F2F5", panel: "#FAFAFB", raised: "#FFFFFF", border: "#DADDE3",
            text: "#1D2025", muted: "#626976", accent: "#4655D6", accentSoft: "#E2E5FB",
            entry: "#A86A12", entrySoft: "#FBEED3", hover: "#E6E8EC");
        return tokens;
    }

    private static ResourceDictionary CreatePalette(
        string chrome,
        string panel,
        string raised,
        string border,
        string text,
        string muted,
        string accent,
        string accentSoft,
        string entry,
        string entrySoft,
        string hover) => new()
    {
        ["Tess.Chrome"] = new SolidColorBrush(Color.Parse(chrome)),
        ["Tess.Panel"] = new SolidColorBrush(Color.Parse(panel)),
        ["Tess.Raised"] = new SolidColorBrush(Color.Parse(raised)),
        ["Tess.Border"] = new SolidColorBrush(Color.Parse(border)),
        ["Tess.Text"] = new SolidColorBrush(Color.Parse(text)),
        ["Tess.TextMuted"] = new SolidColorBrush(Color.Parse(muted)),
        ["Tess.Accent"] = new SolidColorBrush(Color.Parse(accent)),
        ["Tess.AccentSoft"] = new SolidColorBrush(Color.Parse(accentSoft)),
        ["Tess.Entry"] = new SolidColorBrush(Color.Parse(entry)),
        ["Tess.EntrySoft"] = new SolidColorBrush(Color.Parse(entrySoft)),
        ["Tess.Hover"] = new SolidColorBrush(Color.Parse(hover)),
        ["Tess.Transparent"] = Brushes.Transparent,
    };

    private static IEnumerable<IStyle> CreateStyles()
    {
        // Flat toolbar buttons: transparent until hovered, accent tint while checked.
        yield return new Style(x => x.OfType<Button>().Class("tool"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(7)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(Layoutable.MinWidthProperty, 32d),
                new Setter(Layoutable.MinHeightProperty, 32d),
                new Setter(InputElement.FocusableProperty, false),
                new Setter(Layoutable.VerticalAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center),
            },
        };
        yield return new Style(x => x.OfType<ToggleButton>().Class("tool"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(7)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(6)),
                new Setter(Layoutable.MinWidthProperty, 32d),
                new Setter(Layoutable.MinHeightProperty, 32d),
                new Setter(InputElement.FocusableProperty, false),
                new Setter(Layoutable.VerticalAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center),
            },
        };
        yield return PresenterStyle<Button>(":pointerover", "Tess.Hover", "Tess.Text");
        yield return PresenterStyle<ToggleButton>(":pointerover", "Tess.Hover", "Tess.Text");
        yield return PresenterStyle<ToggleButton>(":checked", "Tess.AccentSoft", "Tess.Accent");
        yield return PresenterStyle<Button>("active", "Tess.AccentSoft", "Tess.Accent");
        yield return PresenterStyle<Button>(":disabled", "Tess.Transparent", "Tess.TextMuted", opacity: 0.45);
        yield return PresenterStyle<ToggleButton>(":disabled", "Tess.Transparent", "Tess.TextMuted", opacity: 0.45);

        yield return new Style(x => x.OfType<TextBlock>().Class("section"))
        {
            Setters =
            {
                new Setter(TextBlock.FontSizeProperty, 10.5),
                new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold),
                new Setter(TextBlock.LetterSpacingProperty, 0.8),
                new Setter(TextBlock.ForegroundProperty, Resource("Tess.TextMuted")),
                new Setter(Layoutable.MarginProperty, new Thickness(2, 14, 0, 8)),
            },
        };
        yield return new Style(x => x.OfType<TextBlock>().Class("muted"))
        {
            Setters = { new Setter(TextBlock.ForegroundProperty, Resource("Tess.TextMuted")) },
        };
        yield return new Style(x => x.OfType<TextBlock>().Class("status"))
        {
            Setters =
            {
                new Setter(TextBlock.FontSizeProperty, 12d),
                new Setter(TextBlock.ForegroundProperty, Resource("Tess.TextMuted")),
                new Setter(Layoutable.VerticalAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center),
            },
        };
        yield return new Style(x => x.OfType<Menu>())
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent) },
        };
        yield return new Style(x => x.OfType<Menu>().Child().OfType<MenuItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(9, 5)),
                new Setter(TemplatedControl.FontSizeProperty, 13d),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(5)),
            },
        };
    }

    private static Style PresenterStyle<T>(string pseudoClass, string? background, string foreground, double opacity = 1)
        where T : ContentControl
    {
        Style style = new(x => x.OfType<T>().Class("tool").Class(pseudoClass).Template()
            .OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        if (background is not null)
        {
            style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Resource(background)));
        }

        style.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, Resource(foreground)));
        if (opacity < 1)
        {
            style.Setters.Add(new Setter(Visual.OpacityProperty, opacity));
        }

        return style;
    }

    private static Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension Resource(string key) => new(key);
}
