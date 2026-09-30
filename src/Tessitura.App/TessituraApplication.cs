using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Tessitura.Smufl;
using Tessitura.Core;

namespace Tessitura.App;

/// <summary>Creates the desktop score window.</summary>
public sealed class TessituraApplication : Application
{
    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string assets = Path.Combine(AppContext.BaseDirectory, "assets", "fonts");
            SmuflMetadata metadata = SmuflMetadata.Load(
                Path.Combine(assets, "Bravura.json"),
                Path.Combine(assets, "smufl_glyph_names.json"));
            ScoreInputController scoreInput = new(CreateInitialScore());
            ScoreCanvas canvas = new() { ScoreInputController = scoreInput };
            ScoreWindowShell shell = new(canvas, scoreInput);
            ScoreUpdateCoordinator updates = new(scoreInput, metadata,
                Path.Combine(assets, "Bravura.otf"),
                postToUi: action => Dispatcher.UIThread.Post(action));
            updates.PresentationReady += (_, presentation) => canvas.AttachPresentation(presentation);
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Tessitura",
                "shortcuts.json");
            List<ActionDefinition> actionDefinitions =
            [
                new ActionDefinition(
                    "view.zoom-in", "Aumentar zoom", "Ctrl+Plus", () => canvas.ZoomBy(1.1)),
                new ActionDefinition(
                    "view.zoom-out", "Reducir zoom", "Ctrl+Minus", () => canvas.ZoomBy(1 / 1.1)),
                new ActionDefinition(
                    "view.fit-page", "Ajustar página", "Ctrl+0", canvas.FitPage),
            ];
            actionDefinitions.AddRange(scoreInput.CreateActions());
            actionDefinitions.AddRange(shell.CreateActions());
            ActionRegistry actions = ActionRegistry.LoadOrCreate(actionDefinitions, settingsPath);
            canvas.AttachActionRegistry(actions);
            shell.AttachActionRegistry(actions);
            desktop.Exit += (_, _) =>
            {
                updates.Dispose();
                canvas.DisposePresentation();
                shell.Dispose();
            };
            Window mainWindow = new()
            {
                Title = "Tessitura",
                Width = 1000,
                Height = 800,
                MinWidth = 600,
                MinHeight = 500,
                Content = shell,
            };
            mainWindow.Opened += (_, _) => canvas.Focus();
            desktop.MainWindow = mainWindow;
            updates.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Score CreateInitialScore()
    {
        Measure measure = new(1, new TimeSignature(4, 4));
        Rest rest = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Whole, 0));
        StaffMeasure content = new([new Voice(1, [rest])]);
        return new Score(
            new ScoreMetadata("Sin título", ""),
            [new Instrument("Piano", [new Staff("Pentagrama superior")])],
            [measure],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), content));
    }
}
