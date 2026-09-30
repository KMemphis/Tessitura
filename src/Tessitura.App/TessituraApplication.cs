using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Tessitura.Rendering;
using Tessitura.Smufl;

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
            MusicPreviewRenderer music = new(Path.Combine(assets, "Bravura.otf"), metadata);
            ScoreCanvas canvas = new(music);
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Tessitura",
                "shortcuts.json");
            ActionRegistry actions = ActionRegistry.LoadOrCreate(
            [
                new ActionDefinition(
                    "view.zoom-in", "Aumentar zoom", "Ctrl+Plus", () => canvas.ZoomBy(1.1)),
                new ActionDefinition(
                    "view.zoom-out", "Reducir zoom", "Ctrl+Minus", () => canvas.ZoomBy(1 / 1.1)),
                new ActionDefinition(
                    "view.fit-page", "Ajustar página", "Ctrl+0", canvas.FitPage),
            ],
            settingsPath);
            canvas.AttachActionRegistry(actions);
            desktop.Exit += (_, _) => music.Dispose();
            desktop.MainWindow = new Window
            {
                Title = "Tessitura",
                Width = 1000,
                Height = 800,
                MinWidth = 600,
                MinHeight = 500,
                Content = canvas,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
