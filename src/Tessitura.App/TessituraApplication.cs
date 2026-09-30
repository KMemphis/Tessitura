using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Tessitura.App;

/// <summary>Creates the desktop score window.</summary>
public sealed class TessituraApplication : Application
{
    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "Tessitura",
                Width = 1000,
                Height = 800,
                MinWidth = 600,
                MinHeight = 500,
                Content = new ScoreCanvas(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
