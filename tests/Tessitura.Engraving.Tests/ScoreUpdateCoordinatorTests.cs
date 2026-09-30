using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia;
using Avalonia.Input;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Editing;
using Tessitura.Smufl;
using Xunit;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;

namespace Tessitura.Engraving.Tests;

public sealed class ScoreUpdateCoordinatorTests
{
    [Fact]
    public async Task NoteEntryBuildsAndPublishesTheNewPageOnAWorkerThread()
    {
        SmuflMetadata metadata = LoadMetadata();
        ScoreInputController input = new(CreateScore());
        ScoreCanvas canvas = new() { ScoreInputController = input };
        string musicFontPath = FindAsset("Bravura.otf");
        using ScoreUpdateCoordinator coordinator = new(input, metadata, musicFontPath,
            postToUi: static action => action());
        TaskCompletionSource<ScorePagePresentation> firstPresentation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<ScorePagePresentation> editedPresentation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        int presentationCount = 0;
        coordinator.PresentationReady += (_, presentation) =>
        {
            if (Interlocked.Increment(ref presentationCount) == 1)
            {
                firstPresentation.TrySetResult(presentation);
            }
            else
            {
                canvas.AttachPresentation(presentation);
                editedPresentation.TrySetResult(presentation);
            }
        };

        int callerThreadId = Environment.CurrentManagedThreadId;
        coordinator.Start();
        using ScorePagePresentation initial = await firstPresentation.Task.WaitAsync(TimeSpan.FromSeconds(10));
        input.EnterNoteEntry();
        ActionDefinition writeDo = input.CreateActions().Single(action => action.Id == "score.note.c");
        Stopwatch stopwatch = Stopwatch.StartNew();

        writeDo.Execute();

        ScorePagePresentation updated = await editedPresentation.Task.WaitAsync(TimeSpan.FromSeconds(10));
        stopwatch.Stop();

        Assert.NotEqual(callerThreadId, updated.BuildThreadId);
        Assert.Same(input.CurrentScore, updated.ScoreSnapshot);
        Assert.Contains(updated.Composition.Page.Primitives.OfType<DisplayGlyph>(), glyph =>
            glyph.Codepoint == metadata.GetGlyphCodepoint("noteheadBlack"));
        Assert.True(updated.Composition.CursorLocation.HasValue);
        Assert.True(updated.PreparationElapsed > TimeSpan.Zero);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));

        DisplayGlyph notehead = Assert.Single(
            updated.Composition.Page.Primitives.OfType<DisplayGlyph>(), glyph =>
                glyph.Codepoint == metadata.GetGlyphCodepoint("noteheadBlack"));
        Point noteCenter = new(
            80 + (notehead.Bounds.X + notehead.Bounds.Width / 2) * updated.Composition.StaffSpacePoints,
            40 + (notehead.Bounds.Y + notehead.Bounds.Height / 2) * updated.Composition.StaffSpacePoints);
        Assert.True(canvas.SelectAt(noteCenter, KeyModifiers.None));
        Assert.Equal(new EventId(notehead.ElementId.Value),
            Assert.Single(input.CurrentSelection.Items).EventId);
        canvas.DisposePresentation();
    }

    [Fact]
    public async Task SupersededPresentationIsDiscardedBeforeItReachesTheUi()
    {
        SmuflMetadata metadata = LoadMetadata();
        ScoreInputController input = new(CreateScore());
        ConcurrentQueue<Action> uiQueue = new();
        using SemaphoreSlim queuedActions = new(0);
        using ScoreUpdateCoordinator coordinator = new(input, metadata, FindAsset("Bravura.otf"),
            postToUi: action =>
            {
                uiQueue.Enqueue(action);
                queuedActions.Release();
            });
        TaskCompletionSource<ScorePagePresentation> published =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.PresentationReady += (_, presentation) => published.TrySetResult(presentation);

        coordinator.Start();
        await queuedActions.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(uiQueue.TryDequeue(out Action? obsoleteAction));
        input.EnterNoteEntry();
        input.CreateActions().Single(action => action.Id == "score.note.c").Execute();
        await queuedActions.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(uiQueue.TryDequeue(out Action? currentAction));

        obsoleteAction!();
        Assert.False(published.Task.IsCompleted);
        currentAction!();
        using ScorePagePresentation result = await published.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(input.CurrentScore, result.ScoreSnapshot);
    }

    [Fact]
    public async Task PartViewProjectsTheSelectedInstrumentWithoutReplacingTheMasterScore()
    {
        Score score = CreatePartScore(out EventId partEvent);
        ScorePartView part = Assert.Single(score.PartList, candidate => candidate.Name == "Oboe");
        ScoreInputController input = new(score);
        using ScoreUpdateCoordinator coordinator = new(input, LoadMetadata(), FindAsset("Bravura.otf"),
            postToUi: static action => action());
        TaskCompletionSource<ScorePagePresentation> ready =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.PresentationReady += (_, presentation) => ready.TrySetResult(presentation);

        coordinator.SetView(ScoreViewMode.Part, part);
        coordinator.Start();
        using ScorePagePresentation presentation = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(score, presentation.ScoreSnapshot);
        Assert.Single(presentation.DisplayScoreSnapshot.Instruments);
        Assert.Equal("Oboe", presentation.PartView?.Name);
        Assert.Contains(presentation.DisplayScoreSnapshot.Content.Values.SelectMany(staffMeasure =>
            staffMeasure.Voices).SelectMany(voice => voice.Events), musicEvent => musicEvent.Id == partEvent);
        Assert.True(input.ContainsEvent(partEvent));
    }

    private static Score CreateScore()
    {
        EventId eventId = new(Guid.NewGuid());
        Rest rest = new(eventId, Fraction.Zero, new Duration(NoteValue.Whole, 0));
        return new Score(new ScoreMetadata("Test", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [rest])])));
    }

    private static Score CreatePartScore(out EventId partEvent)
    {
        EventId fluteEvent = new(Guid.NewGuid());
        partEvent = new EventId(Guid.NewGuid());
        Duration whole = new(NoteValue.Whole, 0);
        Chord flute = new(fluteEvent, Fraction.Zero, whole,
            [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Chord oboe = new(partEvent, Fraction.Zero, whole,
            [new Note(new Pitch(Step.G, 0, 4))], StemDirection.Auto);
        return new Score(new ScoreMetadata("Parts", ""),
            [new Instrument("Flute", [new Staff("Flute")]), new Instrument("Oboe", [new Staff("Oboe")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [flute])]))
                .Add(new StaffMeasureKey(1, 0), new StaffMeasure([new Voice(1, [oboe])])),
            Parts: [new ScorePartView("Flute", [0]), new ScorePartView("Oboe", [1])]);
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

        throw new FileNotFoundException($"The font {name} was not found above the test directory.");
    }

    private static SmuflMetadata LoadMetadata() => SmuflMetadata.Load(
        FindAsset("Bravura.json"), FindAsset("smufl_glyph_names.json"));
}
