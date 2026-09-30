using BenchmarkDotNet.Attributes;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Smufl;

namespace Tessitura.Benchmarks;

[MemoryDiagnoser]
public class LayoutBenchmarks
{
    private Style _style = null!;
    private SmuflMetadata _metadata = null!;
    private Score _score = null!;
    private Score _sharpScore = null!;
    private Score _naturalScore = null!;
    private IncrementalScoreLayouter _incrementalLayouter = null!;
    private bool _nextIsSharp;

    [GlobalSetup]
    public void Setup()
    {
        _metadata = ReferenceScoreFactory.LoadMetadata();
        _style = Style.CreateDefault(_metadata);
        _score = ReferenceScoreFactory.Create(advancedNotation: true);
        _sharpScore = ReferenceScoreFactory.WithAccidental(_score, sharp: true);
        _naturalScore = _score;
        _incrementalLayouter = new IncrementalScoreLayouter(_metadata);
        _incrementalLayouter.Layout(_score, _style, availableWidth: 120);
    }

    [Benchmark(Baseline = true)]
    public ScoreLayoutResult FullLayout()
    {
        return new IncrementalScoreLayouter(_metadata).Layout(_score, _style, availableWidth: 120);
    }

    [Benchmark]
    public ScoreLayoutResult ChangeOneNote()
    {
        Score updatedScore = _nextIsSharp ? _sharpScore : _naturalScore;
        _nextIsSharp = !_nextIsSharp;
        return _incrementalLayouter.UpdateMeasure(updatedScore, ReferenceScoreFactory.ChangedMeasure);
    }
}
