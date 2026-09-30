using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Smufl;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class IncrementalScoreLayouterTests
{
    [Fact]
    public void FullLayoutCachesWidthsForEveryMeasureAcrossThirtyStaves()
    {
        Score score = CreateReferenceScore(staffCount: 30, measureCount: 300);
        SmuflMetadata metadata = LoadMetadata();
        IncrementalScoreLayouter layouter = new(metadata);

        ScoreLayoutResult result = layouter.Layout(score, Style.CreateDefault(metadata), availableWidth: 120);

        Assert.Equal(300, result.MeasureWidths.Length);
        Assert.Equal(300, result.RecomputedMeasureCount);
        Assert.NotEmpty(result.Systems);
        Assert.Equal(300, result.Systems.Sum(static system => system.Range.Count));
        Assert.Same(result, layouter.Current);
    }

    [Fact]
    public void OneNoteEditRecomputesOnlyItsMeasureAndReusesStableSystemSuffix()
    {
        Score score = CreateReferenceScore(staffCount: 30, measureCount: 300);
        SmuflMetadata metadata = LoadMetadata();
        IncrementalScoreLayouter layouter = new(metadata);
        ScoreLayoutResult before = layouter.Layout(score, Style.CreateDefault(metadata), availableWidth: 120);
        const int changedMeasure = 150;
        Score editedScore = AddAccidentalToFirstNote(score, staffIndex: 0, changedMeasure);

        ScoreLayoutResult after = layouter.UpdateMeasure(editedScore, changedMeasure);

        Assert.Equal(1, after.RecomputedMeasureCount);
        Assert.NotEqual(before.MeasureWidths[changedMeasure], after.MeasureWidths[changedMeasure]);
        Assert.Equal(before.MeasureWidths.Length, after.MeasureWidths.Length);
        Assert.InRange(after.ReflowedSystemCount, 1, before.Systems.Length - 1);
        Assert.Equal(before.MeasureWidths[changedMeasure - 1], after.MeasureWidths[changedMeasure - 1]);
        Assert.Equal(before.MeasureWidths[changedMeasure + 1], after.MeasureWidths[changedMeasure + 1]);
        int nextMeasure = 0;
        foreach (SystemLine system in after.Systems)
        {
            Assert.Equal(nextMeasure, system.Range.StartIndex);
            Assert.Equal(system.Range.Count, system.MeasureWidths.Length);
            nextMeasure += system.Range.Count;
        }

        Assert.Equal(300, nextMeasure);
    }

    [Fact]
    public void PitchChangeWithUnchangedWidthReusesAllSystems()
    {
        Score score = CreateReferenceScore(staffCount: 30, measureCount: 300);
        SmuflMetadata metadata = LoadMetadata();
        IncrementalScoreLayouter layouter = new(metadata);
        ScoreLayoutResult before = layouter.Layout(score, Style.CreateDefault(metadata), 120);
        Score editedScore = ChangeFirstPitch(score, 0, 150, new Pitch(Step.D, 0, 4));

        ScoreLayoutResult after = layouter.UpdateMeasure(editedScore, 150);

        Assert.Equal(before.MeasureWidths[150], after.MeasureWidths[150]);
        Assert.Equal(1, after.RecomputedMeasureCount);
        Assert.Equal(0, after.ReflowedSystemCount);
        Assert.Same(before.Systems[0], after.Systems[0]);
        Assert.Same(before.Systems[^1], after.Systems[^1]);
    }

    [Fact]
    public void CancellationDoesNotPublishPartialLayoutState()
    {
        Score score = CreateReferenceScore(staffCount: 30, measureCount: 300);
        SmuflMetadata metadata = LoadMetadata();
        IncrementalScoreLayouter layouter = new(metadata);
        ScoreLayoutResult before = layouter.Layout(score, Style.CreateDefault(metadata), availableWidth: 120);
        Score editedScore = AddAccidentalToFirstNote(score, staffIndex: 0, measureIndex: 150);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
        {
            layouter.UpdateMeasure(editedScore, 150, cancellation.Token);
        });

        Assert.Same(before, layouter.Current);
    }

    [Fact]
    public void FullLayoutHonorsCancellationBeforePublishingAnyResult()
    {
        SmuflMetadata metadata = LoadMetadata();
        IncrementalScoreLayouter layouter = new(metadata);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
        {
            layouter.Layout(CreateReferenceScore(30, 300),
                Style.CreateDefault(metadata), 120, cancellation.Token);
        });

        Assert.Null(layouter.Current);
    }

    [Fact]
    public void MeasureWidthUsesSMuFLGlyphMetricsForRestsDotsAndAccidentals()
    {
        SmuflMetadata metadata = LoadMetadata();
        MeasureWidthCalculator calculator = new(metadata);
        Style style = Style.CreateDefault(metadata);
        Score undottedRest = CreateSingleEventScore(
            new Rest(new EventId(Guid.Empty), Fraction.Zero, new Duration(NoteValue.Quarter, 0)));
        Score dottedRest = CreateSingleEventScore(
            new Rest(new EventId(Guid.Empty), Fraction.Zero, new Duration(NoteValue.Quarter, 1)));
        Score naturalNote = CreateSingleEventScore(
            new Chord(new EventId(Guid.Empty), Fraction.Zero, new Duration(NoteValue.Quarter, 0),
                [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto));
        Score sharpNote = CreateSingleEventScore(
            new Chord(new EventId(Guid.Empty), Fraction.Zero, new Duration(NoteValue.Quarter, 0),
                [new Note(new Pitch(Step.C, 1, 4))], StemDirection.Auto));

        SystemBreakMeasure undottedWidth = calculator.Calculate(undottedRest, 0, style);
        SystemBreakMeasure dottedWidth = calculator.Calculate(dottedRest, 0, style);
        SystemBreakMeasure naturalWidth = calculator.Calculate(naturalNote, 0, style);
        SystemBreakMeasure sharpWidth = calculator.Calculate(sharpNote, 0, style);

        Assert.True(dottedWidth.MinimumWidth > undottedWidth.MinimumWidth);
        Assert.True(sharpWidth.MinimumWidth > naturalWidth.MinimumWidth);
    }

    [Fact]
    public void EmptyMeasureKeepsEnoughWidthForAVisibleWholeBarRest()
    {
        SmuflMetadata metadata = LoadMetadata();
        MeasureWidthCalculator calculator = new(metadata);
        Score score = CreateSingleEventScore(
            new Rest(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Whole, 0)));

        SystemBreakMeasure width = calculator.Calculate(score, 0, Style.CreateDefault(metadata));

        Assert.True(width.IdealWidth >= 4);
        Assert.True(width.Elasticity >= 4);
    }

    private static Score CreateReferenceScore(int staffCount, int measureCount)
    {
        ImmutableArray<Staff>.Builder staves = ImmutableArray.CreateBuilder<Staff>(staffCount);
        for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
        {
            staves.Add(new Staff($"Staff {staffIndex + 1}"));
        }

        ImmutableArray<Instrument> instruments = [new Instrument("Reference", staves.MoveToImmutable())];
        ImmutableArray<Measure>.Builder measures = ImmutableArray.CreateBuilder<Measure>(measureCount);
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        Duration quarter = new(NoteValue.Quarter, 0);
        for (int measureIndex = 0; measureIndex < measureCount; measureIndex++)
        {
            measures.Add(new Measure(measureIndex + 1, new TimeSignature(4, 4)));
            for (int staffIndex = 0; staffIndex < staffCount; staffIndex++)
            {
                ImmutableArray<MusicEvent>.Builder events = ImmutableArray.CreateBuilder<MusicEvent>(4);
                for (int beat = 0; beat < 4; beat++)
                {
                    events.Add(new Chord(new EventId(Guid.Empty), new Fraction(beat, 4), quarter,
                        [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto));
                }

                Voice voice = new(1, events.MoveToImmutable());
                content.Add(new StaffMeasureKey(staffIndex, measureIndex), new StaffMeasure([voice]));
            }
        }

        return new Score(new ScoreMetadata("Reference", "Tessitura"), instruments,
            measures.MoveToImmutable(), content.ToImmutable());
    }

    private static Score CreateSingleEventScore(MusicEvent musicEvent)
    {
        ImmutableArray<Instrument> instruments = [new Instrument("Instrument", [new Staff("Staff")])];
        ImmutableArray<Measure> measures = [new Measure(1, new TimeSignature(4, 4))];
        Voice voice = new(1, [musicEvent]);
        ImmutableDictionary<StaffMeasureKey, StaffMeasure> content =
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                new StaffMeasureKey(0, 0), new StaffMeasure([voice]));
        return new Score(new ScoreMetadata("Test", "Tessitura"), instruments, measures, content);
    }

    private static Score AddAccidentalToFirstNote(Score score, int staffIndex, int measureIndex)
        => ChangeFirstPitch(score, staffIndex, measureIndex, new Pitch(Step.C, 1, 4));

    private static Score ChangeFirstPitch(Score score, int staffIndex, int measureIndex, Pitch pitch)
    {
        StaffMeasureKey key = new(staffIndex, measureIndex);
        StaffMeasure staffMeasure = score.Content[key];
        Voice voice = staffMeasure.Voices[0];
        Chord chord = (Chord)voice.Events[0];
        Chord changedChord = chord with { Notes = [new Note(pitch)] };
        Voice changedVoice = voice with { Events = voice.Events.SetItem(0, changedChord) };
        StaffMeasure changedStaffMeasure = staffMeasure with
        {
            Voices = staffMeasure.Voices.SetItem(0, changedVoice),
        };
        return score with { Content = score.Content.SetItem(key, changedStaffMeasure) };
    }

    private static SmuflMetadata LoadMetadata()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string metadataPath = Path.Combine(directory.FullName, "assets", "fonts", "Bravura.json");
            string glyphNamesPath = Path.Combine(directory.FullName, "assets", "fonts", "smufl_glyph_names.json");
            if (File.Exists(metadataPath) && File.Exists(glyphNamesPath))
            {
                return SmuflMetadata.Load(metadataPath, glyphNamesPath);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Bravura metadata was not found above the test directory.");
    }
}
