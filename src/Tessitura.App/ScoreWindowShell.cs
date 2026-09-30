using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using SkiaSharp;
using Tessitura.Core;
using Tessitura.Editing;

namespace Tessitura.App;

/// <summary>Arranges the editor chrome around the score canvas.</summary>
public sealed class ScoreWindowShell : UserControl, IDisposable
{
    private readonly ScoreInputController _input;
    private readonly Grid _layout;
    private readonly TextBlock _statusText;
    private readonly TextBlock _inspectorText;
    private readonly TextBlock _inspectorDetails;
    private readonly ImmutableArray<ScorePartView> _availableParts;
    private readonly List<(Button Button, bool RequiresNote)> _inspectorButtons = [];
    private readonly List<(Button Button, Step Step)> _inspectorPitchButtons = [];
    private readonly Dictionary<string, (Button Header, WrapPanel Items)> _paletteGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _toolbarButtons = new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeToolbarActions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (TextBlock Icon, TextBlock Caption)> _toolbarVisuals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _toolbarRequirements = new(StringComparer.Ordinal);
    private readonly List<Control> _themedControls = [];
    private readonly List<Border> _popupSurfaces = [];
    private readonly List<Border> _toolbarSeparators = [];
    private readonly Action _newScoreRequested;
    private readonly Func<Task> _openScoreRequested;
    private Popup? _morePopup;
    private Popup? _filePopup;
    private Popup? _viewSelectorPopup;
    private Popup? _durationSelectorPopup;
    private ActionRegistry? _actions;
    private CommandPalette? _commandPalette;
    private TextPopover? _textPopover;
    private bool _isDarkTheme = true;
    private bool _isPlaying;

    /// <summary>Creates the main editor layout around an existing score canvas.</summary>
    public ScoreWindowShell(ScoreCanvas canvas, ScoreInputController input,
        Action? newScoreRequested = null, Func<Task>? openScoreRequested = null)
    {
        Canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _newScoreRequested = newScoreRequested ?? (() => { });
        _openScoreRequested = openScoreRequested ?? (() => Task.CompletedTask);
        _availableParts = GetAvailableParts(input.CurrentScore);
        _layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };

        TopBar = CreateTopBar();
        LeftPanel = CreateNotationPalettePanel();
        RightPanel = CreateInspectorPanel(out _inspectorText, out _inspectorDetails);
        BottomPanel = CreateBottomPanel();
        BottomPanel.IsVisible = false;
        StatusBar = CreateStatusBar(out _statusText);

        Grid.SetRow(TopBar, 0);
        Grid.SetColumnSpan(TopBar, 3);
        Grid.SetRow(LeftPanel, 1);
        Grid.SetColumn(LeftPanel, 0);
        Grid.SetRow(Canvas, 1);
        Grid.SetColumn(Canvas, 1);
        Grid.SetRow(RightPanel, 1);
        Grid.SetColumn(RightPanel, 2);
        Grid.SetRow(BottomPanel, 2);
        Grid.SetColumnSpan(BottomPanel, 3);
        Grid.SetRow(StatusBar, 3);
        Grid.SetColumnSpan(StatusBar, 3);

        _layout.Children.Add(TopBar);
        _layout.Children.Add(LeftPanel);
        _layout.Children.Add(Canvas);
        _layout.Children.Add(RightPanel);
        _layout.Children.Add(BottomPanel);
        _layout.Children.Add(StatusBar);
        Content = _layout;
        AttachedToVisualTree += (_, _) => ApplyTheme();
        _input.StateChanged += OnInputStateChanged;
        ApplyTheme();
        UpdateStatus();
    }

    /// <summary>Gets the score canvas at the center of the editor.</summary>
    public ScoreCanvas Canvas { get; }

    /// <summary>Gets the top menu, view, transport and zoom area.</summary>
    public Border TopBar { get; }

    /// <summary>Gets the notation palette panel.</summary>
    public Border LeftPanel { get; }

    /// <summary>Gets the property inspector panel.</summary>
    public Border RightPanel { get; }

    /// <summary>Gets the collapsible mixer and keyboard area.</summary>
    public Border BottomPanel { get; }

    /// <summary>Gets the cursor and selection status area.</summary>
    public Border StatusBar { get; }

    /// <summary>Gets the visible play or pause button.</summary>
    public Button PlayButton { get; private set; } = null!;

    /// <summary>Gets the visible stop button.</summary>
    public Button StopButton { get; private set; } = null!;

    /// <summary>Gets the primary note-writing control.</summary>
    public Button NoteEntryButton { get; private set; } = null!;

    /// <summary>Gets the page view toolbar button.</summary>
    public Button PageViewButton { get; private set; } = null!;

    /// <summary>Gets the continuous view toolbar button.</summary>
    public Button ContinuousViewButton { get; private set; } = null!;

    /// <summary>Gets the part view toolbar button.</summary>
    public Button PartViewButton { get; private set; } = null!;

    /// <summary>Gets the current label on the view selector button.</summary>
    public string ViewSelectorText => _toolbarVisuals["view.open-selector"].Caption.Text ?? string.Empty;

    /// <summary>Gets the main toolbar buttons keyed by their registered action identifier.</summary>
    public IReadOnlyDictionary<string, Button> ToolbarButtons => _toolbarButtons;

    /// <summary>Gets the visible caption of a toolbar action.</summary>
    /// <param name="actionId">The registered action identifier.</param>
    /// <returns>The caption shown below the action icon.</returns>
    public string ToolbarLabel(string actionId) => _toolbarVisuals.TryGetValue(actionId,
        out (TextBlock Icon, TextBlock Caption) visual) ? visual.Caption.Text ?? string.Empty : string.Empty;

    /// <summary>Gets whether a toolbar action currently has its selected visual state.</summary>
    /// <param name="actionId">The action identifier.</param>
    /// <returns>True when the action is highlighted.</returns>
    public bool IsToolbarActionActive(string actionId) => _activeToolbarActions.Contains(actionId);

    /// <summary>Gets the in-editor instruction for the current input mode.</summary>
    public string EntryGuideText => _entryGuide.Text ?? string.Empty;

    private TextBlock _entryGuide = null!;
    private StackPanel _inspectorEditors = null!;
    private Border _noticeBar = null!;
    private TextBlock _noticeText = null!;

    /// <summary>Gets whether controls for an individual selection are visible.</summary>
    public bool InspectorEditorVisible => _inspectorEditors.IsVisible;

    /// <summary>Gets the current action feedback shown beside the toolbar.</summary>
    public string NoticeText => _noticeText.Text ?? string.Empty;

    /// <summary>Gets whether a notation palette is expanded.</summary>
    public bool IsPaletteGroupOpen(string title) => _paletteGroups.TryGetValue(title, out var group) &&
        group.Items.IsVisible;

    /// <summary>Updates the transport button to match the active playback state.</summary>
    /// <param name="isPlaying">Whether playback is currently running.</param>
    public void SetPlaybackState(bool isPlaying)
    {
        if (_isPlaying == isPlaying)
        {
            return;
        }

        _isPlaying = isPlaying;
        SetToolbarContent("playback.toggle", isPlaying ? "Ⅱ" : "▶", isPlaying ? "Pausa" : "Reproducir");
        SetToolbarActionActive("playback.toggle", isPlaying);
    }

    /// <summary>Shows contextual feedback after a file or playback action.</summary>
    public void ShowNotice(string message, bool isError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _noticeText.Text = message;
        _noticeBar.Background = new SolidColorBrush(isError
            ? Color.Parse(_isDarkTheme ? "#533333" : "#FCE8E6")
            : Color.Parse(_isDarkTheme ? "#254B3F" : "#E3F4EA"));
        _noticeBar.IsVisible = true;
    }

    /// <summary>Dismisses the current feedback message.</summary>
    public void ClearNotice()
    {
        _noticeText.Text = string.Empty;
        _noticeBar.IsVisible = false;
    }

    /// <summary>Gets the current view selector.</summary>
    public Button ViewSelectorButton { get; private set; } = null!;

    /// <summary>Gets the currently selected score view.</summary>
    public ScoreViewMode CurrentView { get; private set; } = ScoreViewMode.Page;

    /// <summary>Gets the selected linked part when the editor is in part view.</summary>
    public ScorePartView? CurrentPart { get; private set; }

    /// <summary>Raised after the user changes the score view.</summary>
    public event Action<ScoreViewMode, ScorePartView?>? ViewChanged;

    /// <summary>Gets whether the left palette panel is expanded.</summary>
    public bool IsLeftPanelOpen => LeftPanel.IsVisible;

    /// <summary>Gets whether the right inspector panel is expanded.</summary>
    public bool IsRightPanelOpen => RightPanel.IsVisible;

    /// <summary>Gets whether the lower mixer area is expanded.</summary>
    public bool IsBottomPanelOpen => BottomPanel.IsVisible;

    /// <summary>Gets the visible status line.</summary>
    public string StatusText => _statusText.Text ?? string.Empty;

    /// <summary>Gets the selected event properties displayed by the inspector.</summary>
    public string InspectorDetailsText => _inspectorDetails.Text ?? string.Empty;

    /// <summary>Defines all commands exposed by the window chrome.</summary>
    public ImmutableArray<ActionDefinition> CreateActions()
    {
        ImmutableArray<ActionDefinition>.Builder actions = ImmutableArray.CreateBuilder<ActionDefinition>();
        actions.AddRange((ReadOnlySpan<ActionDefinition>)
        [
        new("command-palette.open", "Abrir paleta de comandos", "Ctrl+K", ToggleCommandPalette),
        new("file.new", "Nueva partitura", "Ctrl+N", () => _newScoreRequested()),
        new("file.open", "Abrir partitura", "Ctrl+O", () => _ = _openScoreRequested()),
        new("view.more", "Más opciones", "Ctrl+Shift+M", ToggleMore),
        new("file.open-menu", "Abrir opciones de archivo", "Ctrl+Shift+F", () => TogglePopup(_filePopup)),
        new("edit.open-menu", "Abrir opciones de edición", "Ctrl+Shift+E", ToggleMore),
        new("text.dynamic", "Escribir dinámica", "Shift+D", () => _textPopover?.Open(TextEntryKind.Dynamic)),
        new("text.tempo", "Escribir tempo", "Shift+T", () => _textPopover?.Open(TextEntryKind.Tempo)),
        new("text.text", "Escribir texto o cifrado", "Shift+X", () => _textPopover?.Open(TextEntryKind.Text)),
        new("text.lyric", "Escribir letra", "Shift+L", () => _textPopover?.Open(TextEntryKind.Lyric)),
        new("view.page", "Vista de página", "Ctrl+Shift+1", SelectPageView),
        new("view.continuous", "Vista continua", "Ctrl+Shift+3", SelectContinuousView),
        new("view.part", "Vista de parte", "Ctrl+Shift+4", SelectFirstPartView),
        new("view.open-menu", "Abrir opciones de vista", "Ctrl+Shift+V", () => TogglePopup(_viewSelectorPopup)),
        new("view.open-selector", "Abrir selector de vista", "Ctrl+Shift+2",
            () => TogglePopup(_viewSelectorPopup)),
        new("score.duration.open-selector", "Elegir duración de nota", "Ctrl+Shift+D",
            () => TogglePopup(_durationSelectorPopup)),
        new("view.toggle-left-panel", "Mostrar u ocultar paletas", "Ctrl+Shift+L",
            () => LeftPanel.IsVisible = !LeftPanel.IsVisible),
        new("view.toggle-right-panel", "Mostrar u ocultar inspector", "Ctrl+Shift+R",
            () => RightPanel.IsVisible = !RightPanel.IsVisible),
        new("view.toggle-bottom-panel", "Mostrar u ocultar panel inferior", "Ctrl+Shift+B",
            () => BottomPanel.IsVisible = !BottomPanel.IsVisible),
        new("view.toggle-theme", "Cambiar tema claro u oscuro", "Ctrl+Shift+T", ToggleTheme),
        new("inspector.duration.whole", "Cambiar duración a redonda", "Alt+1",
            () => _input.ChangeSelectedDuration(new Duration(NoteValue.Whole, 0))),
        new("inspector.duration.half", "Cambiar duración a blanca", "Alt+2",
            () => _input.ChangeSelectedDuration(new Duration(NoteValue.Half, 0))),
        new("inspector.duration.quarter", "Cambiar duración a negra", "Alt+3",
            () => _input.ChangeSelectedDuration(new Duration(NoteValue.Quarter, 0))),
        new("inspector.duration.eighth", "Cambiar duración a corchea", "Alt+4",
            () => _input.ChangeSelectedDuration(new Duration(NoteValue.Eighth, 0))),
        new("inspector.duration.sixteenth", "Cambiar duración a semicorchea", "Alt+5",
            () => _input.ChangeSelectedDuration(new Duration(NoteValue.Sixteenth, 0))),
        new("inspector.dot.toggle", "Alternar puntillo", "Alt+6", ToggleSelectedDot),
        new("inspector.alteration.flat", "Aplicar bemol a la selección", "Alt+7",
            () => SetSelectedAlteration(-1)),
        new("inspector.alteration.natural", "Aplicar becuadro a la selección", "Alt+8",
            () => SetSelectedAlteration(0)),
        new("inspector.alteration.sharp", "Aplicar sostenido a la selección", "Alt+9",
            () => SetSelectedAlteration(1)),
        new("inspector.tie.toggle", "Alternar ligadura de unión", "Alt+0",
            () => _input.ToggleSelectedTie()),
        ]);

        (Step Step, string Name, string Shortcut)[] pitches =
        [
            (Step.C, "Do", "Meta+Alt+Shift+C"), (Step.D, "Re", "Meta+Alt+Shift+D"),
            (Step.E, "Mi", "Meta+Alt+Shift+E"), (Step.F, "Fa", "Meta+Alt+Shift+F"),
            (Step.G, "Sol", "Meta+Alt+Shift+G"), (Step.A, "La", "Meta+Alt+Shift+A"),
            (Step.B, "Si", "Meta+Alt+Shift+B"),
        ];
        foreach ((Step step, string name, string shortcut) in pitches)
        {
            actions.Add(new ActionDefinition($"inspector.pitch.{step.ToString().ToLowerInvariant()}",
                $"Cambiar la nota seleccionada a {name}", shortcut,
                () => _input.ChangeSelectedPitchStep(step)));
        }

        for (int partIndex = 0; partIndex < _availableParts.Length; partIndex++)
        {
            ScorePartView part = _availableParts[partIndex];
            actions.Add(new ActionDefinition($"view.part.select.{partIndex}",
                $"Vista de parte: {part.Name}", GetPartShortcut(partIndex),
                () => SelectPartView(part)));
        }

        (string Title, string Id)[] paletteCategories =
        [
            ("Claves", "clefs"), ("Armaduras", "key-signatures"),
            ("Compases", "meters"), ("Alteraciones", "accidentals"),
            ("Dinámicas", "dynamics"), ("Articulaciones", "articulations"),
            ("Líneas", "lines"), ("Texto", "text"),
        ];
        for (int category = 0; category < paletteCategories.Length; category++)
        {
            (string title, string id) = paletteCategories[category];
            actions.Add(new ActionDefinition($"palette.group.{id}", $"Mostrar u ocultar {title}",
                $"Meta+Alt+Shift+F{category + 1}", () => TogglePaletteGroup(title)));
        }

        int shortcutIndex = 0;
        (Clef Clef, string Label, string Id)[] clefs =
        [
            (Clef.Treble, "Sol", "palette.clef.treble"),
            (Clef.Bass, "Fa", "palette.clef.bass"),
            (Clef.Alto, "Do alto", "palette.clef.alto"),
            (Clef.Tenor, "Do tenor", "palette.clef.tenor"),
        ];
        foreach ((Clef clef, string label, string id) in clefs)
        {
            string shortcut = CreatePaletteShortcut(shortcutIndex++);
            actions.Add(new ActionDefinition(id, $"Aplicar clave de {label}", shortcut,
                () => _input.ChangeSelectedClef(clef)));
        }

        for (int fifths = -7; fifths <= 7; fifths++)
        {
            string id = fifths switch
            {
                < 0 => $"palette.key.flats.{-fifths}",
                > 0 => $"palette.key.sharps.{fifths}",
                _ => "palette.key.c",
            };
            string label = GetKeySignatureLabel(fifths);
            string description = fifths switch
            {
                < 0 => $"Aplicar armadura de {-fifths} bemoles ({label})",
                > 0 => $"Aplicar armadura de {fifths} sostenidos ({label})",
                _ => "Aplicar armadura de Do mayor o La menor",
            };
            string shortcut = CreatePaletteShortcut(shortcutIndex++);
            KeySignature keySignature = new(fifths);
            actions.Add(new ActionDefinition(id, description, shortcut,
                () => _input.ChangeSelectedKeySignature(keySignature)));
        }

        (TimeSignature TimeSignature, string Label)[] meters =
        [
            (new TimeSignature(2, 2), "2/2"),
            (new TimeSignature(2, 4), "2/4"),
            (new TimeSignature(3, 4), "3/4"),
            (new TimeSignature(4, 4), "4/4"),
            (new TimeSignature(6, 8), "6/8"),
            (new TimeSignature(9, 8), "9/8"),
            (new TimeSignature(12, 8), "12/8"),
        ];
        foreach ((TimeSignature timeSignature, string label) in meters)
        {
            string id = $"palette.meter.{label.Replace('/', '-')}";
            string shortcut = CreatePaletteShortcut(shortcutIndex++);
            actions.Add(new ActionDefinition(id, $"Aplicar compás {label}", shortcut,
                () => _input.ChangeSelectedTimeSignature(timeSignature)));
        }

        (int Alteration, string Label, string Id)[] accidentals =
        [
            (-1, "bemol", "palette.accidental.flat"),
            (0, "becuadro", "palette.accidental.natural"),
            (1, "sostenido", "palette.accidental.sharp"),
        ];
        foreach ((int alteration, string label, string id) in accidentals)
        {
            string shortcut = CreatePaletteShortcut(shortcutIndex++);
            actions.Add(new ActionDefinition(id, $"Aplicar {label} a la selección", shortcut,
                () => SetSelectedAlteration(alteration)));
        }

        return actions.ToImmutable();
    }

    /// <summary>Connects visible controls to the central action registry.</summary>
    public void AttachActionRegistry(ActionRegistry actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (_actions is not null)
        {
            throw new InvalidOperationException("An action registry is already attached to the window.");
        }

        _actions = actions;
        _commandPalette = new CommandPalette(actions, () => Canvas.Focus());
        Grid.SetRowSpan(_commandPalette, 4);
        Grid.SetColumnSpan(_commandPalette, 3);
        _popupSurfaces.Add(_commandPalette);
        _layout.Children.Add(_commandPalette);
        _textPopover = new TextPopover(_input, () => Canvas.Focus());
        Grid.SetRowSpan(_textPopover, 4);
        Grid.SetColumnSpan(_textPopover, 3);
        _popupSurfaces.Add(_textPopover);
        _layout.Children.Add(_textPopover);
        ApplyTheme();
    }

    /// <summary>Gets the text popover, available once an action registry is attached.</summary>
    public TextPopover? TextPopover => _textPopover;

    /// <summary>Gets the command palette, available once an action registry is attached.</summary>
    public CommandPalette? CommandPalette => _commandPalette;

    private void ToggleCommandPalette()
    {
        if (_commandPalette is null)
        {
            return;
        }

        if (_commandPalette.IsOpen)
        {
            _commandPalette.Close();
        }
        else
        {
            _commandPalette.Open();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _input.StateChanged -= OnInputStateChanged;

    private Border CreateTopBar()
    {
        StackPanel ribbon = new() { Orientation = Orientation.Horizontal };
        AddToolbarGroup(ribbon, ("file.open-menu", "▣", "Archivo", "Nueva, abrir, guardar o exportar", false));
        AddToolbarGroup(ribbon,
            ("score.selection-mode", "↖", "Seleccionar", "Seleccionar elementos (Esc)", false),
            ("score.note-entry", "♫", "Nota", "Escribir notas (N)", false));
        AddToolbarGroup(ribbon, ("playback.toggle", "▶", "Reproducir", "Reproducir o pausar", false));
        AddToolbarGroup(ribbon,
            ("view.zoom-out", "−", "Reducir", "Reducir el zoom", false),
            ("view.fit-page", "↔", "Ajustar", "Ajustar la página a la ventana", false),
            ("view.zoom-in", "+", "Ampliar", "Aumentar el zoom", false));
        AddToolbarGroup(ribbon, ("view.open-selector", "▤", "Una página ▾",
            "Seleccionar una vista o una parte", false));
        AddToolbarGroup(ribbon, ("score.duration.open-selector", "♩", "Negra ▾",
            "Elegir la duración para las notas nuevas", false));
        AddToolbarGroup(ribbon, ("view.more", "⋯", "Más", "Deshacer, borrar y otras opciones", false));

        PlayButton = _toolbarButtons["playback.toggle"];
        StopButton = CreateButton("Detener", "Detener la reproducción", "playback.stop");
        NoteEntryButton = _toolbarButtons["score.note-entry"];
        Button fileButton = _toolbarButtons["file.open-menu"];
        _filePopup = CreatePopup(fileButton,
        [
            CreateButton("Nueva partitura", "Crear una partitura", "file.new"),
            CreateButton("Abrir…", "Abrir una partitura .tess", "file.open"),
            CreateButton("Guardar", "Guardar la partitura", "file.save"),
            CreateButton("Guardar como…", "Guardar con otro nombre", "file.save-as"),
            CreateButton("Exportar PDF…", "Exportar la partitura", "file.export-pdf"),
            CreateButton("Cerrar partitura", "Volver al inicio", "file.close"),
        ]);

        ViewSelectorButton = _toolbarButtons["view.open-selector"];
        PageViewButton = CreateButton("Una página", "Vista de página", "view.page");
        ContinuousViewButton = CreateButton("Continua", "Vista continua", "view.continuous");
        PartViewButton = CreateButton("Parte", "Vista de la primera parte", "view.part",
            _availableParts.Length > 0);
        _viewSelectorPopup = CreatePopup(ViewSelectorButton,
        [
            PageViewButton,
            ContinuousViewButton,
            PartViewButton,
            .. CreatePartViewButtons(),
        ]);

        Button durationButton = _toolbarButtons["score.duration.open-selector"];
        _durationSelectorPopup = CreatePopup(durationButton,
        [
            CreateButton("Redonda", "Duración de redonda", "score.duration.whole"),
            CreateButton("Blanca", "Duración de blanca", "score.duration.half"),
            CreateButton("Negra", "Duración de negra", "score.duration.quarter"),
            CreateButton("Corchea", "Duración de corchea", "score.duration.eighth"),
            CreateButton("Semicorchea", "Duración de semicorchea", "score.duration.sixteenth"),
        ]);

        Button moreButton = _toolbarButtons["view.more"];
        _morePopup = CreatePopup(moreButton,
        [
            CreateButton("Deshacer", "Deshacer la última edición", "score.undo"),
            CreateButton("Rehacer", "Rehacer la última edición", "score.redo"),
            CreateButton("Borrar nota", "Reemplazar la nota seleccionada por un silencio", "score.delete"),
            CreateButton("Copiar", "Copiar la selección", "edit.copy"),
            CreateButton("Pegar", "Pegar en el cursor", "edit.paste"),
            CreateButton("Detener reproducción", "Detener la reproducción", "playback.stop"),
            CreateButton("Paleta de comandos…", "Buscar cualquier acción (Ctrl+K)", "command-palette.open"),
            CreateButton("Cambiar tema", "Alternar tema claro y oscuro", "view.toggle-theme"),
            CreateButton("Paletas", "Mostrar u ocultar las paletas", "view.toggle-left-panel"),
            CreateButton("Inspector", "Mostrar u ocultar el inspector", "view.toggle-right-panel"),
            CreateButton("Mezclador", "Mostrar u ocultar el mezclador", "view.toggle-bottom-panel"),
            CreateButton("Cerrar partitura", "Volver al inicio", "file.close"),
        ]);
        _entryGuide = CreateLabel("", 12);
        _entryGuide.VerticalAlignment = VerticalAlignment.Center;
        _entryGuide.TextWrapping = TextWrapping.Wrap;
        _noticeText = CreateLabel("", 12);
        _noticeText.TextWrapping = TextWrapping.Wrap;
        _noticeBar = new Border { Child = _noticeText, Padding = new Thickness(8, 5),
            CornerRadius = new CornerRadius(4), IsVisible = false };
        ScrollViewer toolbarScroll = new()
        {
            Content = ribbon,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Height = 58,
        };
        StackPanel content = new() { Spacing = 3, Children = { toolbarScroll, _entryGuide, _noticeBar } };
        return new Border { Child = content, Padding = new Thickness(5, 4) };
    }

    private void AddToolbarGroup(StackPanel ribbon,
        params (string ActionId, string Icon, string Label, string Hint, bool RequiresNote)[] items)
    {
        StackPanel group = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach ((string actionId, string icon, string label, string hint, bool requiresNote) in items)
        {
            Button button = CreateToolbarButton(actionId, icon, label, hint, requiresNote);
            group.Children.Add(button);
            if (actionId == "view.page") PageViewButton = button;
            if (actionId == "view.continuous") ContinuousViewButton = button;
            if (actionId == "view.part") PartViewButton = button;
        }

        Border separator = new()
        {
            Child = group,
            Padding = new Thickness(3, 1, 7, 1),
            BorderThickness = new Thickness(0, 0, 1, 0),
        };
        _toolbarSeparators.Add(separator);
        ribbon.Children.Add(separator);
    }

    private Button CreateToolbarButton(string actionId, string icon, string label, string hint, bool requiresNote)
    {
        TextBlock iconText = CreateLabel(icon, 20, FontWeight.SemiBold);
        iconText.HorizontalAlignment = HorizontalAlignment.Center;
        TextBlock caption = CreateLabel(label, 10);
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.MaxWidth = 72;
        StackPanel content = new() { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Children = { iconText, caption } };
        Button button = new()
        {
            Content = content,
            Width = 74,
            Height = 54,
            Padding = new Thickness(2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, hint);
        button.Click += (_, _) => Execute(actionId);
        _themedControls.Add(button);
        _toolbarButtons.Add(actionId, button);
        _toolbarVisuals.Add(actionId, (iconText, caption));
        _toolbarRequirements.Add(actionId, requiresNote);
        return button;
    }

    private void SetToolbarContent(string actionId, string icon, string caption)
    {
        if (_toolbarVisuals.TryGetValue(actionId, out (TextBlock Icon, TextBlock Caption) visual))
        {
            visual.Icon.Text = icon;
            visual.Caption.Text = caption;
        }
    }

    private void SetToolbarActionActive(string actionId, bool active)
    {
        if (!_toolbarButtons.TryGetValue(actionId, out Button? button))
        {
            return;
        }

        if (active) _activeToolbarActions.Add(actionId);
        else _activeToolbarActions.Remove(actionId);

        if (active)
        {
            SolidColorBrush accent = new(Color.Parse("#176DB0"));
            button.Background = accent;
            button.Foreground = new SolidColorBrush(Colors.White);
            button.BorderBrush = new SolidColorBrush(Color.Parse("#429BE0"));
            button.BorderThickness = new Thickness(1);
            button.FontWeight = FontWeight.Bold;
            if (_toolbarVisuals.TryGetValue(actionId, out (TextBlock Icon, TextBlock Caption) visual))
            {
                visual.Icon.Foreground = new SolidColorBrush(Colors.White);
                visual.Caption.Foreground = new SolidColorBrush(Colors.White);
            }
        }
        else
        {
            button.ClearValue(TemplatedControl.BackgroundProperty);
            button.ClearValue(TemplatedControl.ForegroundProperty);
            button.ClearValue(TemplatedControl.BorderBrushProperty);
            button.BorderThickness = new Thickness(0);
            button.FontWeight = FontWeight.Normal;
            if (_toolbarVisuals.TryGetValue(actionId, out (TextBlock Icon, TextBlock Caption) visual))
            {
                IBrush text = new SolidColorBrush(Color.Parse(_isDarkTheme ? "#EDF0F4" : "#202834"));
                visual.Icon.Foreground = text;
                visual.Caption.Foreground = text;
            }
        }
    }

    private void UpdateToolbarState(ScoreEventProperties? properties)
    {
        bool hasSelectedNote = properties is { HasSelectedNote: true };
        foreach ((string actionId, bool requiresNote) in _toolbarRequirements)
        {
            Button button = _toolbarButtons[actionId];
            button.IsEnabled = !requiresNote || hasSelectedNote;
            button.Opacity = button.IsEnabled ? 1 : 0.48;
        }

        bool writing = _input.Mode == ScoreInputMode.NoteEntry;
        SetToolbarActionActive("score.note-entry", writing);
        SetToolbarActionActive("score.selection-mode", !writing);
        SetToolbarContent("score.duration.open-selector",
            GetDurationIcon(_input.CurrentDuration.Value), $"{GetDurationLabel(_input.CurrentDuration.Value)} ▾");
        int? alteration = hasSelectedNote ? properties!.Notes[0].Pitch.Alter : null;
        SetToolbarActionActive("inspector.alteration.flat", alteration == -1);
        SetToolbarActionActive("inspector.alteration.natural", alteration == 0);
        SetToolbarActionActive("inspector.alteration.sharp", alteration == 1);
        bool tied = hasSelectedNote && properties!.Notes[0].TiedToNext;
        SetToolbarActionActive("score.tie.toggle", tied);
    }

    private Border CreateNotationPalettePanel()
    {
        StackPanel stack = new() { Spacing = 9, Margin = new Thickness(14, 14, 10, 10) };
        stack.Children.Add(CreateLabel("Paletas", 17, FontWeight.SemiBold));
        AddPaletteGroup(stack, "Claves",
        [
            ("Sol", "palette.clef.treble"),
            ("Fa", "palette.clef.bass"),
            ("Do alto", "palette.clef.alto"),
            ("Do tenor", "palette.clef.tenor"),
        ]);
        (string Label, string ActionId)[] keySignatures = new (string, string)[15];
        for (int fifths = -7; fifths <= 7; fifths++)
        {
            string actionId = fifths switch
            {
                < 0 => $"palette.key.flats.{-fifths}",
                > 0 => $"palette.key.sharps.{fifths}",
                _ => "palette.key.c",
            };
            keySignatures[fifths + 7] = (GetKeySignatureLabel(fifths), actionId);
        }

        AddPaletteGroup(stack, "Armaduras", keySignatures);
        AddPaletteGroup(stack, "Compases",
        [
            ("2/2", "palette.meter.2-2"),
            ("2/4", "palette.meter.2-4"),
            ("3/4", "palette.meter.3-4"),
            ("4/4", "palette.meter.4-4"),
            ("6/8", "palette.meter.6-8"),
            ("9/8", "palette.meter.9-8"),
            ("12/8", "palette.meter.12-8"),
        ]);
        AddPaletteGroup(stack, "Alteraciones",
        [
            ("♭", "palette.accidental.flat"),
            ("♮", "palette.accidental.natural"),
            ("♯", "palette.accidental.sharp"),
        ]);
        AddPaletteGroup(stack, "Dinámicas",
        [
            ("Dinámica…", "text.dynamic"),
            ("Tempo…", "text.tempo"),
        ]);
        AddPaletteGroup(stack, "Articulaciones",
        [
            ("Staccato", "articulation.staccato"),
            ("Tenuto", "articulation.tenuto"),
            ("Acento", "articulation.accent"),
            ("Marcato", "articulation.marcato"),
            ("Calderón", "articulation.fermata"),
            ("Trino", "articulation.trill"),
        ]);
        AddPaletteGroup(stack, "Líneas",
        [
            ("Crescendo", "spanner.crescendo"),
            ("Diminuendo", "spanner.diminuendo"),
            ("8va", "spanner.octaveup"),
            ("8vb", "spanner.octavedown"),
            ("Pedal", "spanner.pedal"),
            ("Ligadura", "spanner.slur"),
        ]);
        AddPaletteGroup(stack, "Texto",
        [
            ("Texto…", "text.text"),
            ("Letra…", "text.lyric"),
        ]);

        return new Border { Width = 220, Child = new ScrollViewer { Content = stack } };
    }

    private void AddPaletteGroup(StackPanel parent, string title,
        (string Label, string ActionId)[] items)
    {
        string id = title switch
        {
            "Claves" => "clefs", "Armaduras" => "key-signatures",
            "Compases" => "meters", "Alteraciones" => "accidentals",
            "Dinámicas" => "dynamics", "Articulaciones" => "articulations",
            "Líneas" => "lines", "Texto" => "text",
            _ => throw new ArgumentOutOfRangeException(nameof(title)),
        };
        Button header = CreateButton($"▸  {title}", $"Mostrar u ocultar {title}", $"palette.group.{id}");
        header.HorizontalAlignment = HorizontalAlignment.Stretch;
        header.HorizontalContentAlignment = HorizontalAlignment.Left;
        header.FontWeight = FontWeight.SemiBold;
        parent.Children.Add(header);
        WrapPanel row = new() { Orientation = Orientation.Horizontal };
        foreach ((string label, string actionId) in items)
        {
            Button button = CreateButton(label, label, actionId);
            button.MinWidth = 42;
            button.MinHeight = 32;
            button.Margin = new Thickness(1);
            button.Padding = new Thickness(6, 2);
            row.Children.Add(button);
        }

        row.IsVisible = false;
        parent.Children.Add(row);
        _paletteGroups.Add(title, (header, row));
    }

    private void TogglePaletteGroup(string title)
    {
        (Button header, WrapPanel items) = _paletteGroups[title];
        items.IsVisible = !items.IsVisible;
        header.Content = $"{(items.IsVisible ? "▾" : "▸")}  {title}";
    }

    private static string CreatePaletteShortcut(int index)
    {
        const string digits = "1234567890";
        if (index < 0 || index >= digits.Length + 26)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        char key = index < digits.Length ? digits[index] : (char)('A' + index - digits.Length);
        return $"Ctrl+Alt+Shift+{key}";
    }

    private static string GetKeySignatureLabel(int fifths) => fifths switch
    {
        -7 => "Do♭",
        -6 => "Sol♭",
        -5 => "Re♭",
        -4 => "La♭",
        -3 => "Mi♭",
        -2 => "Si♭",
        -1 => "Fa",
        0 => "Do",
        1 => "Sol",
        2 => "Re",
        3 => "La",
        4 => "Mi",
        5 => "Si",
        6 => "Fa♯",
        7 => "Do♯",
        _ => throw new ArgumentOutOfRangeException(nameof(fifths)),
    };

    private Border CreateInspectorPanel(out TextBlock inspectorText, out TextBlock inspectorDetails)
    {
        StackPanel stack = new() { Spacing = 10, Margin = new Thickness(14) };
        stack.Children.Add(CreateLabel("Inspector", 17, FontWeight.SemiBold));
        inspectorText = CreateLabel("Sin selección", 13, FontWeight.SemiBold);
        stack.Children.Add(inspectorText);
        inspectorDetails = CreateLabel("Para editar una nota o un silencio, haz clic sobre el pentagrama.", 13);
        inspectorDetails.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(inspectorDetails);
        _inspectorEditors = new StackPanel { Spacing = 10, IsVisible = false };
        _inspectorEditors.Children.Add(CreateLabel("Altura", 14, FontWeight.SemiBold));
        _inspectorEditors.Children.Add(CreateInspectorPitchButtons());
        TextBlock pitchHint = CreateLabel("⌘+↑/↓: un paso · ⇧⌘+↑/↓: una octava", 10);
        pitchHint.TextWrapping = TextWrapping.Wrap;
        _inspectorEditors.Children.Add(pitchHint);
        _inspectorEditors.Children.Add(CreateLabel("Duración", 14, FontWeight.SemiBold));
        _inspectorEditors.Children.Add(CreateInspectorButtonRow(
        [
            ("Redonda", "inspector.duration.whole", false),
            ("Blanca", "inspector.duration.half", false),
            ("Negra", "inspector.duration.quarter", false),
        ]));
        _inspectorEditors.Children.Add(CreateInspectorButtonRow(
        [
            ("Corchea", "inspector.duration.eighth", false),
            ("16.ª", "inspector.duration.sixteenth", false),
            ("Puntillo", "inspector.dot.toggle", false),
        ]));
        _inspectorEditors.Children.Add(CreateLabel("Nota", 14, FontWeight.SemiBold));
        _inspectorEditors.Children.Add(CreateInspectorButtonRow(
        [
            ("♭", "inspector.alteration.flat", true),
            ("♮", "inspector.alteration.natural", true),
            ("♯", "inspector.alteration.sharp", true),
            ("Ligadura", "inspector.tie.toggle", true),
        ]));
        stack.Children.Add(_inspectorEditors);
        return new Border { Width = 240, Child = new ScrollViewer { Content = stack } };
    }

    private Control CreateInspectorPitchButtons()
    {
        (Step Step, string Name)[] pitches =
        [
            (Step.C, "Do"), (Step.D, "Re"), (Step.E, "Mi"), (Step.F, "Fa"),
            (Step.G, "Sol"), (Step.A, "La"), (Step.B, "Si"),
        ];
        WrapPanel row = new() { Orientation = Orientation.Horizontal };
        foreach ((Step step, string name) in pitches)
        {
            string actionId = $"inspector.pitch.{step.ToString().ToLowerInvariant()}";
            Button button = CreateButton(name, $"Cambiar la nota seleccionada a {name}", actionId, false);
            button.MinWidth = 28;
            button.Padding = new Thickness(3, 2);
            _inspectorPitchButtons.Add((button, step));
            row.Children.Add(button);
        }

        return row;
    }

    private Control CreateInspectorButtonRow((string Label, string ActionId, bool RequiresNote)[] items)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 3 };
        foreach ((string label, string actionId, bool requiresNote) in items)
        {
            Button button = CreateButton(label, label, actionId, false);
            _inspectorButtons.Add((button, requiresNote));
            row.Children.Add(button);
        }

        return row;
    }

    /// <summary>Shows one mixer strip per instrument (up to nine) in the lower panel; each control runs a registered action.</summary>
    /// <param name="instrumentNames">The instrument names in score order.</param>
    public void BuildMixerStrips(IReadOnlyList<string> instrumentNames)
    {
        ArgumentNullException.ThrowIfNull(instrumentNames);
        StackPanel stack = (StackPanel)BottomPanel.Child!;
        while (stack.Children.Count > 1)
        {
            stack.Children.RemoveAt(1);
        }

        for (int i = 0; i < Math.Min(instrumentNames.Count, 9); i++)
        {
            StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(CreateLabel(instrumentNames[i], 13));
            row.Children.Add(CreateButton("Silencio", "Silenciar", $"mixer.mute.{i + 1}"));
            row.Children.Add(CreateButton("Solo", "Solo", $"mixer.solo.{i + 1}"));
            row.Children.Add(CreateButton("−", "Bajar volumen", $"mixer.volume-down.{i + 1}"));
            row.Children.Add(CreateButton("+", "Subir volumen", $"mixer.volume-up.{i + 1}"));
            stack.Children.Add(row);
        }

        ApplyTheme();
    }

    private Border CreateBottomPanel()
    {
        StackPanel stack = new() { Margin = new Thickness(14), Spacing = 10 };
        stack.Children.Add(CreateLabel("Mezclador  ·  Teclado de piano", 15, FontWeight.SemiBold));
        return new Border { MinHeight = 170, Child = stack };
    }

    private Border CreateStatusBar(out TextBlock status)
    {
        status = CreateLabel(string.Empty, 12);
        status.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Height = 30, Child = status, Padding = new Thickness(12, 0) };
    }

    private TextBlock CreateLabel(string text, double size, FontWeight weight = FontWeight.Normal)
    {
        TextBlock label = new() { Text = text, FontSize = size, FontWeight = weight };
        _themedControls.Add(label);
        return label;
    }

    private Button CreateButton(string text, string hint, string? actionId, bool enabled = true)
    {
        Button button = new() { Content = text, IsEnabled = enabled,
            MinWidth = 30, Padding = new Thickness(8, 4) };
        button.Opacity = enabled ? 1 : 0.45;
        ToolTip.SetTip(button, hint);
        if (actionId is not null)
        {
            button.Click += (_, _) => Execute(actionId);
        }

        _themedControls.Add(button);
        return button;
    }

    private Popup CreatePopup(Control anchor, Button[] items)
    {
        StackPanel menu = new() { Spacing = 2 };
        foreach (Button item in items)
        {
            item.HorizontalAlignment = HorizontalAlignment.Stretch;
            menu.Children.Add(item);
        }

        Border surface = new() { Child = menu, Padding = new Thickness(8),
            BorderThickness = new Thickness(1) };
        _popupSurfaces.Add(surface);
        return new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom,
            Child = surface };
    }

    private static void TogglePopup(Popup? popup)
    {
        if (popup is not null)
        {
            popup.IsOpen = !popup.IsOpen;
        }
    }

    private void ToggleMore() => TogglePopup(_morePopup);

    private void CloseMenus()
    {
        if (_filePopup is not null) _filePopup.IsOpen = false;
        if (_viewSelectorPopup is not null) _viewSelectorPopup.IsOpen = false;
        if (_durationSelectorPopup is not null) _durationSelectorPopup.IsOpen = false;
        if (_morePopup is not null) _morePopup.IsOpen = false;
    }

    private void Execute(string actionId)
    {
        if (_actions?.TryExecute(actionId) == true)
        {
            if (actionId is "file.open-menu" or "edit.open-menu" or "view.open-menu" or
                "view.open-selector" or "score.duration.open-selector" or "view.more")
            {
                return;
            }

            CloseMenus();

            if (actionId is not "file.new" and not "file.open" &&
                !actionId.StartsWith("text.", StringComparison.Ordinal))
            {
                Canvas.Focus();
            }
        }
    }

    private Button[] CreatePartViewButtons()
    {
        Button[] buttons = new Button[_availableParts.Length];
        for (int partIndex = 0; partIndex < _availableParts.Length; partIndex++)
        {
            ScorePartView part = _availableParts[partIndex];
            buttons[partIndex] = CreateButton(part.Name, $"Vista de parte: {part.Name}",
                $"view.part.select.{partIndex}");
        }

        return buttons;
    }

    private void SelectPageView() => SetView(ScoreViewMode.Page, null);

    private void SelectContinuousView() => SetView(ScoreViewMode.Continuous, null);

    private void SelectFirstPartView()
    {
        if (!_availableParts.IsDefaultOrEmpty)
        {
            SelectPartView(_availableParts[0]);
        }
    }

    private void SelectPartView(ScorePartView part) => SetView(ScoreViewMode.Part, part);

    private void SetView(ScoreViewMode view, ScorePartView? part)
    {
        CurrentView = view;
        CurrentPart = part;
        SetToolbarContent("view.open-selector", "▤", view switch
        {
            ScoreViewMode.Page => "Una página ▾",
            ScoreViewMode.Continuous => "Continua ▾",
            ScoreViewMode.Part => $"{part?.Name ?? "Parte"} ▾",
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        });
        UpdateToolbarState(_input.SelectedEventProperties);
        ViewChanged?.Invoke(view, part);
        Canvas.Focus();
    }

    private static ImmutableArray<ScorePartView> GetAvailableParts(Score score)
    {
        ImmutableArray<ScorePartView> savedParts = score.PartList;
        if (!savedParts.IsDefaultOrEmpty)
        {
            ImmutableArray<ScorePartView>.Builder validParts = ImmutableArray.CreateBuilder<ScorePartView>();
            foreach (ScorePartView part in savedParts)
            {
                if (part.InstrumentIndices.Length == 1 &&
                    part.InstrumentIndices[0] < score.Instruments.Length)
                {
                    validParts.Add(part);
                }
            }

            if (validParts.Count > 0)
            {
                return validParts.ToImmutable();
            }
        }

        ImmutableArray<ScorePartView>.Builder parts = ImmutableArray.CreateBuilder<ScorePartView>(
            score.Instruments.Length);
        for (int index = 0; index < score.Instruments.Length; index++)
        {
            parts.Add(new ScorePartView(score.Instruments[index].Name, [index]));
        }

        return parts.MoveToImmutable();
    }

    private static string GetPartShortcut(int partIndex) => partIndex switch
    {
        < 12 => $"Ctrl+Alt+Shift+F{partIndex + 13}",
        < 22 => $"Ctrl+Alt+Shift+{partIndex - 12}",
        < 48 => $"Ctrl+Alt+Shift+{(char)('A' + partIndex - 22)}",
        _ => throw new InvalidOperationException("The part selector supports at most 48 keyboard actions."),
    };

    private void ToggleTheme()
    {
        _isDarkTheme = !_isDarkTheme;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (TopLevel.GetTopLevel(this) is TopLevel topLevel)
        {
            topLevel.RequestedThemeVariant = _isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        Color chrome = _isDarkTheme ? Color.Parse("#20252D") : Color.Parse("#F2F4F7");
        Color panel = _isDarkTheme ? Color.Parse("#282E38") : Color.Parse("#FFFFFF");
        Color text = _isDarkTheme ? Color.Parse("#EDF0F4") : Color.Parse("#202834");
        Color border = _isDarkTheme ? Color.Parse("#3B4552") : Color.Parse("#D6DCE4");
        _layout.Background = new SolidColorBrush(chrome);
        foreach (Border area in new[] { TopBar, LeftPanel, RightPanel, BottomPanel, StatusBar })
        {
            area.Background = new SolidColorBrush(panel);
            area.BorderBrush = new SolidColorBrush(border);
            area.BorderThickness = new Thickness(0, 0, 1, 1);
        }

        foreach (Border surface in _popupSurfaces)
        {
            surface.Background = new SolidColorBrush(panel);
            surface.BorderBrush = new SolidColorBrush(border);
        }

        foreach (Border separator in _toolbarSeparators)
        {
            separator.BorderBrush = new SolidColorBrush(border);
        }

        foreach (Control control in _themedControls)
        {
            switch (control)
            {
                case TextBlock label:
                    label.Foreground = new SolidColorBrush(text);
                    break;
                case TemplatedControl templated:
                    templated.Foreground = new SolidColorBrush(text);
                    break;
            }
        }

        UpdateToolbarState(_input.SelectedEventProperties);

        Canvas.WorkspaceColor = _isDarkTheme
            ? new SKColor(47, 52, 61)
            : new SKColor(222, 225, 230);
    }

    private void OnInputStateChanged(object? sender, EventArgs e) => UpdateStatus();

    private void UpdateStatus()
    {
        bool writing = _input.Mode == ScoreInputMode.NoteEntry;
        _entryGuide.Text = writing
            ? "Haz clic en el pentagrama para colocar una nota; C D E F G A B también funcionan. Esc selecciona."
            : "Para empezar, elige Nota y haz clic en el pentagrama; también puedes escribir C D E F G A B.";

        ScoreInputCursor cursor = _input.Cursor;
        Score score = _input.CurrentScore;
        int measureIndex = 0;
        Fraction measureStart = Fraction.Zero;
        for (; measureIndex < score.Measures.Length; measureIndex++)
        {
            Fraction measureEnd = measureStart + score.Measures[measureIndex].TimeSignature.Length;
            if (cursor.Position < measureEnd)
            {
                break;
            }

            measureStart = measureEnd;
        }

        bool cursorAtEnd = measureIndex == score.Measures.Length;
        TimeSignature signature = score.Measures[cursorAtEnd ? measureIndex - 1 : measureIndex].TimeSignature;
        Fraction beatPosition = (cursor.Position - measureStart) /
            new Fraction(1, signature.Denominator);
        Fraction beatNumber = beatPosition + Fraction.One;
        string mode = _input.Mode == ScoreInputMode.NoteEntry ? "Entrada" : "Selección";
        string duration = _input.CurrentDuration.Value switch
        {
            NoteValue.Whole => "Redonda",
            NoteValue.Half => "Blanca",
            NoteValue.Quarter => "Negra",
            NoteValue.Eighth => "Corchea",
            NoteValue.Sixteenth => "Semicorchea",
            _ => _input.CurrentDuration.Value.ToString(),
        };
        if (_input.CurrentDuration.Dots > 0)
        {
            duration += _input.CurrentDuration.Dots == 1 ? " con puntillo" : " con puntillos";
        }

        int selectedCount = _input.CurrentSelection.Items.Length;
        ScoreEventProperties? properties = _input.SelectedEventProperties;
        int displayedMeasure = cursorAtEnd
            ? score.Measures[^1].Number + 1
            : score.Measures[measureIndex].Number;
        _statusText.Text = $"{mode}  ·  {duration}  ·  Voz {cursor.VoiceNumber}  ·  " +
            $"Compás {displayedMeasure}  ·  Tiempo {beatNumber}  ·  " +
            $"Selección: {selectedCount}";
        _inspectorText.Text = properties is null
            ? selectedCount == 0 ? "Sin selección" : "Selección múltiple"
            : $"{properties.Kind}  ·  Compás {properties.MeasureNumber}  ·  Voz {properties.VoiceNumber}";
        _inspectorDetails.Text = properties is null
            ? selectedCount == 0
                ? "Para editar una nota o un silencio, haz clic sobre el pentagrama."
                : "Selecciona una sola nota para editar su duración o alteración."
            : FormatEventProperties(properties);
        bool hasSelection = properties is not null;
        _inspectorEditors.IsVisible = hasSelection;
        bool hasSelectedNote = properties is { HasSelectedNote: true };
        foreach ((Button button, bool requiresNote) in _inspectorButtons)
        {
            button.IsEnabled = hasSelection && (!requiresNote || hasSelectedNote);
            button.Opacity = button.IsEnabled ? 1 : 0.45;
        }

        Step? selectedStep = hasSelectedNote ? properties!.Notes[0].Pitch.Step : null;
        foreach ((Button button, Step step) in _inspectorPitchButtons)
        {
            button.IsEnabled = hasSelectedNote;
            button.Opacity = hasSelectedNote ? 1 : 0.45;
            button.FontWeight = selectedStep == step ? FontWeight.Bold : FontWeight.Normal;
            button.Background = selectedStep == step
                ? new SolidColorBrush(Color.Parse("#176DB0")) : null;
            button.Foreground = selectedStep == step || _isDarkTheme
                ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Color.Parse("#202834"));
        }

        UpdateToolbarState(properties);
    }

    private static string FormatEventProperties(ScoreEventProperties properties)
    {
        string duration = FormatDuration(properties.Duration);
        if (properties.Notes.IsDefaultOrEmpty)
        {
            return $"{properties.Kind}  ·  {duration}";
        }

        if (properties.Notes.Length == 1)
        {
            return $"{GetPitchName(properties.Notes[0].Pitch)}  ·  {duration}";
        }

        System.Text.StringBuilder pitches = new();
        foreach (Note note in properties.Notes)
        {
            if (pitches.Length > 0)
            {
                pitches.Append(", ");
            }

            pitches.Append(GetPitchName(note.Pitch));
        }

        return $"{pitches}  ·  {duration}";
    }

    private static string FormatDuration(Duration duration)
    {
        string name = duration.Value switch
        {
            NoteValue.Whole => "Redonda",
            NoteValue.Half => "Blanca",
            NoteValue.Quarter => "Negra",
            NoteValue.Eighth => "Corchea",
            NoteValue.Sixteenth => "Semicorchea",
            _ => duration.Value.ToString(),
        };
        return duration.Dots switch
        {
            0 => name,
            1 => name + " con puntillo",
            _ => name + " con " + duration.Dots + " puntillos",
        };
    }

    private static string GetPitchName(Pitch pitch)
    {
        string step = pitch.Step switch
        {
            Step.C => "Do",
            Step.D => "Re",
            Step.E => "Mi",
            Step.F => "Fa",
            Step.G => "Sol",
            Step.A => "La",
            Step.B => "Si",
            _ => pitch.Step.ToString(),
        };
        string alteration = pitch.Alter switch
        {
            -2 => " doble bemol",
            -1 => " bemol",
            0 => string.Empty,
            1 => " sostenido",
            2 => " doble sostenido",
            _ => $" alteración {pitch.Alter}",
        };
        return $"{step}{alteration} {pitch.Octave}";
    }

    private static string GetDurationLabel(NoteValue value) => value switch
    {
        NoteValue.Whole => "Redonda",
        NoteValue.Half => "Blanca",
        NoteValue.Quarter => "Negra",
        NoteValue.Eighth => "Corchea",
        NoteValue.Sixteenth => "Semicorchea",
        _ => value.ToString(),
    };

    private static string GetDurationIcon(NoteValue value) => value switch
    {
        NoteValue.Whole => "○",
        NoteValue.Half => "○│",
        NoteValue.Quarter => "♩",
        NoteValue.Eighth => "♪",
        NoteValue.Sixteenth => "♫",
        _ => "♪",
    };

    private void ToggleSelectedDot()
    {
        if (_input.SelectedEventProperties is ScoreEventProperties properties)
        {
            _input.ChangeSelectedDotCount(properties.Duration.Dots == 0 ? 1 : 0);
        }
    }

    private void SetSelectedAlteration(int alteration)
    {
        if (_input.SelectedEventProperties is not ScoreEventProperties { HasSelectedNote: true } properties)
        {
            return;
        }

        _input.ChangeSelectedAlteration(alteration);
    }
}
