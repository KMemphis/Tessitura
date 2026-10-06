using System.Collections.Immutable;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Tessitura.Core;

namespace Tessitura.App;

/// <summary>Lays out the main editor window: top bar, side panels, score canvas, bottom panel, and status bar.</summary>
public sealed class MainView : UserControl
{
    private const string NotYetAvailable = "Disponible en una próxima versión";

    private readonly ScoreInputController _input;
    private readonly Dictionary<string, Button> _actionButtons = new(StringComparer.Ordinal);
    private readonly List<(string Id, Control Control)> _shortcutTips = [];
    private readonly ToggleButton _leftToggle;
    private readonly ToggleButton _rightToggle;
    private readonly ToggleButton _bottomToggle;
    private readonly ToggleButton _selectionModeButton;
    private readonly ToggleButton _entryModeButton;
    private readonly Border _modePill;
    private readonly MusicGlyphIcon _durationGlyph;
    private readonly TextBlock _zoomText;
    private readonly TextBlock _staffText;
    private readonly TextBlock _inspectorSelection;
    private readonly TextBlock _inspectorEmpty;
    private readonly StackPanel _scoreFacts;
    private readonly PianoKeyboard _keyboard;
    private readonly ThemeVariantScope _themeScope;
    private ActionRegistry? _actions;

    /// <summary>Creates the editor layout around a score canvas.</summary>
    /// <param name="input">The input controller whose state is reflected in the shell.</param>
    /// <param name="canvas">The score canvas shown in the center of the window.</param>
    public MainView(ScoreInputController input, ScoreCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(canvas);
        _input = input;
        Canvas = canvas;
        FontFamily = ShellTheme.InterfaceFont;
        FontSize = 13;
        Bind(ForegroundProperty, new DynamicResourceExtension("Tess.Text"));

        _leftToggle = ToolToggle(Icons.LeftPanel, "view.panel.left");
        _bottomToggle = ToolToggle(Icons.BottomPanel, "view.panel.bottom");
        _rightToggle = ToolToggle(Icons.RightPanel, "view.panel.right");
        _selectionModeButton = ModeToggle("Selección", "score.selection-mode");
        _entryModeButton = ModeToggle("Entrada", "score.note-entry");
        _durationGlyph = new MusicGlyphIcon
        {
            FontSize = 15,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _zoomText = new TextBlock
        {
            MinWidth = 44,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            FontFeatures = FontFeatureCollection.Parse("tnum"),
        };
        ModeText = new TextBlock { FontSize = 11.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _modePill = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(9, 2),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = ModeText,
        };
        DurationText = StatusText();
        VoiceText = StatusText();
        PositionText = StatusText();
        SelectionText = StatusText();
        _staffText = StatusText();
        _inspectorSelection = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        _inspectorEmpty = new TextBlock
        {
            Text = "Selecciona una nota o un silencio en la partitura para ver sus propiedades.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
        };
        _scoreFacts = new StackPanel { Spacing = 6 };
        _keyboard = new PianoKeyboard { Height = 96, Margin = new Thickness(12, 4, 12, 12) };
        _keyboard.Bind(PianoKeyboard.HighlightBrushProperty, new DynamicResourceExtension("Tess.Accent"));

        MainMenu = new Menu { VerticalAlignment = VerticalAlignment.Center };
        ViewSelector = CreateViewSelector();
        TransportBar = CreateTransportBar();
        ZoomBar = CreateZoomBar();
        TopBar = CreateTopBar();
        StatusBar = CreateStatusBar();
        LeftPanel = CreateLeftPanel();
        RightPanel = CreateRightPanel();
        BottomPanel = CreateBottomPanel();
        CanvasHost = new Border { Child = canvas, ClipToBounds = true };

        DockPanel.SetDock(TopBar, Dock.Top);
        DockPanel.SetDock(StatusBar, Dock.Bottom);
        DockPanel.SetDock(LeftPanel, Dock.Left);
        DockPanel.SetDock(RightPanel, Dock.Right);
        DockPanel.SetDock(BottomPanel, Dock.Bottom);
        DockPanel root = new() { LastChildFill = true };
        root.Children.Add(TopBar);
        root.Children.Add(StatusBar);
        root.Children.Add(LeftPanel);
        root.Children.Add(RightPanel);
        root.Children.Add(BottomPanel);
        root.Children.Add(CanvasHost);
        _themeScope = new ThemeVariantScope { Child = root, RequestedThemeVariant = ThemeVariant.Dark };
        Content = _themeScope;

        BottomPanel.IsVisible = false;
        ApplyTheme(ThemeVariant.Dark);
        _input.StateChanged += (_, _) => RefreshState();
        canvas.ViewChanged += (_, _) => RefreshZoom();
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Bubble);
        RefreshState();
        RefreshZoom();
        RefreshPanels();
    }

    /// <summary>Gets the theme variant applied to the shell.</summary>
    public ThemeVariant ThemeVariant => _themeScope.RequestedThemeVariant ?? ThemeVariant.Dark;

    /// <summary>Gets the score canvas in the center of the window.</summary>
    public ScoreCanvas Canvas { get; }

    /// <summary>Gets the border that hosts the score canvas.</summary>
    public Border CanvasHost { get; }

    /// <summary>Gets the top bar with menu, view selector, transport, and zoom.</summary>
    public Border TopBar { get; }

    /// <summary>Gets the main menu.</summary>
    public Menu MainMenu { get; }

    /// <summary>Gets the page, continuous, and part view selector.</summary>
    public Control ViewSelector { get; }

    /// <summary>Gets the playback transport controls.</summary>
    public Control TransportBar { get; }

    /// <summary>Gets the zoom controls.</summary>
    public Control ZoomBar { get; }

    /// <summary>Gets the collapsible left panel with note input and notation palettes.</summary>
    public Border LeftPanel { get; }

    /// <summary>Gets the collapsible right panel with the inspector.</summary>
    public Border RightPanel { get; }

    /// <summary>Gets the collapsible bottom panel with the virtual keyboard and mixer.</summary>
    public Border BottomPanel { get; }

    /// <summary>Gets the status bar.</summary>
    public Border StatusBar { get; }

    /// <summary>Gets the status text that shows the input mode.</summary>
    public TextBlock ModeText { get; }

    /// <summary>Gets the status text that shows the active duration.</summary>
    public TextBlock DurationText { get; }

    /// <summary>Gets the status text that shows the active voice.</summary>
    public TextBlock VoiceText { get; }

    /// <summary>Gets the status text that shows the cursor's measure and beat.</summary>
    public TextBlock PositionText { get; }

    /// <summary>Gets the status text that describes the selection.</summary>
    public TextBlock SelectionText { get; }

    /// <summary>Gets the toolbar buttons keyed by the action identifier they execute.</summary>
    public IReadOnlyDictionary<string, Button> ActionButtons => _actionButtons;

    /// <summary>Creates the window-layout actions for the action registry.</summary>
    /// <returns>View, panel, and theme actions owned by the shell.</returns>
    public ImmutableArray<ActionDefinition> CreateActions() =>
    [
        new ActionDefinition("view.zoom-in", "Aumentar zoom", "Ctrl+Plus", () => Canvas.ZoomBy(1.1)),
        new ActionDefinition("view.zoom-out", "Reducir zoom", "Ctrl+Minus", () => Canvas.ZoomBy(1 / 1.1)),
        new ActionDefinition("view.fit-page", "Ajustar página", "Ctrl+0", Canvas.FitPage),
        new ActionDefinition("view.panel.left", "Mostrar u ocultar panel de notación", "Ctrl+7",
            () => TogglePanel(LeftPanel)),
        new ActionDefinition("view.panel.bottom", "Mostrar u ocultar panel inferior", "Ctrl+8",
            () => TogglePanel(BottomPanel)),
        new ActionDefinition("view.panel.right", "Mostrar u ocultar inspector", "Ctrl+9",
            () => TogglePanel(RightPanel)),
        new ActionDefinition("view.theme.toggle", "Alternar tema claro y oscuro", "Ctrl+Shift+L", ToggleTheme),
        new ActionDefinition("app.quit", "Salir", "Ctrl+Q", Quit),
    ];

    /// <summary>Connects menus, toolbar tooltips, and keyboard dispatch to the action registry.</summary>
    /// <param name="actions">The application's action registry.</param>
    public void AttachActionRegistry(ActionRegistry actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (_actions is not null)
        {
            throw new InvalidOperationException("An action registry is already attached to this view.");
        }

        _actions = actions;
        foreach ((string id, Control control) in _shortcutTips)
        {
            if (actions.TryGetAction(id, out RegisteredAction? action))
            {
                ToolTip.SetTip(control, $"{action.Name}  ({FormatShortcut(action.Shortcut)})");
            }
        }

        BuildMenu(actions);
        RefreshState();
    }

    /// <summary>Formats a registry shortcut for display, using symbols for arrows and signs.</summary>
    /// <param name="shortcut">The shortcut as stored in the action registry.</param>
    /// <returns>The display text, for example «Ctrl+↑».</returns>
    public static string FormatShortcut(string shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        string[] parts = shortcut.Split('+');
        for (int index = 0; index < parts.Length; index++)
        {
            parts[index] = parts[index] switch
            {
                "Up" => "↑",
                "Down" => "↓",
                "Left" => "←",
                "Right" => "→",
                "Plus" => "+",
                "Minus" => "−",
                "Space" => "Espacio",
                "Period" => ".",
                string part => part,
            };
        }

        return string.Join("+", parts);
    }

    /// <summary>Applies a light or dark theme to the shell and the canvas desk.</summary>
    /// <param name="variant">The theme variant to apply.</param>
    public void ApplyTheme(ThemeVariant variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        _themeScope.RequestedThemeVariant = variant;
        Canvas.WorkspaceColor = variant == ThemeVariant.Light ? ShellTheme.LightWorkspace : ShellTheme.DarkWorkspace;
        if (Application.Current is Application application)
        {
            // Menus and tooltips open in their own top-level windows, so they follow the application variant.
            application.RequestedThemeVariant = variant;
        }
    }

    private void ToggleTheme() =>
        ApplyTheme(ThemeVariant == ThemeVariant.Light ? ThemeVariant.Dark : ThemeVariant.Light);

    private void TogglePanel(Control panel)
    {
        panel.IsVisible = !panel.IsVisible;
        RefreshPanels();
    }

    private void Quit()
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Close();
        }
    }

    private void Execute(string actionId)
    {
        _actions?.TryExecute(actionId);
        Canvas.Focus();
    }

    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Source is TextBox || _actions is null)
        {
            return;
        }

        e.Handled = _actions.TryExecute(e.Key, e.KeyModifiers);
    }

    private void RefreshState()
    {
        bool entry = _input.Mode == ScoreInputMode.NoteEntry;
        ModeText.Text = ShellStatus.DescribeMode(_input.Mode);
        _modePill.Bind(Border.BackgroundProperty, new DynamicResourceExtension(entry ? "Tess.EntrySoft" : "Tess.AccentSoft"));
        ModeText.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(entry ? "Tess.Entry" : "Tess.Accent"));
        _selectionModeButton.IsChecked = !entry;
        _entryModeButton.IsChecked = entry;

        Duration duration = _input.CurrentDuration;
        DurationText.Text = ShellStatus.DescribeDuration(duration);
        _durationGlyph.Glyph = MusicGlyphs.ForNoteValue(duration.Value);
        VoiceText.Text = $"Voz {_input.Cursor.VoiceNumber}";
        PositionText.Text = ShellStatus.DescribePosition(_input.CurrentScore, _input.Cursor.Position);
        SelectionText.Text = ShellStatus.DescribeSelection(_input.CurrentScore, _input.CurrentSelection);
        _staffText.Text = DescribeStaff(_input.CurrentScore, _input.Cursor.StaffIndex);
        foreach ((string id, Button button) in _actionButtons)
        {
            button.IsEnabled = id switch
            {
                "score.undo" => _input.CanUndo,
                "score.redo" => _input.CanRedo,
                _ => button.IsEnabled,
            };
            if (id.StartsWith("score.duration.", StringComparison.Ordinal) && id != "score.duration.dot")
            {
                button.Classes.Set("active", MusicGlyphs.ActionFor(duration.Value) == id);
            }
        }

        if (_actionButtons.TryGetValue("score.duration.dot", out Button? dot))
        {
            dot.Classes.Set("active", duration.Dots > 0);
        }

        bool hasSelection = !_input.CurrentSelection.Items.IsDefaultOrEmpty;
        _inspectorSelection.Text = hasSelection ? SelectionText.Text : string.Empty;
        _inspectorSelection.IsVisible = hasSelection;
        _inspectorEmpty.IsVisible = !hasSelection;
        _keyboard.HighlightedMidi = _input.LastEnteredPitch?.MidiNumber;
        RefreshScoreFacts(_input.CurrentScore);
        if (_actions is not null)
        {
            foreach (MenuItem item in MainMenu.Items.OfType<MenuItem>().SelectMany(menu => menu.Items.OfType<MenuItem>()))
            {
                item.IsEnabled = (string?)item.Tag switch
                {
                    "score.undo" => _input.CanUndo,
                    "score.redo" => _input.CanRedo,
                    _ => true,
                };
            }
        }
    }

    private void RefreshZoom() => _zoomText.Text = $"{Math.Round(Canvas.Zoom * 100)} %";

    private void RefreshPanels()
    {
        _leftToggle.IsChecked = LeftPanel.IsVisible;
        _rightToggle.IsChecked = RightPanel.IsVisible;
        _bottomToggle.IsChecked = BottomPanel.IsVisible;
    }

    private void RefreshScoreFacts(Score score)
    {
        _scoreFacts.Children.Clear();
        int staves = 0;
        foreach (Instrument instrument in score.Instruments)
        {
            staves += instrument.Staves.Length;
        }

        AddFact("Título", string.IsNullOrWhiteSpace(score.Metadata.Title) ? "Sin título" : score.Metadata.Title);
        AddFact("Compositor", string.IsNullOrWhiteSpace(score.Metadata.Composer) ? "—" : score.Metadata.Composer);
        AddFact("Instrumentos", string.Join(", ", score.Instruments.Select(instrument => instrument.Name)));
        AddFact("Pentagramas", staves.ToString());
        AddFact("Compases", score.Measures.Length.ToString());
        if (!score.Measures.IsDefaultOrEmpty)
        {
            TimeSignature meter = score.Measures[0].TimeSignature;
            AddFact("Compás inicial", $"{meter.Numerator}/{meter.Denominator}");
        }

        void AddFact(string label, string value)
        {
            Grid row = new() { ColumnDefinitions = new ColumnDefinitions("96,*") };
            TextBlock name = new() { Text = label, Classes = { "muted" }, FontSize = 12 };
            TextBlock text = new() { Text = value, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(text, 1);
            row.Children.Add(name);
            row.Children.Add(text);
            _scoreFacts.Children.Add(row);
        }
    }

    private static string DescribeStaff(Score score, int staffIndex)
    {
        int index = 0;
        foreach (Instrument instrument in score.Instruments)
        {
            foreach (Staff staff in instrument.Staves)
            {
                if (index++ == staffIndex)
                {
                    return instrument.Staves.Length > 1 ? $"{instrument.Name} · {staff.Name}" : instrument.Name;
                }
            }
        }

        return string.Empty;
    }

    private void BuildMenu(ActionRegistry actions)
    {
        MainMenu.Items.Clear();
        MainMenu.Items.Add(CreateMenu("_Archivo", ["app.quit"], actions));
        MainMenu.Items.Add(CreateMenu("_Edición",
            ["score.undo", "score.redo", null, "score.note-entry", "score.selection-mode"], actions));
        MainMenu.Items.Add(CreateMenu("_Ver",
        [
            "view.zoom-in", "view.zoom-out", "view.fit-page", null,
            "view.panel.left", "view.panel.right", "view.panel.bottom", null,
            "view.theme.toggle",
        ], actions));
        MainMenu.Items.Add(CreateMenu("_Notas",
        [
            "score.duration.whole", "score.duration.half", "score.duration.quarter",
            "score.duration.eighth", "score.duration.sixteenth", "score.duration.dot", null,
            "score.rest", "score.tie.toggle", null,
            "score.pitch.semitone-up", "score.pitch.semitone-down",
            "score.pitch.octave-up", "score.pitch.octave-down",
        ], actions));
    }

    private MenuItem CreateMenu(string header, string?[] actionIds, ActionRegistry actions)
    {
        MenuItem menu = new() { Header = header };
        foreach (string? id in actionIds)
        {
            if (id is null)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            if (!actions.TryGetAction(id, out RegisteredAction? action))
            {
                continue;
            }

            Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinWidth = 250 };
            TextBlock shortcut = new()
            {
                Text = FormatShortcut(action.Shortcut),
                Margin = new Thickness(24, 0, 0, 0),
                Classes = { "muted" },
            };
            Grid.SetColumn(shortcut, 1);
            row.Children.Add(new TextBlock { Text = action.Name });
            row.Children.Add(shortcut);
            menu.Items.Add(new MenuItem
            {
                Header = row,
                Tag = id,
                Command = new ActionCommand(() => Execute(id)),
            });
        }

        return menu;
    }

    private Border CreateTopBar()
    {
        StackPanel brand = new()
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 8, 0),
            Spacing = 7,
        };
        MusicGlyphIcon clef = new()
        {
            Glyph = MusicGlyphs.GClef,
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
        };
        clef.Bind(MusicGlyphIcon.ForegroundProperty, new DynamicResourceExtension("Tess.Accent"));
        brand.Children.Add(clef);
        brand.Children.Add(new TextBlock
        {
            Text = "Tessitura",
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });

        StackPanel left = new() { Orientation = Orientation.Horizontal };
        left.Children.Add(brand);
        left.Children.Add(MainMenu);

        StackPanel center = new() { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        center.Children.Add(ViewSelector);
        center.Children.Add(Divider());
        center.Children.Add(TransportBar);

        StackPanel right = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        right.Children.Add(ZoomBar);
        right.Children.Add(Divider());
        right.Children.Add(_leftToggle);
        right.Children.Add(_bottomToggle);
        right.Children.Add(_rightToggle);
        right.Children.Add(Divider());
        right.Children.Add(ToolButton(Icons.Theme, "view.theme.toggle"));

        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto"), Height = 46 };
        Grid.SetColumn(center, 2);
        Grid.SetColumn(right, 4);
        grid.Children.Add(left);
        grid.Children.Add(center);
        grid.Children.Add(right);
        return Chrome(grid, new Thickness(0, 0, 0, 1));
    }

    private Control CreateViewSelector()
    {
        Border segmented = new()
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        segmented.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Tess.Raised"));
        StackPanel options = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
        options.Children.Add(Segment("Página", isChecked: true, isEnabled: true));
        options.Children.Add(Segment("Continua", isChecked: false, isEnabled: false));
        options.Children.Add(Segment("Parte", isChecked: false, isEnabled: false));
        segmented.Child = options;
        return segmented;

        static ToggleButton Segment(string text, bool isChecked, bool isEnabled)
        {
            ToggleButton button = new()
            {
                Content = text,
                IsChecked = isChecked,
                IsEnabled = isEnabled,
                IsHitTestVisible = !isChecked,
                Padding = new Thickness(12, 4),
                MinHeight = 26,
                FontSize = 12,
                Classes = { "tool" },
            };
            ToolTip.SetTip(button, isEnabled ? "Vista de página" : NotYetAvailable);
            ToolTip.SetShowOnDisabled(button, true);
            return button;
        }
    }

    private Control CreateTransportBar()
    {
        StackPanel transport = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        transport.Children.Add(DisabledTool(Icons.Rewind, "Volver al inicio · " + NotYetAvailable));
        transport.Children.Add(DisabledTool(Icons.Play, "Reproducir (Espacio) · " + NotYetAvailable));
        transport.Children.Add(DisabledTool(Icons.Stop, "Detener · " + NotYetAvailable));
        return transport;
    }

    private Control CreateZoomBar()
    {
        StackPanel zoom = new() { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        zoom.Children.Add(ToolButton(Icons.Minus, "view.zoom-out"));
        zoom.Children.Add(_zoomText);
        zoom.Children.Add(ToolButton(Icons.Plus, "view.zoom-in"));
        zoom.Children.Add(ToolButton(Icons.Fit, "view.fit-page"));
        return zoom;
    }

    private Border CreateLeftPanel()
    {
        StackPanel content = new() { Margin = new Thickness(14, 2, 14, 14) };
        content.Children.Add(Section("ENTRADA DE NOTAS"));
        Grid modes = new() { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetColumn(_entryModeButton, 1);
        modes.Children.Add(_selectionModeButton);
        modes.Children.Add(_entryModeButton);
        Border modeFrame = new() { CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Child = modes };
        modeFrame.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Tess.Raised"));
        content.Children.Add(modeFrame);

        content.Children.Add(Section("DURACIÓN"));
        UniformGrid durations = new() { Columns = 6 };
        foreach (NoteValue value in new[] { NoteValue.Whole, NoteValue.Half, NoteValue.Quarter, NoteValue.Eighth, NoteValue.Sixteenth })
        {
            durations.Children.Add(GlyphButton(MusicGlyphs.ForNoteValue(value), MusicGlyphs.ActionFor(value)));
        }

        durations.Children.Add(GlyphButton(MusicGlyphs.AugmentationDot, "score.duration.dot"));
        content.Children.Add(durations);

        content.Children.Add(Section("ESCRITURA"));
        UniformGrid writing = new() { Columns = 6 };
        writing.Children.Add(GlyphButton(MusicGlyphs.QuarterRest, "score.rest"));
        writing.Children.Add(GlyphButton(MusicGlyphs.Tie, "score.tie.toggle"));
        writing.Children.Add(ToolButton(Icons.ArrowUp, "score.pitch.semitone-up"));
        writing.Children.Add(ToolButton(Icons.ArrowDown, "score.pitch.semitone-down"));
        writing.Children.Add(ToolButton(Icons.Undo, "score.undo"));
        writing.Children.Add(ToolButton(Icons.Redo, "score.redo"));
        content.Children.Add(writing);
        content.Children.Add(new TextBlock
        {
            Text = "Pulsa N para escribir y usa A–G para las notas, 3–7 para la duración y Esc para salir.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Margin = new Thickness(2, 10, 0, 0),
            Classes = { "muted" },
        });

        content.Children.Add(Section("PALETAS"));
        (string Glyph, string Name)[] palettes =
        [
            (MusicGlyphs.GClef, "Claves"),
            (MusicGlyphs.Sharp, "Armaduras"),
            (MusicGlyphs.CommonTime, "Compases"),
            (MusicGlyphs.Flat, "Alteraciones"),
            (MusicGlyphs.Forte, "Dinámicas"),
            (MusicGlyphs.Accent, "Articulaciones"),
            (MusicGlyphs.Hairpin, "Líneas"),
        ];
        foreach ((string glyph, string name) in palettes)
        {
            content.Children.Add(PaletteRow(glyph, name));
        }

        return Panel(new ScrollViewer { Content = content }, 236, new Thickness(0, 0, 1, 0));
    }

    private Border CreateRightPanel()
    {
        StackPanel content = new() { Margin = new Thickness(14, 2, 14, 14) };
        content.Children.Add(Section("INSPECTOR · SELECCIÓN"));
        Border selectionCard = Card(new StackPanel { Children = { _inspectorEmpty, _inspectorSelection } });
        content.Children.Add(selectionCard);
        content.Children.Add(Section("PARTITURA"));
        content.Children.Add(Card(_scoreFacts));
        return Panel(new ScrollViewer { Content = content }, 268, new Thickness(1, 0, 0, 0));
    }

    private Border CreateBottomPanel()
    {
        TabControl tabs = new() { Padding = new Thickness(0), Margin = new Thickness(6, 0, 6, 0) };
        tabs.Items.Add(new TabItem { Header = "Teclado", FontSize = 13, Content = _keyboard });
        tabs.Items.Add(new TabItem
        {
            Header = "Mezclador",
            FontSize = 13,
            Content = new TextBlock
            {
                Text = "El mezclador estará disponible cuando se active la reproducción.",
                Margin = new Thickness(14),
                Classes = { "muted" },
            },
        });
        Border panel = Panel(tabs, double.NaN, new Thickness(0, 1, 0, 0));
        panel.Height = 168;
        return panel;
    }

    private Border CreateStatusBar()
    {
        StackPanel left = new() { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(10, 0) };
        left.Children.Add(_modePill);
        StackPanel duration = new() { Orientation = Orientation.Horizontal };
        _durationGlyph.Bind(MusicGlyphIcon.ForegroundProperty, new DynamicResourceExtension("Tess.TextMuted"));
        duration.Children.Add(_durationGlyph);
        duration.Children.Add(DurationText);
        left.Children.Add(duration);
        left.Children.Add(Dot());
        left.Children.Add(VoiceText);
        left.Children.Add(Dot());
        left.Children.Add(PositionText);
        left.Children.Add(Dot());
        left.Children.Add(SelectionText);

        StackPanel right = new() { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(10, 0) };
        right.Children.Add(_staffText);

        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 28 };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return Chrome(grid, new Thickness(0, 1, 0, 0));

        static TextBlock Dot()
        {
            TextBlock dot = StatusText();
            dot.Text = "·";
            return dot;
        }
    }

    private Button ToolButton(Geometry icon, string actionId)
    {
        Button button = new()
        {
            Content = IconControl(icon),
            Command = new ActionCommand(() => Execute(actionId)),
            Classes = { "tool" },
        };
        ToolTip.SetShowOnDisabled(button, true);
        _actionButtons[actionId] = button;
        _shortcutTips.Add((actionId, button));
        return button;
    }

    private Button GlyphButton(string glyph, string actionId)
    {
        Button button = new()
        {
            Content = new MusicGlyphIcon { Glyph = glyph, FontSize = 24 },
            Command = new ActionCommand(() => Execute(actionId)),
            MinHeight = 38,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Classes = { "tool", "glyph" },
        };
        _actionButtons[actionId] = button;
        _shortcutTips.Add((actionId, button));
        return button;
    }

    private ToggleButton ToolToggle(Geometry icon, string actionId)
    {
        ToggleButton button = new()
        {
            Content = IconControl(icon),
            Command = new ActionCommand(() => Execute(actionId)),
            Classes = { "tool" },
        };
        _shortcutTips.Add((actionId, button));
        return button;
    }

    private ToggleButton ModeToggle(string text, string actionId)
    {
        ToggleButton button = new()
        {
            Content = text,
            Command = new ActionCommand(() => Execute(actionId)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 28,
            FontSize = 12,
            Classes = { "tool" },
        };
        _shortcutTips.Add((actionId, button));
        return button;
    }

    private static Button DisabledTool(Geometry icon, string tip)
    {
        Button button = new() { Content = IconControl(icon), IsEnabled = false, Classes = { "tool" } };
        ToolTip.SetTip(button, tip);
        ToolTip.SetShowOnDisabled(button, true);
        return button;
    }

    private static PathIcon IconControl(Geometry icon) => new() { Data = icon, Width = 15, Height = 15 };

    private static TextBlock Section(string text) => new() { Text = text, Classes = { "section" } };

    private static TextBlock StatusText() => new() { Classes = { "status" } };

    private static Control PaletteRow(string glyph, string name)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("30,*,Auto"),
            Height = 32,
            Opacity = 0.85,
        };
        ToolTip.SetTip(row, NotYetAvailable);
        MusicGlyphIcon icon = new() { Glyph = glyph, FontSize = 19 };
        icon.Bind(MusicGlyphIcon.ForegroundProperty, new DynamicResourceExtension("Tess.TextMuted"));
        TextBlock label = new() { Text = name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        Border badge = new()
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Pronto", FontSize = 10.5, Classes = { "muted" } },
        };
        badge.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Tess.Raised"));
        Grid.SetColumn(label, 1);
        Grid.SetColumn(badge, 2);
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(badge);
        return row;
    }

    private static Border Card(Control child)
    {
        Border card = new()
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10),
            BorderThickness = new Thickness(1),
            Child = child,
        };
        card.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Tess.Raised"));
        card.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("Tess.Border"));
        return card;
    }

    private static Border Divider()
    {
        Border divider = new() { Width = 1, Height = 20, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
        divider.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Tess.Border"));
        return divider;
    }

    private static Border Chrome(Control child, Thickness border)
    {
        Border chrome = new() { Child = child, BorderThickness = border };
        chrome.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Tess.Chrome"));
        chrome.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("Tess.Border"));
        return chrome;
    }

    private static Border Panel(Control child, double width, Thickness border)
    {
        Border panel = new() { Child = child, Width = width, BorderThickness = border };
        panel.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Tess.Panel"));
        panel.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("Tess.Border"));
        return panel;
    }

    private sealed class ActionCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }

    private static class Icons
    {
        public static readonly Geometry Play = Geometry.Parse("M4,2 L14,8 L4,14 Z");
        public static readonly Geometry Stop = Geometry.Parse("M3,3 H13 V13 H3 Z");
        public static readonly Geometry Rewind = Geometry.Parse("M2,2 H4 V14 H2 Z M14,2 L5,8 L14,14 Z");
        public static readonly Geometry Minus = Geometry.Parse("M2,7.25 H14 V8.75 H2 Z");
        public static readonly Geometry Plus = Geometry.Parse("M7.25,2 H8.75 V7.25 H14 V8.75 H8.75 V14 H7.25 V8.75 H2 V7.25 H7.25 Z");
        public static readonly Geometry Fit = Geometry.Parse(
            "M1,1 H6 V2.5 H2.5 V6 H1 Z M10,1 H15 V6 H13.5 V2.5 H10 Z M13.5,10 H15 V15 H10 V13.5 H13.5 Z M1,10 H2.5 V13.5 H6 V15 H1 Z");
        public static readonly Geometry LeftPanel = Geometry.Parse(
            "M1,2 H15 V14 H1 Z M6.5,3.5 V12.5 H13.5 V3.5 Z M2.5,3.5 V12.5 H5.5 V3.5 Z");
        public static readonly Geometry RightPanel = Geometry.Parse(
            "M1,2 H15 V14 H1 Z M2.5,3.5 V12.5 H9.5 V3.5 Z M10.5,3.5 V12.5 H13.5 V3.5 Z");
        public static readonly Geometry BottomPanel = Geometry.Parse(
            "M1,2 H15 V14 H1 Z M2.5,3.5 V8.5 H13.5 V3.5 Z M2.5,9.5 V12.5 H13.5 V9.5 Z");
        public static readonly Geometry Theme = Geometry.Parse(
            "M8,1 A7,7 0 1 1 8,15 A7,7 0 1 1 8,1 Z M8,2.5 V13.5 A5.5,5.5 0 0 0 8,2.5 Z");
        public static readonly Geometry ArrowUp = Geometry.Parse("M8,2 L13.5,7.5 L12.4,8.6 L8.75,4.95 V14 H7.25 V4.95 L3.6,8.6 L2.5,7.5 Z");
        public static readonly Geometry ArrowDown = Geometry.Parse("M8,14 L13.5,8.5 L12.4,7.4 L8.75,11.05 V2 H7.25 V11.05 L3.6,7.4 L2.5,8.5 Z");
        public static readonly Geometry Undo = Geometry.Parse(
            "M5.5,2 L6.6,3.1 L4.45,5.25 H10 A4.5,4.5 0 0 1 10,14.25 H6 V12.75 H10 A3,3 0 0 0 10,6.75 H4.45 L6.6,8.9 L5.5,10 L1.5,6 Z");
        public static readonly Geometry Redo = Geometry.Parse(
            "M10.5,2 L9.4,3.1 L11.55,5.25 H6 A4.5,4.5 0 0 0 6,14.25 H10 V12.75 H6 A3,3 0 0 1 6,6.75 H11.55 L9.4,8.9 L10.5,10 L14.5,6 Z");
    }
}
