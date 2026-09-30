using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SkiaSharp;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Rendering;
using Tessitura.Smufl;

namespace Tessitura.Benchmarks;

[MemoryDiagnoser]
public class PresentationBenchmarks : IDisposable
{
    private SmuflMetadata _metadata = null!;
    private Style _style = null!;
    private Score _score = null!;
    private Score _currentScore = null!;
    private ScoreLayoutResult _layout = null!;
    private IncrementalScoreLayouter _layouter = null!;
    private ScorePageComposer _composer = null!;
    private DisplayListRenderer _renderer = null!;
    private SKSurface _surface = null!;
    private int _nextMeasure;
    private bool _nextIsSharp;

    [GlobalSetup]
    public void Setup()
    {
        _metadata = ReferenceScoreFactory.LoadMetadata();
        _style = Style.CreateDefault(_metadata);
        _score = ReferenceScoreFactory.Create(advancedNotation: true);
        _currentScore = _score;
        _composer = new ScorePageComposer(_metadata, _style);
        _layouter = new IncrementalScoreLayouter(_metadata);
        _layout = _layouter.Layout(_score, _style, _composer.GetAvailableWidth(_score));
        string musicFontPath = FindAsset("Bravura.otf");
        _renderer = new DisplayListRenderer(musicFontPath);
        _surface = SKSurface.Create(new SKImageInfo(1190, 1684))
            ?? throw new InvalidOperationException("The reference page surface could not be created.");
        _nextMeasure = ReferenceScoreFactory.ChangedMeasure;
        ScorePageComposition initial = _composer.Compose(_score, _layout, _nextMeasure);
        double workingSetMiB = Process.GetCurrentProcess().WorkingSet64 / (1024d * 1024d);
        Console.WriteLine($"Reference profile: systems={_layout.Systems.Length}, page primitives={initial.Page.Primitives.Length}, " +
            $"measured skyline systems={_composer.SkylineSystemMeasureCount}, staves={ReferenceScoreFactory.StaffCount}, " +
            $"measures={ReferenceScoreFactory.MeasureCount}, attachments={_score.AttachmentList.Length}.");
        Console.WriteLine($"Reference profile working set after setup: {workingSetMiB:F1} MiB.");
    }

    [Benchmark]
    public void EditLayoutComposeAndRecordSystem()
    {
        Score current = ReferenceScoreFactory.WithAccidental(_currentScore, _nextIsSharp);
        _nextIsSharp = !_nextIsSharp;
        _currentScore = current;
        _layout = _layouter.UpdateMeasure(current, _nextMeasure);
        RecordPage(current, _layout);
    }

    [Benchmark]
    public void EditLayoutComposeAndRecordPicture()
    {
        Score current = ReferenceScoreFactory.WithAccidental(_currentScore, _nextIsSharp);
        _nextIsSharp = !_nextIsSharp;
        _currentScore = current;
        _layout = _layouter.UpdateMeasure(current, _nextMeasure);
        ScorePageComposition composition = _composer.Compose(current, _layout, _nextMeasure,
            new EngravingCursor(0, new Fraction(_nextMeasure * 4L, 1)));
        PageSpatialIndex spatialIndex = new(composition.Page);
        using SKPicture picture = _renderer.Record(composition.Page,
            (float)composition.StaffSpacePoints);
        GC.KeepAlive(spatialIndex);
        GC.KeepAlive(picture);
    }

    [Benchmark]
    public void EditLayoutComposeAndIndex()
    {
        Score current = ReferenceScoreFactory.WithAccidental(_currentScore, _nextIsSharp);
        _nextIsSharp = !_nextIsSharp;
        _currentScore = current;
        _layout = _layouter.UpdateMeasure(current, _nextMeasure);
        ScorePageComposition composition = _composer.Compose(current, _layout, _nextMeasure,
            new EngravingCursor(0, new Fraction(_nextMeasure * 4L, 1)));
        PageSpatialIndex spatialIndex = new(composition.Page);
        GC.KeepAlive(spatialIndex);
    }

    [Benchmark]
    public void ComposeSameSnapshotAndRecordSystem() => RecordPage(_currentScore, _layout);

    [Benchmark]
    public void ComposeSameSnapshotDisplayList()
    {
        ScorePageComposition composition = _composer.Compose(_currentScore, _layout, _nextMeasure);
        GC.KeepAlive(composition.Page);
    }

    [Benchmark]
    public void ComposeSameSnapshotAndIndex()
    {
        ScorePageComposition composition = _composer.Compose(_currentScore, _layout, _nextMeasure);
        PageSpatialIndex spatialIndex = new(composition.Page);
        GC.KeepAlive(spatialIndex);
    }

    [Benchmark]
    public double GetAvailableWidth() => _composer.GetAvailableWidth(_currentScore);

    private void RecordPage(Score score, ScoreLayoutResult layout)
    {
        ScorePageComposition composition = _composer.Compose(score, layout, _nextMeasure,
            new EngravingCursor(0, new Fraction(_nextMeasure * 4L, 1)));
        PageSpatialIndex spatialIndex = new(composition.Page);
        using var picture = _renderer.Record(composition.Page,
            (float)composition.StaffSpacePoints);
        SKCanvas canvas = _surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Save();
        canvas.Scale(2);
        canvas.DrawPicture(picture);
        canvas.Restore();
        canvas.Flush();
        GC.KeepAlive(spatialIndex);
        GC.KeepAlive(picture);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _surface?.Dispose();
        _renderer?.Dispose();
    }

    private static string FindAsset(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "assets", "fonts", name);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"The font {name} was not found above the benchmark directory.");
    }
}
