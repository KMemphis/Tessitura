using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.IO;
using Tessitura.IO.Tess;
using Tessitura.Rendering;
using Tessitura.Smufl;

namespace Tessitura.App;

/// <summary>Owns one open score: its editor window content, saving and recovery copy.</summary>
internal sealed class EditorSession : IDisposable
{
    private readonly Window _window;
    private readonly RecentScores _recents;
    private readonly Style _style;
    private readonly ScoreInputController _input;
    private readonly ScoreCanvas _canvas;
    private readonly ScoreUpdateCoordinator _updates;
    private readonly RecoveryAutosave _autosave;
    private readonly SmuflMetadata _metadata;
    private readonly string _assetsPath;
    private readonly PlaybackController _playback;
    private readonly Avalonia.Threading.DispatcherTimer _playhead;
    private string? _path;

    public EditorSession(Window window, SmuflMetadata metadata, string assetsPath,
        string settingsPath, string recoveryDirectory, RecentScores recents, Score score, string? path,
        Action closeToStart)
    {
        _window = window;
        _metadata = metadata;
        _assetsPath = assetsPath;
        _recents = recents;
        _path = path;
        _style = Style.CreateDefault(metadata);
        _input = new ScoreInputController(score);
        _canvas = new ScoreCanvas { ScoreInputController = _input };
        Shell = new ScoreWindowShell(_canvas, _input);
        _updates = new ScoreUpdateCoordinator(_input, metadata,
            Path.Combine(assetsPath, "Bravura.otf"),
            postToUi: action => Dispatcher.UIThread.Post(action));
        _updates.PresentationReady += (_, presentation) => _canvas.AttachPresentation(presentation);
        List<ActionDefinition> definitions =
        [
            new("view.zoom-in", "Aumentar zoom", "Ctrl+Plus", () => _canvas.ZoomBy(1.1)),
            new("view.zoom-out", "Reducir zoom", "Ctrl+Minus", () => _canvas.ZoomBy(1 / 1.1)),
            new("view.fit-page", "Ajustar página", "Ctrl+0", _canvas.FitPage),
            new("file.save", "Guardar", "Ctrl+S", () => _ = SaveAsync(saveAs: false)),
            new("file.save-as", "Guardar como…", "Ctrl+Shift+S", () => _ = SaveAsync(saveAs: true)),
            new("file.export-pdf", "Exportar a PDF…", "Ctrl+E", () => _ = ExportPdfAsync()),
            new("file.close", "Cerrar y volver al inicio", "Ctrl+W", closeToStart),
        ];
        string soundFont = Path.Combine(assetsPath, "..", "soundfonts", "default.sf2");
        _playback = new PlaybackController(_input, rate =>
        {
            if (!File.Exists(soundFont))
            {
                throw new FileNotFoundException("No SoundFont is installed (assets/soundfonts/default.sf2).", soundFont);
            }

            return new MeltySynth.Synthesizer(new MeltySynth.SoundFont(soundFont), new MeltySynth.SynthesizerSettings(rate));
        }, () => new Tessitura.Playback.Audio.MiniAudioOutput());
        _playhead = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _playhead.Tick += (_, _) => _playback.Tick();
        _playhead.Start();
        definitions.AddRange(_playback.CreateActions().Select(a => a with { Execute = () => RunPlayback(a.Execute) }));
        definitions.AddRange(_input.CreateActions());
        definitions.AddRange(Shell.CreateActions());
        ActionRegistry actions = ActionRegistry.LoadOrCreate(definitions, settingsPath);
        _canvas.AttachActionRegistry(actions);
        Shell.AttachActionRegistry(actions);
        Shell.BuildMixerStrips([.. score.Instruments.Select(i => i.Name)]);
        Directory.CreateDirectory(recoveryDirectory);
        string recoveryBase = path ?? Path.Combine(recoveryDirectory, $"untitled-{Guid.NewGuid():N}.tess");
        _autosave = new RecoveryAutosave(recoveryBase, () => (_input.CurrentScore, _style));
        _updates.Start();
    }

    public ScoreWindowShell Shell { get; }

    private void RunPlayback(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            _window.Title = $"Tessitura — no se puede reproducir: {exception.Message}";
        }
    }

    public void Focus() => _canvas.Focus();

    public void Dispose()
    {
        // A clean close leaves nothing to recover; only a crash keeps the recovery copy.
        _playhead.Stop();
        _playback.Dispose();
        _autosave.Dispose();
        _autosave.Discard();
        _updates.Dispose();
        _canvas.DisposePresentation();
        Shell.Dispose();
    }

    private async Task ExportPdfAsync()
    {
        IStorageFile? file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = _input.CurrentScore.Metadata.Title,
            DefaultExtension = "pdf",
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (file?.TryGetLocalPath() is not string target)
        {
            return;
        }

        try
        {
            Score score = _input.CurrentScore;
            string musicFont = Path.Combine(_assetsPath, "Bravura.otf");
            string textFont = Path.Combine(_assetsPath, "NotoSerif[wdth,wght].ttf");
            await Task.Run(() => ScorePdfExport.Export(target, score, _style, _metadata, musicFont,
                File.Exists(textFont) ? textFont : null));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _window.Title = $"Tessitura — no se pudo exportar: {exception.Message}";
        }
    }

    private async Task SaveAsync(bool saveAs)
    {
        string? target = _path;
        if (target is null || saveAs)
        {
            IStorageFile? file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = _input.CurrentScore.Metadata.Title,
                DefaultExtension = "tess",
                FileTypeChoices = [new FilePickerFileType("Partitura Tessitura") { Patterns = ["*.tess"] }],
            });
            target = file?.TryGetLocalPath();
            if (target is null)
            {
                return;
            }
        }

        try
        {
            TessFile.Save(target, _input.CurrentScore, _style);
            _path = target;
            _recents.Add(target, _input.CurrentScore.Metadata.Title, DateTimeOffset.Now);
            _autosave.Discard();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _window.Title = $"Tessitura — no se pudo guardar: {exception.Message}";
        }
    }
}
