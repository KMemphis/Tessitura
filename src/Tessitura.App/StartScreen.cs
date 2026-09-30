using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Tessitura.Core;
using Tessitura.Editing;

namespace Tessitura.App;

/// <summary>Shows recent scores, templates and the new-score wizard.</summary>
public sealed class StartScreen : UserControl
{
    private const string FirstSteps = "Crea una partitura, pulsa «Escribir notas» y haz clic en el pentagrama. También puedes escribir C D E F G A B.";

    /// <summary>Gets the short first-note instruction visible on the home screen.</summary>
    public string FirstStepsText => FirstSteps;

    private static readonly (string Label, TimeSignature Meter)[] Meters =
    [
        ("2/2", new TimeSignature(2, 2)), ("2/4", new TimeSignature(2, 4)),
        ("3/4", new TimeSignature(3, 4)), ("4/4", new TimeSignature(4, 4)),
        ("6/8", new TimeSignature(6, 8)), ("9/8", new TimeSignature(9, 8)),
        ("12/8", new TimeSignature(12, 8)),
    ];

    private readonly StartScreenController _controller;
    private ActionRegistry? _actions;

    /// <summary>Creates the start screen over its controller.</summary>
    /// <param name="controller">The start-screen state and actions.</param>
    public StartScreen(StartScreenController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _controller.StateChanged += (_, _) => Rebuild();
        Rebuild();
    }

    /// <summary>Gets the buttons currently shown, keyed by their action identifier.</summary>
    public Dictionary<string, Button> Buttons { get; } = [];

    /// <summary>Connects the visible buttons to the central action registry.</summary>
    /// <param name="actions">The registry that executes the actions.</param>
    public void AttachActionRegistry(ActionRegistry actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <inheritdoc />
    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        if (_actions?.TryExecute(e.Key, e.KeyModifiers) == true)
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void Rebuild()
    {
        Buttons.Clear();
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 18, Margin = new Thickness(40), MaxWidth = 840,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { _controller.IsWizardOpen ? BuildWizard() : BuildHome() },
            },
        };
    }

    private Control BuildHome()
    {
        StackPanel page = new() { Spacing = 16 };
        page.Children.Add(Label("Tessitura", 34, FontWeight.SemiBold));
        page.Children.Add(Label("Escribe, escucha y prepara partituras en un solo lugar.", 16));
        page.Children.Add(Label("Nueva partitura", 20, FontWeight.SemiBold));
        WrapPanel templates = new() { Orientation = Orientation.Horizontal };
        templates.Children.Add(TemplateButton("Piano", "Dos pentagramas", "start.template.piano"));
        templates.Children.Add(TemplateButton("Cuarteto de cuerda", "Cuatro instrumentos", "start.template.string-quartet"));
        templates.Children.Add(TemplateButton("Coro SATB", "Cuatro voces", "start.template.satb"));
        page.Children.Add(templates);
        page.Children.Add(Label(FirstSteps, 14));
        page.Children.Add(ActionButton("Abrir partitura .tess…", "start.open", 210));
        page.Children.Add(Label("Recientes", 18, FontWeight.SemiBold));
        if (_controller.Recents.Count == 0)
        {
            page.Children.Add(Label("Todavía no hay partituras recientes.", 13));
        }

        for (int index = 0; index < _controller.Recents.Count; index++)
        {
            RecentScoreLabel entry = new(_controller.Recents[index]);
            page.Children.Add(ActionButton(entry.Text, $"start.recent.{index}", 480));
        }

        return page;
    }

    private Control BuildWizard()
    {
        NewScoreOptions options = _controller.Options;
        StackPanel page = new() { Spacing = 12 };
        page.Children.Add(Label("Nueva partitura", 24, FontWeight.SemiBold));
        page.Children.Add(Label(options.Template switch
        {
            ScoreTemplate.Piano => "Plantilla: piano",
            ScoreTemplate.StringQuartet => "Plantilla: cuarteto de cuerda",
            _ => "Plantilla: coro SATB",
        }, 14));

        TextBox title = new() { Text = options.Title, PlaceholderText = "Título" };
        title.TextChanged += (_, _) =>
            _controller.Options = _controller.Options with { Title = title.Text ?? string.Empty };
        TextBox composer = new() { Text = options.Composer, PlaceholderText = "Compositor" };
        composer.TextChanged += (_, _) =>
            _controller.Options = _controller.Options with { Composer = composer.Text ?? string.Empty };
        page.Children.Add(Label("Título", 13));
        page.Children.Add(title);
        page.Children.Add(Label("Compositor", 13));
        page.Children.Add(composer);

        ComboBox meter = new() { ItemsSource = Array.ConvertAll(Meters, m => m.Label) };
        meter.SelectedIndex = Array.FindIndex(Meters, m => m.Meter == options.TimeSignature);
        meter.SelectionChanged += (_, _) =>
        {
            if (meter.SelectedIndex >= 0)
            {
                _controller.Options = _controller.Options with { TimeSignature = Meters[meter.SelectedIndex].Meter };
            }
        };
        page.Children.Add(Label("Compás", 13));
        page.Children.Add(meter);

        string[] keyLabels = new string[15];
        for (int fifths = -7; fifths <= 7; fifths++)
        {
            keyLabels[fifths + 7] = fifths switch
            {
                0 => "Do mayor / La menor",
                < 0 => $"{-fifths} bemol{(fifths == -1 ? "" : "es")}",
                _ => $"{fifths} sostenido{(fifths == 1 ? "" : "s")}",
            };
        }

        ComboBox key = new() { ItemsSource = keyLabels, SelectedIndex = options.KeySignature.Fifths + 7 };
        key.SelectionChanged += (_, _) =>
        {
            if (key.SelectedIndex >= 0)
            {
                _controller.Options = _controller.Options with { KeySignature = new KeySignature(key.SelectedIndex - 7) };
            }
        };
        page.Children.Add(Label("Armadura", 13));
        page.Children.Add(key);

        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(ActionButton("Crear", "start.create", 120));
        row.Children.Add(ActionButton("Volver", "start.back", 120));
        page.Children.Add(row);
        return page;
    }

    private Button ActionButton(string text, string actionId, double width)
    {
        Button button = new() { Content = new TextBlock { Text = text,
                TextTrimming = TextTrimming.CharacterEllipsis }, Width = width, Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(10, 8), HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(button, text);
        button.Click += (_, _) => _actions?.TryExecute(actionId);
        Buttons[actionId] = button;
        return button;
    }

    private Button TemplateButton(string title, string subtitle, string actionId)
    {
        Button button = new()
        {
            Content = new StackPanel
            {
                Spacing = 7,
                Children =
                {
                    Label(title, 16, FontWeight.SemiBold),
                    Label(subtitle, 12),
                },
            },
            Width = 225, Height = 90, Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(14, 10),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => _actions?.TryExecute(actionId);
        Buttons[actionId] = button;
        return button;
    }

    private static TextBlock Label(string text, double size, FontWeight weight = FontWeight.Normal) =>
        new() { Text = text, FontSize = size, FontWeight = weight };

    private readonly record struct RecentScoreLabel(Tessitura.IO.RecentScore Score)
    {
        public string Text => string.Create(CultureInfo.CurrentCulture,
            $"{Score.Title}  ·  {Score.LastOpened.LocalDateTime:g}  ·  {Score.Path}");
    }
}
