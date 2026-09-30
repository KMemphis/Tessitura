using Avalonia.Controls;
using Avalonia.Interactivity;
using Tessitura.App;
using Tessitura.Core;
using Tessitura.Editing;
using Tessitura.IO;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class StartScreenTests : IDisposable
{
    [Fact]
    public void StartScreenExplainsHowToWriteTheFirstNote()
    {
        (StartScreen screen, _) = CreateScreen(out ActionRegistry _);
        Assert.Contains("Escribir notas", screen.FirstStepsText);
        Assert.Contains("pentagrama", screen.FirstStepsText);
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tessitura-start-{Guid.NewGuid():N}");

    public StartScreenTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("start.template.piano", 1, 2, new[] { Clef.Treble, Clef.Bass })]
    [InlineData("start.template.string-quartet", 4, 4,
        new[] { Clef.Treble, Clef.Treble, Clef.Alto, Clef.Bass })]
    [InlineData("start.template.satb", 4, 4,
        new[] { Clef.Treble, Clef.Treble, Clef.Treble, Clef.Bass })]
    public void EachTemplateIsCreatedInTwoClicks(string templateAction, int instruments, int staves, Clef[] clefs)
    {
        (StartScreen screen, StartScreenController controller) = CreateScreen(out ActionRegistry _);
        Score? created = null;
        controller.ScoreCreated += (_, score) => created = score;
        int clicks = 0;

        Click(screen, templateAction, ref clicks);
        Click(screen, "start.create", ref clicks);

        Assert.NotNull(created);
        Assert.True(clicks < 5);
        Assert.Equal(instruments, created.Instruments.Length);
        List<Clef> actualClefs = [];
        foreach (Instrument instrument in created.Instruments)
        {
            foreach (Staff staff in instrument.Staves)
            {
                actualClefs.Add(staff.InitialClef);
            }
        }

        Assert.Equal(clefs, actualClefs);
        Assert.Equal(staves, actualClefs.Count);
        AssertAllMeasuresValid(created, staves);
    }

    [Fact]
    public void WizardChoicesReachTheScoreAndEveryMeasureStaysValid()
    {
        (StartScreen screen, StartScreenController controller) = CreateScreen(out ActionRegistry actions);
        Score? created = null;
        controller.ScoreCreated += (_, score) => created = score;

        Assert.True(actions.TryExecute("start.template.piano"));
        controller.Options = controller.Options with
        {
            Title = "Nocturno", Composer = "Chopin",
            TimeSignature = new TimeSignature(6, 8), KeySignature = new KeySignature(-3),
        };
        Assert.True(actions.TryExecute("start.create"));

        Assert.NotNull(created);
        Assert.Equal(new ScoreMetadata("Nocturno", "Chopin"), created.Metadata);
        Assert.All(created.Measures, measure =>
        {
            Assert.Equal(new TimeSignature(6, 8), measure.TimeSignature);
            Assert.Equal(new KeySignature(-3), measure.KeySignature);
        });
        AssertAllMeasuresValid(created, 2);
        Assert.NotNull(screen.Content);
    }

    [Fact]
    public void RecentScoresAreShownAndOpenedFromTheHomePage()
    {
        string file = Path.Combine(_directory, "recents.json");
        RecentScores recents = new(file);
        recents.Add(Path.Combine(_directory, "a.tess"), "A", DateTimeOffset.UnixEpoch);
        recents.Add(Path.Combine(_directory, "b.tess"), "B", DateTimeOffset.UnixEpoch.AddDays(1));
        (StartScreen screen, StartScreenController controller) = CreateScreen(out ActionRegistry _, recents);
        string? chosen = null;
        controller.RecentChosen += (_, path) => chosen = path;
        int clicks = 0;

        Assert.True(screen.Buttons.ContainsKey("start.recent.1"));
        Click(screen, "start.recent.0", ref clicks);

        Assert.Equal(Path.Combine(_directory, "b.tess"), chosen);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void RecentListKeepsTenUniqueEntriesAndSurvivesRestartOrCorruption()
    {
        string file = Path.Combine(_directory, "recents.json");
        RecentScores recents = new(file);
        for (int index = 0; index < 12; index++)
        {
            recents.Add(Path.Combine(_directory, $"{index}.tess"), $"T{index}", DateTimeOffset.UnixEpoch.AddDays(index));
        }

        recents.Add(Path.Combine(_directory, "5.tess"), "Again", DateTimeOffset.UnixEpoch.AddDays(20));
        RecentScores reloaded = new(file);

        Assert.Equal(RecentScores.Capacity, reloaded.Items.Count);
        Assert.Equal("Again", reloaded.Items[0].Title);
        Assert.Single(reloaded.Items, item => item.Path.EndsWith("5.tess", StringComparison.Ordinal));
        File.WriteAllText(file, "{ not json");
        Assert.Empty(new RecentScores(file).Items);
    }

    private (StartScreen, StartScreenController) CreateScreen(out ActionRegistry actions, RecentScores? recents = null)
    {
        StartScreenController controller = new(recents ?? new RecentScores(Path.Combine(_directory, "recents.json")));
        StartScreen screen = new(controller);
        actions = ActionRegistry.LoadOrCreate(controller.CreateActions(),
            Path.Combine(_directory, $"start-{Guid.NewGuid():N}.json"));
        screen.AttachActionRegistry(actions);
        return (screen, controller);
    }

    private static void Click(StartScreen screen, string actionId, ref int clicks)
    {
        screen.Buttons[actionId].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        clicks++;
    }

    private static void AssertAllMeasuresValid(Score score, int staffCount)
    {
        Assert.Equal(score.Measures.Length * staffCount, score.Content.Count);
        for (int staff = 0; staff < staffCount; staff++)
        {
            for (int measure = 0; measure < score.Measures.Length; measure++)
            {
                Assert.True(ScoreValidator.IsMeasureValid(score.Measures[measure],
                    score.Content[new StaffMeasureKey(staff, measure)]));
            }
        }
    }
}
