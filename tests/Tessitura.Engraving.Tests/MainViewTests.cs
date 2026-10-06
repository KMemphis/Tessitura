using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Editing;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class MainViewTests : IDisposable
{
    private readonly string _settingsDirectory =
        Path.Combine(Path.GetTempPath(), "tessitura-shell-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void LayoutHasEveryRegionOfTheDefinition() => HeadlessPlatform.Run(() =>
    {
            (MainView view, _, _) = CreateShell();

            Assert.NotNull(view.TopBar);
            Assert.NotNull(view.ViewSelector);
            Assert.NotNull(view.TransportBar);
            Assert.NotNull(view.ZoomBar);
            Assert.NotNull(view.LeftPanel);
            Assert.NotNull(view.RightPanel);
            Assert.NotNull(view.BottomPanel);
            Assert.NotNull(view.StatusBar);
            Assert.Same(view.Canvas, view.CanvasHost.Child);
            Assert.Equal(Dock.Top, DockPanel.GetDock(view.TopBar));
            Assert.Equal(Dock.Bottom, DockPanel.GetDock(view.StatusBar));
            Assert.Equal(Dock.Left, DockPanel.GetDock(view.LeftPanel));
            Assert.Equal(Dock.Right, DockPanel.GetDock(view.RightPanel));
            Assert.Equal(Dock.Bottom, DockPanel.GetDock(view.BottomPanel));
            Assert.True(view.LeftPanel.IsVisible);
            Assert.True(view.RightPanel.IsVisible);
            Assert.False(view.BottomPanel.IsVisible);
    });

    [Fact]
    public void PanelsCollapseAndExpandThroughRegisteredActions() => HeadlessPlatform.Run(() =>
    {
            (MainView view, _, ActionRegistry actions) = CreateShell();

            Assert.True(actions.TryExecute("view.panel.left"));
            Assert.True(actions.TryExecute("view.panel.right"));
            Assert.True(actions.TryExecute("view.panel.bottom"));
            Assert.False(view.LeftPanel.IsVisible);
            Assert.False(view.RightPanel.IsVisible);
            Assert.True(view.BottomPanel.IsVisible);

            Assert.True(actions.TryExecute(Avalonia.Input.Key.D7, Avalonia.Input.KeyModifiers.Control));
            Assert.True(view.LeftPanel.IsVisible);
    });

    [Fact]
    public void ThemeToggleSwitchesBetweenDarkAndLight() => HeadlessPlatform.Run(() =>
    {
            (MainView view, _, ActionRegistry actions) = CreateShell();
            Assert.Equal(ThemeVariant.Dark, view.ThemeVariant);

            Assert.True(actions.TryExecute("view.theme.toggle"));
            Assert.Equal(ThemeVariant.Light, view.ThemeVariant);
            Assert.Equal(ShellTheme.LightWorkspace, view.Canvas.WorkspaceColor);

            Assert.True(actions.TryExecute("view.theme.toggle"));
            Assert.Equal(ThemeVariant.Dark, view.ThemeVariant);
            Assert.Equal(ShellTheme.DarkWorkspace, view.Canvas.WorkspaceColor);
    });

    [Fact]
    public void StatusBarFollowsModeDurationAndCursor() => HeadlessPlatform.Run(() =>
    {
            (MainView view, ScoreInputController input, ActionRegistry actions) = CreateShell();
            Assert.Equal("Selección", view.ModeText.Text);
            Assert.Equal("Negra", view.DurationText.Text);
            Assert.Equal("Compás 1 · Tiempo 1", view.PositionText.Text);

            actions.TryExecute("score.note-entry");
            actions.TryExecute("score.duration.half");
            actions.TryExecute("score.note.c");

            Assert.Equal("Entrada de notas", view.ModeText.Text);
            Assert.Equal("Blanca", view.DurationText.Text);
            Assert.Equal("Compás 1 · Tiempo 3", view.PositionText.Text);
            Assert.Equal("Voz 1", view.VoiceText.Text);
            Assert.Equal(new Duration(NoteValue.Half, 0), input.CurrentDuration);
    });

    [Fact]
    public void ToolbarButtonsDispatchThroughTheActionRegistry() => HeadlessPlatform.Run(() =>
    {
            (MainView view, ScoreInputController input, ActionRegistry actions) = CreateShell();
            Button eighth = view.ActionButtons["score.duration.eighth"];
            actions.TryExecute("score.note-entry");

            eighth.Command!.Execute(null);

            Assert.Equal(new Duration(NoteValue.Eighth, 0), input.CurrentDuration);
            Assert.Contains("4", ToolTip.GetTip(eighth) as string, StringComparison.Ordinal);
    });

    [Fact]
    public void UndoButtonIsEnabledOnlyWhenHistoryAllowsIt() => HeadlessPlatform.Run(() =>
    {
            (MainView view, _, ActionRegistry actions) = CreateShell();
            Button undo = view.ActionButtons["score.undo"];
            Assert.False(undo.IsEnabled);

            actions.TryExecute("score.note-entry");
            actions.TryExecute("score.note.d");

            Assert.True(undo.IsEnabled);
    });

    [Fact]
    public void MenusListRegisteredActionsWithTheirShortcuts() => HeadlessPlatform.Run(() =>
    {
            (MainView view, _, ActionRegistry actions) = CreateShell();
            HashSet<string> registered = actions.Actions.Select(action => action.Id).ToHashSet(StringComparer.Ordinal);
            List<MenuItem> items = view.MainMenu.Items.OfType<MenuItem>()
                .SelectMany(menu => menu.Items.OfType<MenuItem>())
                .ToList();

            Assert.NotEmpty(items);
            Assert.All(items, item => Assert.Contains((string)item.Tag!, registered));
            MenuItem undo = items.Single(item => (string)item.Tag! == "score.undo");
            TextBlock[] labels = ((Grid)undo.Header!).Children.OfType<TextBlock>().ToArray();
            Assert.Equal(["Deshacer", "Ctrl+Z"], labels.Select(label => label.Text));
            Assert.Equal("Ctrl+↑", MainView.FormatShortcut("Ctrl+Up"));
            Assert.Equal("Ctrl+−", MainView.FormatShortcut("Ctrl+Minus"));
    });

    [Fact]
    public void StatusDescribesSelectedNotesAndRests() => HeadlessPlatform.Run(() =>
    {
            Chord chord = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Quarter, 1),
                [new Note(new Pitch(Step.F, 1, 5))], StemDirection.Auto);
            Rest rest = new(new EventId(Guid.NewGuid()), new Fraction(3, 8), new Duration(NoteValue.Eighth, 0));
            Rest tail = new(new EventId(Guid.NewGuid()), new Fraction(1, 2), new Duration(NoteValue.Half, 0));
            Score score = CreateScore([chord, rest, tail]);

            Assert.Equal("Sin selección", ShellStatus.DescribeSelection(score, Selection.Empty));
            Assert.Equal("Negra con puntillo, Fa♯ 5 · compás 1, tiempo 1",
                ShellStatus.DescribeSelection(score, new Selection([new SelectionItem(chord.Id, 0)])));
            Assert.Equal("Silencio de corchea · compás 1, tiempo 2 + 1/2",
                ShellStatus.DescribeSelection(score, new Selection([new SelectionItem(rest.Id)])));
            Assert.Equal("2 elementos", ShellStatus.DescribeSelection(score,
                new Selection([new SelectionItem(chord.Id, 0), new SelectionItem(rest.Id)])));
    });

    [Fact]
    public void PositionUsesTheBeatUnitOfEachMeasure() => HeadlessPlatform.Run(() =>
    {
            Score score = new(
                new ScoreMetadata("Test", ""),
                [new Instrument("Piano", [new Staff("Treble")])],
                [new Measure(1, new TimeSignature(4, 4)), new Measure(2, new TimeSignature(6, 8))],
                ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty);

            Assert.Equal("Compás 2 · Tiempo 4", ShellStatus.DescribePosition(score, new Fraction(11, 8)));
            Assert.Equal("Compás 1 · Tiempo 2 + 1/4", ShellStatus.DescribePosition(score, new Fraction(5, 16)));
            Assert.Equal("Corchea con doble puntillo", ShellStatus.DescribeDuration(new Duration(NoteValue.Eighth, 2)));
    });

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }

    private (MainView View, ScoreInputController Input, ActionRegistry Actions) CreateShell()
    {
        Rest rest = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Whole, 0));
        ScoreInputController input = new(CreateScore([rest]));
        ScoreCanvas canvas = new() { ScoreInputController = input };
        MainView view = new(input, canvas);
        List<ActionDefinition> definitions = [.. view.CreateActions(), .. input.CreateActions()];
        ActionRegistry actions = ActionRegistry.LoadOrCreate(
            definitions, Path.Combine(_settingsDirectory, "shortcuts.json"));
        view.AttachActionRegistry(actions);
        return (view, input, actions);
    }

    private static Score CreateScore(ImmutableArray<MusicEvent> events) => new(
        new ScoreMetadata("Test", ""),
        [new Instrument("Piano", [new Staff("Treble")])],
        [new Measure(1, new TimeSignature(4, 4))],
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
            new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, events)])));
}

/// <summary>Runs UI tests on Avalonia's headless dispatcher thread so styling and geometry services exist.</summary>
internal static class HeadlessPlatform
{
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication)));

    public static void Run(Action test) =>
        Session.Value.Dispatch(test, CancellationToken.None).GetAwaiter().GetResult();
}

/// <summary>Configures the headless application used by UI tests.</summary>
public sealed class HeadlessTestApplication : Application
{
    /// <summary>Builds the headless test application.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HeadlessTestApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
