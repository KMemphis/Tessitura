using System.Diagnostics;
using SkiaSharp;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Rendering;
using Tessitura.Smufl;

namespace Tessitura.App;

/// <summary>Owns an engraved page prepared from one immutable score snapshot.</summary>
public sealed class ScorePagePresentation : EventArgs, IDisposable
{
    private readonly object _pictureGate = new();
    private SKPicture? _picture;
    private int _referenceCount = 1;
    private bool _ownerReleased;
    private readonly long _requestedTimestamp;
    private long _publishedTimestamp;
    private long _sceneTimestamp;
    private long _paintedTimestamp;

    internal ScorePagePresentation(Score scoreSnapshot, ScorePageComposition composition,
        PageSpatialIndex spatialIndex, SKPicture picture, int buildThreadId,
        TimeSpan preparationElapsed, long requestedTimestamp)
    {
        ScoreSnapshot = scoreSnapshot;
        Composition = composition;
        SpatialIndex = spatialIndex;
        _picture = picture;
        BuildThreadId = buildThreadId;
        PreparationElapsed = preparationElapsed;
        _requestedTimestamp = requestedTimestamp;
    }

    /// <summary>Gets the score snapshot that produced this page.</summary>
    public Score ScoreSnapshot { get; }

    /// <summary>Gets the composed page and its score-system metadata.</summary>
    public ScorePageComposition Composition { get; }

    /// <summary>Gets the page hit-test grid prepared with the display list.</summary>
    public PageSpatialIndex SpatialIndex { get; }

    /// <summary>Gets the recorded vector page ready for Avalonia's Skia renderer.</summary>
    public SKPicture Picture
    {
        get
        {
            lock (_pictureGate)
            {
                return _picture ?? throw new ObjectDisposedException(nameof(ScorePagePresentation));
            }
        }
    }

    /// <summary>Gets the worker thread that composed and recorded this page.</summary>
    public int BuildThreadId { get; }

    /// <summary>Gets the time from accepting the score snapshot through display-list recording.</summary>
    public TimeSpan PreparationElapsed { get; }

    /// <summary>Gets the elapsed time through the first canvas draw, if it has been painted.</summary>
    public TimeSpan? VisibleElapsed
    {
        get
        {
            long paintedTimestamp = Volatile.Read(ref _paintedTimestamp);
            return paintedTimestamp == 0
                ? null
                : Stopwatch.GetElapsedTime(_requestedTimestamp, paintedTimestamp);
        }
    }

    /// <summary>Gets the elapsed time through publishing to the UI thread, if published.</summary>
    public TimeSpan? PublishedElapsed
    {
        get
        {
            long publishedTimestamp = Volatile.Read(ref _publishedTimestamp);
            return publishedTimestamp == 0
                ? null
                : Stopwatch.GetElapsedTime(_requestedTimestamp, publishedTimestamp);
        }
    }

    internal TimeSpan? SceneElapsed
    {
        get
        {
            long sceneTimestamp = Volatile.Read(ref _sceneTimestamp);
            return sceneTimestamp == 0 ? null : Stopwatch.GetElapsedTime(_requestedTimestamp, sceneTimestamp);
        }
    }

    internal void MarkPublished() =>
        Interlocked.CompareExchange(ref _publishedTimestamp, Stopwatch.GetTimestamp(), 0);

    internal void MarkSceneBuilt() =>
        Interlocked.CompareExchange(ref _sceneTimestamp, Stopwatch.GetTimestamp(), 0);

    internal TimeSpan? MarkPainted()
    {
        long timestamp = Stopwatch.GetTimestamp();
        long previous = Interlocked.CompareExchange(ref _paintedTimestamp, timestamp, 0);
        return previous == 0 ? Stopwatch.GetElapsedTime(_requestedTimestamp, timestamp) : null;
    }

    /// <summary>Releases the recorded Skia picture.</summary>
    public void Dispose()
    {
        SKPicture? release = null;
        lock (_pictureGate)
        {
            if (_ownerReleased)
            {
                return;
            }

            _ownerReleased = true;
            _referenceCount--;
            if (_referenceCount == 0)
            {
                release = _picture;
                _picture = null;
            }
        }

        release?.Dispose();
    }

    internal IDisposable RetainPicture()
    {
        lock (_pictureGate)
        {
            if (_picture is null)
            {
                throw new ObjectDisposedException(nameof(ScorePagePresentation));
            }

            _referenceCount++;
            return new PictureReference(this);
        }
    }

    private void ReleasePicture()
    {
        SKPicture? release = null;
        lock (_pictureGate)
        {
            _referenceCount--;
            if (_referenceCount == 0)
            {
                release = _picture;
                _picture = null;
            }
        }

        release?.Dispose();
    }

    private sealed class PictureReference(ScorePagePresentation presentation) : IDisposable
    {
        private ScorePagePresentation? _presentation = presentation;

        public void Dispose() => Interlocked.Exchange(ref _presentation, null)?.ReleasePicture();
    }
}

/// <summary>Schedules score engraving off the UI thread and publishes only the latest result.</summary>
public sealed class ScoreUpdateCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly ScoreInputController _input;
    private readonly ScorePageComposer _composer;
    private readonly Style _style;
    private readonly IncrementalScoreLayouter _layouter;
    private readonly DisplayListRenderer _renderer;
    private readonly Action<Action> _postToUi;
    private readonly CancellationTokenSource _shutdown = new();
    private Score? _pendingScore;
    private ScoreInputCursor _pendingCursor;
    private Score? _lastLayoutScore;
    private Task? _worker;
    private long _pendingTimestamp;
    private bool _started;
    private bool _workerRunning;
    private bool _disposed;

    /// <summary>Creates an update coordinator using the application's engraving assets.</summary>
    /// <param name="input">The controller that owns the immutable score history.</param>
    /// <param name="metadata">The loaded SMuFL font metadata.</param>
    /// <param name="musicFontPath">The SMuFL music font used to record display lists.</param>
    /// <param name="textFontPath">The optional text font used by display-list rendering.</param>
    /// <param name="postToUi">Posts the publication callback to Avalonia's UI thread.</param>
    public ScoreUpdateCoordinator(ScoreInputController input, SmuflMetadata metadata,
        string musicFontPath, string? textFontPath = null, Action<Action>? postToUi = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(musicFontPath);
        _style = Style.CreateDefault(metadata);
        _composer = new ScorePageComposer(metadata, _style);
        _layouter = new IncrementalScoreLayouter(metadata);
        _renderer = new DisplayListRenderer(musicFontPath, textFontPath);
        _postToUi = postToUi ?? (static action => action());
    }

    /// <summary>Raised on the supplied UI dispatcher when a current page is ready for display.</summary>
    public event EventHandler<ScorePagePresentation>? PresentationReady;

    /// <summary>Raised when a layout or recording request fails.</summary>
    public event Action<Exception>? ProcessingFailed;

    /// <summary>Begins observing score edits and queues the initial score snapshot.</summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("The score update coordinator has already started.");
            }

            _started = true;
            _input.StateChanged += OnInputStateChanged;
            QueueScore(_input.CurrentScore, _input.Cursor);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Task? worker;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pendingScore = null;
            worker = _worker;
            _input.StateChanged -= OnInputStateChanged;
            _shutdown.Cancel();
        }

        if (worker is null)
        {
            _renderer.Dispose();
            _shutdown.Dispose();
        }
        else
        {
            _ = DisposeAfterWorkerAsync(worker);
        }
    }

    private void OnInputStateChanged(object? sender, EventArgs args)
    {
        Score score = _input.CurrentScore;
        ScoreInputCursor cursor = _input.Cursor;
        lock (_gate)
        {
            if (_disposed || ReferenceEquals(score, _pendingScore) ||
                ReferenceEquals(score, _lastLayoutScore))
            {
                return;
            }

            QueueScore(score, cursor);
        }
    }

    private void QueueScore(Score score, ScoreInputCursor cursor)
    {
        _pendingScore = score;
        _pendingCursor = cursor;
        _pendingTimestamp = Stopwatch.GetTimestamp();
        if (_workerRunning)
        {
            return;
        }

        _workerRunning = true;
        _worker = Task.Run(ProcessPendingScores);
    }

    private void ProcessPendingScores()
    {
        while (true)
        {
            Score? score;
            ScoreInputCursor cursor;
            long requestedTimestamp;
            lock (_gate)
            {
                if (_disposed)
                {
                    _workerRunning = false;
                    return;
                }

                score = _pendingScore;
                cursor = _pendingCursor;
                requestedTimestamp = _pendingTimestamp;
                _pendingScore = null;
                if (score is null)
                {
                    _workerRunning = false;
                    return;
                }
            }

            try
            {
                ProcessScore(score, cursor, requestedTimestamp, _shutdown.Token);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                lock (_gate)
                {
                    _workerRunning = false;
                }

                return;
            }
            catch (Exception exception)
            {
                ProcessingFailed?.Invoke(exception);
            }
        }
    }

    private void ProcessScore(Score score, ScoreInputCursor cursor, long requestedTimestamp,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int changedMeasure = _lastLayoutScore is null ? -1 :
            FindSingleChangedMeasure(_lastLayoutScore, score, cancellationToken);
        double availableWidth = _composer.GetAvailableWidth(score);
        ScoreLayoutResult layout;
        if (_layouter.Current is not null && changedMeasure >= 0)
        {
            layout = _layouter.UpdateMeasure(score, changedMeasure, cancellationToken);
        }
        else
        {
            layout = _layouter.Layout(score, _style, availableWidth, cancellationToken);
        }

        lock (_gate)
        {
            _lastLayoutScore = score;
        }

        cancellationToken.ThrowIfCancellationRequested();
        int displayMeasure = ResolveCursorMeasure(score, cursor.Position);
        ScorePageComposition composition = _composer.Compose(score, layout, displayMeasure,
            new EngravingCursor(cursor.StaffIndex, cursor.Position), cancellationToken);
        PageSpatialIndex spatialIndex = new(composition.Page);
        SKPicture picture = _renderer.Record(composition.Page,
            (float)composition.StaffSpacePoints);
        ScorePagePresentation presentation = new(score, composition, spatialIndex, picture,
            Environment.CurrentManagedThreadId,
            Stopwatch.GetElapsedTime(requestedTimestamp), requestedTimestamp);

        try
        {
            _postToUi(() => PublishIfCurrent(presentation));
        }
        catch
        {
            presentation.Dispose();
            throw;
        }
    }

    private void PublishIfCurrent(ScorePagePresentation presentation)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_input.CurrentScore, presentation.ScoreSnapshot))
            {
                presentation.Dispose();
                return;
            }
        }

        EventHandler<ScorePagePresentation>? handler = PresentationReady;
        if (handler is null)
        {
            presentation.Dispose();
            return;
        }

        handler(this, presentation);
    }

    private static int FindSingleChangedMeasure(Score previous, Score current,
        CancellationToken cancellationToken)
    {
        if (previous.Measures.Length != current.Measures.Length ||
            previous.Instruments.Length != current.Instruments.Length)
        {
            return -1;
        }

        int changedMeasure = -1;
        for (int measureIndex = 0; measureIndex < current.Measures.Length; measureIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (previous.Measures[measureIndex] != current.Measures[measureIndex])
            {
                if (changedMeasure >= 0)
                {
                    return -1;
                }

                changedMeasure = measureIndex;
            }
        }

        foreach (KeyValuePair<StaffMeasureKey, StaffMeasure> entry in previous.Content)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!current.Content.TryGetValue(entry.Key, out StaffMeasure? currentMeasure) ||
                !ReferenceEquals(entry.Value, currentMeasure) && entry.Value != currentMeasure)
            {
                if (!SetChangedMeasure(entry.Key.MeasureIndex, ref changedMeasure))
                {
                    return -1;
                }
            }
        }

        foreach (KeyValuePair<StaffMeasureKey, StaffMeasure> entry in current.Content)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!previous.Content.ContainsKey(entry.Key) &&
                !SetChangedMeasure(entry.Key.MeasureIndex, ref changedMeasure))
            {
                return -1;
            }
        }

        return changedMeasure;
    }

    private static bool SetChangedMeasure(int measureIndex, ref int changedMeasure)
    {
        if (changedMeasure >= 0 && changedMeasure != measureIndex)
        {
            return false;
        }

        changedMeasure = measureIndex;
        return true;
    }

    private static int ResolveCursorMeasure(Score score, Fraction cursorPosition)
    {
        Fraction measureStart = Fraction.Zero;
        for (int index = 0; index < score.Measures.Length; index++)
        {
            Fraction measureEnd = measureStart + score.Measures[index].TimeSignature.Length;
            if (cursorPosition < measureEnd || index == score.Measures.Length - 1)
            {
                return index;
            }

            measureStart = measureEnd;
        }

        return score.Measures.Length - 1;
    }

    private async Task DisposeAfterWorkerAsync(Task worker)
    {
        try
        {
            await worker.ConfigureAwait(false);
        }
        catch
        {
            // Processing failures are reported through ProcessingFailed; disposal only releases resources.
        }

        _renderer.Dispose();
        _shutdown.Dispose();
    }
}
