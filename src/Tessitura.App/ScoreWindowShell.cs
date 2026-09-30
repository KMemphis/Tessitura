using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using SkiaSharp;
using Tessitura.Core;

namespace Tessitura.App;

/// <summary>Arranges the editor chrome around the score canvas.</summary>
public sealed class ScoreWindowShell : UserControl, IDisposable
{
    private readonly ScoreInputController _input;
    private readonly Grid _layout;
    private readonly TextBlock _statusText;
    private readonly TextBlock _inspectorText;
    private readonly List<Control> _themedControls = [];
    private readonly List<Border> _popupSurfaces = [];
    private Popup? _viewMenuPopup;
    private Popup? _viewSelectorPopup;
    private ActionRegistry? _actions;
    private bool _isDarkTheme = true;

    /// <summary>Creates the main editor layout around an existing score canvas.</summary>
    public ScoreWindowShell(ScoreCanvas canvas, ScoreInputController input)
    {
        Canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };

        TopBar = CreateTopBar();
        LeftPanel = CreateSidePanel("Paletas", ["Claves", "Armaduras", "Compases", "Alteraciones",
            "Dinámicas", "Articulaciones", "Líneas", "Texto"], 200);
        RightPanel = CreateInspectorPanel(out _inspectorText);
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

    /// <summary>Gets the visible file menu label.</summary>
    public Button FileMenuButton { get; private set; } = null!;

    /// <summary>Gets the current view selector.</summary>
    public Button ViewSelectorButton { get; private set; } = null!;

    /// <summary>Gets whether the left palette panel is expanded.</summary>
    public bool IsLeftPanelOpen => LeftPanel.IsVisible;

    /// <summary>Gets whether the right inspector panel is expanded.</summary>
    public bool IsRightPanelOpen => RightPanel.IsVisible;

    /// <summary>Gets whether the lower mixer area is expanded.</summary>
    public bool IsBottomPanelOpen => BottomPanel.IsVisible;

    /// <summary>Gets the visible status line.</summary>
    public string StatusText => _statusText.Text ?? string.Empty;

    /// <summary>Defines all commands exposed by the window chrome.</summary>
    public ImmutableArray<ActionDefinition> CreateActions() =>
    [
        new("view.page", "Vista de página", "Ctrl+Shift+1", () => Canvas.Focus()),
        new("view.open-menu", "Abrir menú Ver", "Ctrl+Shift+V", () => TogglePopup(_viewMenuPopup)),
        new("view.open-selector", "Abrir selector de vista", "Ctrl+Shift+2",
            () => TogglePopup(_viewSelectorPopup)),
        new("view.toggle-left-panel", "Mostrar u ocultar paletas", "Ctrl+Shift+L",
            () => LeftPanel.IsVisible = !LeftPanel.IsVisible),
        new("view.toggle-right-panel", "Mostrar u ocultar inspector", "Ctrl+Shift+R",
            () => RightPanel.IsVisible = !RightPanel.IsVisible),
        new("view.toggle-bottom-panel", "Mostrar u ocultar panel inferior", "Ctrl+Shift+B",
            () => BottomPanel.IsVisible = !BottomPanel.IsVisible),
        new("view.toggle-theme", "Cambiar tema claro u oscuro", "Ctrl+Shift+T", ToggleTheme),
    ];

    /// <summary>Connects visible controls to the central action registry.</summary>
    public void AttachActionRegistry(ActionRegistry actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (_actions is not null)
        {
            throw new InvalidOperationException("An action registry is already attached to the window.");
        }

        _actions = actions;
    }

    /// <inheritdoc />
    public void Dispose() => _input.StateChanged -= OnInputStateChanged;

    private Border CreateTopBar()
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center };
        FileMenuButton = CreateButton("Archivo", "Archivo", null, false);
        row.Children.Add(FileMenuButton);
        row.Children.Add(CreateButton("Editar", "Editar", null, false));
        Button viewMenu = CreateButton("Ver ▾", "Abrir menú Ver", "view.open-menu");
        row.Children.Add(viewMenu);
        _viewMenuPopup = CreatePopup(viewMenu,
        [
            CreateButton("Acercar", "Acercar", "view.zoom-in"),
            CreateButton("Alejar", "Alejar", "view.zoom-out"),
            CreateButton("Ajustar página", "Ajustar página", "view.fit-page"),
            CreateButton("Paletas", "Paletas", "view.toggle-left-panel"),
            CreateButton("Inspector", "Inspector", "view.toggle-right-panel"),
            CreateButton("Panel inferior", "Panel inferior", "view.toggle-bottom-panel"),
            CreateButton("Tema claro/oscuro", "Tema claro/oscuro", "view.toggle-theme"),
        ]);
        row.Children.Add(_viewMenuPopup);

        ViewSelectorButton = CreateButton("Página ▾", "Seleccionar vista", "view.open-selector");
        row.Children.Add(ViewSelectorButton);
        _viewSelectorPopup = CreatePopup(ViewSelectorButton,
        [
            CreateButton("Página", "Vista de página", "view.page"),
            CreateButton("Continua", "Disponible en una fase posterior", null, false),
            CreateButton("Parte", "Disponible en una fase posterior", null, false),
        ]);
        row.Children.Add(_viewSelectorPopup);
        row.Children.Add(CreateButton("◀", "Reproducción anterior", null, false));
        row.Children.Add(CreateButton("▶", "Reproducir", null, false));
        row.Children.Add(CreateButton("■", "Detener", null, false));
        row.Children.Add(CreateButton("−", "Alejar", "view.zoom-out"));
        row.Children.Add(CreateButton("+", "Acercar", "view.zoom-in"));
        row.Children.Add(CreateButton("Ajustar", "Ajustar página", "view.fit-page"));
        row.Children.Add(CreateButton("☰", "Paletas", "view.toggle-left-panel"));
        row.Children.Add(CreateButton("▤", "Inspector", "view.toggle-right-panel"));
        row.Children.Add(CreateButton("▱", "Panel inferior", "view.toggle-bottom-panel"));
        row.Children.Add(CreateButton("◐", "Cambiar tema", "view.toggle-theme"));
        return new Border { Child = row, Padding = new Thickness(10, 6), Height = 52 };
    }

    private Border CreateSidePanel(string title, string[] categories, double width)
    {
        StackPanel stack = new() { Spacing = 9, Margin = new Thickness(14, 14, 10, 10) };
        stack.Children.Add(CreateLabel(title, 17, FontWeight.SemiBold));
        foreach (string category in categories)
        {
            stack.Children.Add(CreateLabel(category, 13));
        }

        return new Border { Width = width, Child = new ScrollViewer { Content = stack } };
    }

    private Border CreateInspectorPanel(out TextBlock inspectorText)
    {
        StackPanel stack = new() { Spacing = 10, Margin = new Thickness(14) };
        stack.Children.Add(CreateLabel("Inspector", 17, FontWeight.SemiBold));
        inspectorText = CreateLabel("Selecciona un elemento", 13);
        stack.Children.Add(inspectorText);
        stack.Children.Add(CreateLabel("Estilo", 17, FontWeight.SemiBold));
        stack.Children.Add(CreateLabel("Ajustes de la partitura", 13));
        return new Border { Width = 240, Child = new ScrollViewer { Content = stack } };
    }

    private Border CreateBottomPanel()
    {
        StackPanel stack = new() { Margin = new Thickness(14), Spacing = 10 };
        stack.Children.Add(CreateLabel("Mezclador  ·  Teclado de piano", 15, FontWeight.SemiBold));
        return new Border { Height = 170, Child = stack };
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

    private void Execute(string actionId)
    {
        if (_actions?.TryExecute(actionId) == true)
        {
            if (actionId is "view.open-menu" or "view.open-selector")
            {
                return;
            }

            if (_viewMenuPopup is not null)
            {
                _viewMenuPopup.IsOpen = false;
            }

            if (_viewSelectorPopup is not null)
            {
                _viewSelectorPopup.IsOpen = false;
            }

            Canvas.Focus();
        }
    }

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

        Canvas.WorkspaceColor = _isDarkTheme
            ? new SKColor(47, 52, 61)
            : new SKColor(222, 225, 230);
    }

    private void OnInputStateChanged(object? sender, EventArgs e) => UpdateStatus();

    private void UpdateStatus()
    {
        ScoreInputCursor cursor = _input.Cursor;
        Score score = _input.CurrentScore;
        int measureIndex = 0;
        Fraction measureStart = Fraction.Zero;
        for (; measureIndex < score.Measures.Length - 1; measureIndex++)
        {
            Fraction measureEnd = measureStart + score.Measures[measureIndex].TimeSignature.Length;
            if (cursor.Position < measureEnd)
            {
                break;
            }

            measureStart = measureEnd;
        }

        TimeSignature signature = score.Measures[measureIndex].TimeSignature;
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
        _statusText.Text = $"{mode}  ·  {duration}  ·  Voz {cursor.VoiceNumber}  ·  " +
            $"Compás {score.Measures[measureIndex].Number}  ·  Tiempo {beatNumber}  ·  " +
            $"Selección: {selectedCount}";
        _inspectorText.Text = selectedCount == 0
            ? "Selecciona un elemento"
            : selectedCount == 1 ? "1 elemento seleccionado" : $"{selectedCount} elementos seleccionados";
    }
}
