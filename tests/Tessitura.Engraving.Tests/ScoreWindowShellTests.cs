using System.Collections.Immutable;
using Avalonia.Controls;
using SkiaSharp;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Editing;
using Tessitura.Rendering;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class ScoreWindowShellTests
{
    [Fact]
    public void MainWindowPlacesScoreBetweenToolPanelsAndStatusBelow()
    {
        ScoreCanvas canvas = new();
        using ScoreWindowShell shell = new(canvas, CreateInput());

        Assert.Same(canvas, shell.Canvas);
        Assert.Equal("Archivo ▾", shell.FileMenuButton.Content);
        Assert.Equal("Página ▾", shell.ViewSelectorButton.Content);
        Assert.Equal(0, Grid.GetRow(shell.TopBar));
        Assert.Equal(1, Grid.GetRow(shell.LeftPanel));
        Assert.Equal(0, Grid.GetColumn(shell.LeftPanel));
        Assert.Equal(1, Grid.GetRow(canvas));
        Assert.Equal(1, Grid.GetColumn(canvas));
        Assert.Equal(1, Grid.GetRow(shell.RightPanel));
        Assert.Equal(2, Grid.GetColumn(shell.RightPanel));
        Assert.Equal(2, Grid.GetRow(shell.BottomPanel));
        Assert.Equal(3, Grid.GetRow(shell.StatusBar));
    }

    [Fact]
    public void ToolbarAndAdvancedPaletteExposeWorkingControls()
    {
        using ScoreWindowShell shell = new(new ScoreCanvas(), CreateInput());

        Assert.True(shell.FileMenuButton.IsEnabled);
        Assert.True(shell.EditMenuButton.IsEnabled);
        Assert.True(shell.PlayButton.IsEnabled);
        Assert.True(shell.StopButton.IsEnabled);

        ScrollViewer scroller = Assert.IsType<ScrollViewer>(shell.LeftPanel.Child);
        StackPanel groups = Assert.IsType<StackPanel>(scroller.Content);
        string[] buttons = groups.Children.OfType<WrapPanel>()
            .SelectMany(row => row.Children.OfType<Button>())
            .Select(button => Assert.IsType<string>(button.Content)).ToArray();
        Assert.Contains("Dinámica…", buttons);
        Assert.Contains("Staccato", buttons);
        Assert.Contains("Crescendo", buttons);
        Assert.Contains("Texto…", buttons);
    }

    [Fact]
    public void RegisteredActionsCollapsePanelsAndSwitchTheme()
    {
        ScoreCanvas canvas = new();
        using ScoreWindowShell shell = new(canvas, CreateInput());
        string settings = Path.Combine(Path.GetTempPath(), $"tessitura-shell-{Guid.NewGuid():N}.json");
        try
        {
            ActionRegistry registry = ActionRegistry.LoadOrCreate(shell.CreateActions(), settings);
            shell.AttachActionRegistry(registry);

            Assert.True(shell.IsLeftPanelOpen);
            Assert.True(shell.IsRightPanelOpen);
            Assert.False(shell.IsBottomPanelOpen);
            Assert.True(registry.TryExecute("view.toggle-left-panel"));
            Assert.True(registry.TryExecute("view.toggle-right-panel"));
            Assert.True(registry.TryExecute("view.toggle-bottom-panel"));
            Assert.False(shell.IsLeftPanelOpen);
            Assert.False(shell.IsRightPanelOpen);
            Assert.True(shell.IsBottomPanelOpen);

            SKColor initialColor = canvas.WorkspaceColor;
            Assert.True(registry.TryExecute("view.toggle-theme"));
            Assert.NotEqual(initialColor, canvas.WorkspaceColor);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void SwitchingScoreViewsKeepsTheCurrentSelection()
    {
        ScoreInputController input = CreatePartInput(out EventId selectedEvent);
        input.SelectEvent(selectedEvent);
        Selection selection = input.CurrentSelection;
        using ScoreWindowShell shell = new(new ScoreCanvas(), input);
        string settings = Path.Combine(Path.GetTempPath(), $"tessitura-view-{Guid.NewGuid():N}.json");
        try
        {
            ActionRegistry actions = ActionRegistry.LoadOrCreate(
                input.CreateActions().AddRange(shell.CreateActions()), settings);
            shell.AttachActionRegistry(actions);

            Assert.True(actions.TryExecute("view.continuous"));
            Assert.Equal(ScoreViewMode.Continuous, shell.CurrentView);
            Assert.Equal(selection, input.CurrentSelection);
            Assert.Equal("Continua ▾", shell.ViewSelectorButton.Content);

            Assert.True(actions.TryExecute("view.part.select.0"));
            Assert.Equal(ScoreViewMode.Part, shell.CurrentView);
            Assert.Equal("Flute", shell.CurrentPart?.Name);
            Assert.Equal(selection, input.CurrentSelection);
            Assert.Equal("Flute ▾", shell.ViewSelectorButton.Content);

            Assert.True(actions.TryExecute("view.page"));
            Assert.Equal(ScoreViewMode.Page, shell.CurrentView);
            Assert.Equal(selection, input.CurrentSelection);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void StatusBarTracksInputModeAndMusicalCursor()
    {
        ScoreInputController input = CreateInput();
        using ScoreWindowShell shell = new(new ScoreCanvas(), input);

        Assert.Contains("Selección", shell.StatusText);
        Assert.Contains("Voz 1", shell.StatusText);
        Assert.Contains("Compás 1", shell.StatusText);

        input.EnterNoteEntry();

        Assert.Contains("Entrada", shell.StatusText);
        Assert.Contains("Negra", shell.StatusText);
    }

    [Fact]
    public void StatusShowsNextMeasureAtTheBarline()
    {
        ScoreInputController input = CreateInput();
        using ScoreWindowShell shell = new(new ScoreCanvas(), input);
        string settings = Path.Combine(Path.GetTempPath(), $"tessitura-shell-{Guid.NewGuid():N}.json");
        try
        {
            ActionRegistry actions = ActionRegistry.LoadOrCreate(
                input.CreateActions().AddRange(shell.CreateActions()), settings);
            shell.AttachActionRegistry(actions);
            input.EnterNoteEntry();
            for (int noteIndex = 0; noteIndex < 4; noteIndex++)
            {
                Assert.True(actions.TryExecute("score.note.c"));
            }

            Assert.Contains("Compás 2  ·  Tiempo 1", shell.StatusText);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void WorkspaceColorChangesWithoutChangingPaperColor()
    {
        using SKBitmap bitmap = new(720, 920);
        using SKCanvas canvas = new(bitmap);
        SKColor workspace = new(222, 225, 230);

        PagePreviewRenderer.Draw(canvas, 720, 920, 1, 0, 0, workspaceColor: workspace);

        Assert.Equal(workspace, bitmap.GetPixel(0, 0));
        Assert.Equal(SKColors.White, bitmap.GetPixel(100, 100));
    }

    private static ScoreInputController CreateInput()
    {
        Rest rest = new(new EventId(Guid.NewGuid()), Fraction.Zero,
            new Duration(NoteValue.Whole, 0));
        Score score = new(new ScoreMetadata("Test", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [rest])])));
        return new ScoreInputController(score);
    }

    private static ScoreInputController CreatePartInput(out EventId selectedEvent)
    {
        selectedEvent = new EventId(Guid.NewGuid());
        Chord chord = new(selectedEvent, Fraction.Zero, new Duration(NoteValue.Whole, 0),
            [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Rest rest = new(new EventId(Guid.NewGuid()), Fraction.Zero,
            new Duration(NoteValue.Whole, 0));
        Score score = new(new ScoreMetadata("Test", ""),
            [new Instrument("Flute", [new Staff("Flute")]), new Instrument("Oboe", [new Staff("Oboe")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [chord])]))
                .Add(new StaffMeasureKey(1, 0), new StaffMeasure([new Voice(1, [rest])])),
            Parts: [new ScorePartView("Flute", [0]), new ScorePartView("Oboe", [1])]);
        return new ScoreInputController(score);
    }
}
