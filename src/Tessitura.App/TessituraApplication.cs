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
            desktop.Exit += (_, _) => music.Dispose();
            desktop.MainWindow = new Window
            {
                Title = "Tessitura",
                Width = 1000,
                Height = 800,
                MinWidth = 600,
                MinHeight = 500,
                Content = new ScoreCanvas(music),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
