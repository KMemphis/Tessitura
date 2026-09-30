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

    internal ScorePagePresentation(Score scoreSnapshot, Score displayScoreSnapshot,
        ScorePartView? partView, ScoreViewMode viewMode, ScorePageComposition composition,
        PageSpatialIndex spatialIndex, SKPicture picture, int buildThreadId,
        TimeSpan preparationElapsed, long requestedTimestamp)
    {
        ScoreSnapshot = scoreSnapshot;
        DisplayScoreSnapshot = displayScoreSnapshot;
        PartView = partView;
        ViewMode = viewMode;
        Composition = composition;
        SpatialIndex = spatialIndex;
        _picture = picture;
        BuildThreadId = buildThreadId;
        PreparationElapsed = preparationElapsed;
        _requestedTimestamp = requestedTimestamp;
    }

    /// <summary>Gets the score snapshot that produced this page.</summary>
    public Score ScoreSnapshot { get; }

    /// <summary>Gets the projected score snapshot that produced the displayed page.</summary>
    public Score DisplayScoreSnapshot { get; }

    /// <summary>Gets the linked part used to produce this presentation, if it is a part view.</summary>
    public ScorePartView? PartView { get; }

    /// <summary>Gets the view mode used to produce this presentation.</summary>
    public ScoreViewMode ViewMode { get; }

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
    private Score? _lastSourceScore;
    private ScoreViewMode _requestedView;
    private ScorePartView? _requestedPart;
    private ScoreViewMode _pendingView;
    private ScorePartView? _pendingPart;
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

    /// <summary>Changes the view projection and queues a fresh presentation of the current score.</summary>
    /// <param name="viewMode">The requested page, continuous or part view.</param>
    /// <param name="partView">The single-instrument part shown when <paramref name="viewMode"/> is Part.</param>
    public void SetView(ScoreViewMode viewMode, ScorePartView? partView = null)
    {
        if (!Enum.IsDefined(viewMode))
        {
            throw new ArgumentOutOfRangeException(nameof(viewMode));
        }

        if (viewMode == ScoreViewMode.Part && (partView is null || partView.InstrumentIndices.Length != 1))
        {
            throw new ArgumentException("Part view needs a part with exactly one instrument.", nameof(partView));
        }

        if (viewMode != ScoreViewMode.Part && partView is not null)
        {
            throw new ArgumentException("Only part view accepts a part definition.", nameof(partView));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _requestedView = viewMode;
            _requestedPart = partView;
            if (_started)
            {
                QueueScore(_input.CurrentScore, _input.Cursor);
            }
        }
    }

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
                ReferenceEquals(score, _lastSourceScore))
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
        _pendingView = _requestedView;
        _pendingPart = _requestedPart;
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
            ScoreViewMode viewMode;
            ScorePartView? partView;
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
                viewMode = _pendingView;
                partView = _pendingPart;
                _pendingScore = null;
                if (score is null)
                {
                    _workerRunning = false;
                    return;
                }
            }

            try
            {
                ProcessScore(score, cursor, requestedTimestamp, viewMode, partView, _shutdown.Token);
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
        ScoreViewMode viewMode, ScorePartView? partView, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Score displayScore = viewMode == ScoreViewMode.Part
            ? ScorePartProjector.Project(score, partView!)
            : score;
        int changedMeasure = _lastLayoutScore is null ? -1 :
            FindSingleChangedMeasure(_lastLayoutScore, displayScore, cancellationToken);
        ScoreLayoutResult layout;
        if (_layouter.Current is not null && changedMeasure >= 0)
        {
            layout = _layouter.UpdateMeasure(score, changedMeasure, cancellationToken);
        }
        else
        {
            double availableWidth = _composer.GetAvailableWidth(displayScore);
            layout = _layouter.Layout(score, _style, availableWidth, cancellationToken);
        }

        lock (_gate)
        {
            _lastLayoutScore = displayScore;
            _lastSourceScore = score;
        }

        cancellationToken.ThrowIfCancellationRequested();
        int displayMeasure = ResolveCursorMeasure(score, cursor.Position);
        int displayStaff = viewMode == ScoreViewMode.Part
            ? ResolvePartStaffIndex(score, partView!, cursor.StaffIndex)
            : cursor.StaffIndex;
        EngravingCursor engravingCursor = new(displayStaff, cursor.Position);
        ScorePageComposition composition = viewMode switch
        {
            ScoreViewMode.Page => _composer.Compose(displayScore, layout, displayMeasure,
                engravingCursor, cancellationToken),
            ScoreViewMode.Continuous => _composer.ComposeContinuous(displayScore, layout, displayMeasure,
                engravingCursor, cancellationToken),
            ScoreViewMode.Part => _composer.ComposePart(score, partView!, layout, displayMeasure,
                cursor: engravingCursor, cancellationToken: cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(viewMode)),
        };
        PageSpatialIndex spatialIndex = new(composition.Page);
        SKPicture picture = _renderer.Record(composition.Page,
            (float)composition.StaffSpacePoints);
        ScorePagePresentation presentation = new(score, displayScore, partView, viewMode,
            composition, spatialIndex, picture, Environment.CurrentManagedThreadId,
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
            if (_disposed || !ReferenceEquals(_input.CurrentScore, presentation.ScoreSnapshot) ||
                presentation.ViewMode != _requestedView ||
                !ReferenceEquals(presentation.PartView, _requestedPart))
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

    private static int ResolvePartStaffIndex(Score score, ScorePartView part, int sourceStaffIndex)
    {
        int staffOffset = 0;
        for (int instrumentIndex = 0; instrumentIndex < score.Instruments.Length; instrumentIndex++)
        {
            Instrument instrument = score.Instruments[instrumentIndex];
            if (instrumentIndex == part.InstrumentIndices[0])
            {
                return Math.Clamp(sourceStaffIndex - staffOffset, 0, instrument.Staves.Length - 1);
            }

            staffOffset += instrument.Staves.Length;
        }

        throw new ArgumentException("The part refers to a missing instrument.", nameof(part));
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
