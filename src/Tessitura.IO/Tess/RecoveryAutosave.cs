using Tessitura.Core;
using Tessitura.Engraving;

namespace Tessitura.IO.Tess;

/// <summary>Periodically writes a recovery copy of the open score next to its file.</summary>
public sealed class RecoveryAutosave : IDisposable
{
    /// <summary>The default autosave period from the project definition.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(2);

    private readonly Func<(Score Score, Style Style)?> _snapshot;
    private readonly string _recoveryPath;
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private int _disposed;

    /// <summary>Starts autosaving.</summary>
    /// <param name="documentPath">The document path; the recovery file is <c>path + ".recovery"</c>.</param>
    /// <param name="snapshot">Returns the current score and style, or null when there is nothing to save.</param>
    /// <param name="interval">The period; defaults to two minutes.</param>
    public RecoveryAutosave(string documentPath, Func<(Score Score, Style Style)?> snapshot,
        TimeSpan? interval = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentPath);
        ArgumentNullException.ThrowIfNull(snapshot);
        _recoveryPath = GetRecoveryPath(documentPath);
        _snapshot = snapshot;
        _timer = new PeriodicTimer(interval ?? DefaultInterval);
        _loop = Task.Run(RunAsync);
    }

    /// <summary>Gets the recovery file path for a document.</summary>
    /// <param name="documentPath">The document path.</param>
    /// <returns>The sibling recovery path.</returns>
    public static string GetRecoveryPath(string documentPath) => documentPath + ".recovery";

    /// <summary>Deletes the recovery copy after a normal save or close.</summary>
    public void Discard() => File.Delete(_recoveryPath);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        _timer.Dispose();
        try
        {
            _loop.Wait();
        }
        catch (AggregateException)
        {
        }

        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_stop.Token))
            {
                if (_snapshot() is (Score score, Style style))
                {
                    TessFile.Save(_recoveryPath, score, style);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
