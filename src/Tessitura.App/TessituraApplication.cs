using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Tessitura.IO;
using Tessitura.IO.Tess;
using Tessitura.Smufl;
using Tessitura.Core;

namespace Tessitura.App;

/// <summary>Creates the desktop score window.</summary>
public sealed class TessituraApplication : Application
{
    private Window _window = null!;
    private SmuflMetadata _metadata = null!;
    private string _assets = null!;
    private string _dataDirectory = null!;
    private RecentScores _recents = null!;
    private EditorSession? _session;

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        // Third-party control styles (AngryCarrot789/WPFDarkTheme, MIT), copied under Themes/AngryCarrot.
        Uri themeBase = new("avares://Tessitura.App/");
        Styles.Add(new Avalonia.Themes.Simple.SimpleTheme());
        Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(themeBase)
        {
            Source = new Uri("avares://Tessitura.App/Themes/AngryCarrot/Colours/SoftDark.axaml"),
        });
        Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(themeBase)
        {
            Source = new Uri("avares://Tessitura.App/Themes/AngryCarrot/ControlColours.axaml"),
        });
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(themeBase)
        {
            Source = new Uri("avares://Tessitura.App/Themes/AngryCarrot/Controls.axaml"),
        });
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _assets = Path.Combine(AppContext.BaseDirectory, "assets", "fonts");
            _metadata = SmuflMetadata.Load(
                Path.Combine(_assets, "Bravura.json"),
                Path.Combine(_assets, "smufl_glyph_names.json"));
            _dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tessitura");
            _recents = new RecentScores(Path.Combine(_dataDirectory, "recents.json"));
            _window = new Window
            {
                Title = "Tessitura",
                Width = 1000,
                Height = 800,
                MinWidth = 600,
                MinHeight = 500,
            };
            desktop.Exit += (_, _) => _session?.Dispose();
            desktop.MainWindow = _window;
            ShowStart();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowStart()
    {
        _session?.Dispose();
        _session = null;
        _window.Title = "Tessitura";
        StartScreenController controller = new(_recents);
        StartScreen screen = new(controller);
        ActionRegistry actions = ActionRegistry.LoadOrCreate(controller.CreateActions(),
            Path.Combine(_dataDirectory, "start-shortcuts.json"));
        screen.AttachActionRegistry(actions);
        controller.ScoreCreated += (_, score) => OpenEditor(score, null);
        controller.RecentChosen += (_, path) => OpenPath(path);
        controller.OpenFileRequested += async (_, _) =>
        {
            IReadOnlyList<IStorageFile> files = await _window.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Partitura Tessitura") { Patterns = ["*.tess"] }],
                });
            if (files.Count == 1 && files[0].TryGetLocalPath() is string path)
            {
                OpenPath(path);
            }
        };
        _window.Content = screen;
        screen.Focusable = true;
        _window.Opened += (_, _) => screen.Focus();
        screen.Focus();
    }

    private void OpenPath(string path)
    {
        try
        {
            TessDocument document = TessFile.Open(path);
            _recents.Add(path, document.Score.Metadata.Title, DateTimeOffset.Now);
            OpenEditor(document.Score, path);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
            or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _window.Title = $"Tessitura — no se pudo abrir: {exception.Message}";
        }
    }

    private void OpenEditor(Score score, string? path)
    {
        _session?.Dispose();
        _session = new EditorSession(_window, _metadata, _assets,
            Path.Combine(_dataDirectory, "shortcuts.json"), Path.Combine(_dataDirectory, "recovery"),
            _recents, score, path, ShowStart);
        _window.Title = "Tessitura";
        _window.Content = _session.Shell;
        _session.Focus();
    }
}
