using System.Collections.Immutable;
using Tessitura.App;
using Tessitura.Core;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class CommandPaletteTests
{
    [Fact]
    public void EveryRegisteredActionAppearsAndRunsFromThePalette()
    {
        using Harness harness = new();
        CommandPalette palette = harness.Shell.CommandPalette!;

        Assert.True(harness.Actions.TryExecute("command-palette.open"));
        Assert.True(palette.IsOpen);

        Assert.Equal(harness.Actions.Actions.Length, palette.Results.Length);
        HashSet<string> shown = [.. palette.Results.Select(action => action.Id)];
        foreach (RegisteredAction action in harness.Actions.Actions)
        {
            Assert.Contains(action.Id, shown);
        }

        int executed = 0;
        foreach (RegisteredAction action in harness.Actions.Actions)
        {
            palette.Open();
            palette.Query = action.Id;
            int index = -1;
            for (int i = 0; i < palette.Results.Length; i++)
            {
                if (palette.Results[i].Id == action.Id)
                {
                    index = i;
                }
            }

            Assert.True(index >= 0, action.Id);
            palette.SelectedIndex = index;
            Assert.True(palette.ExecuteSelected(), action.Id);
            // The palette's own action legitimately reopens it.
            Assert.Equal(action.Id == "command-palette.open", palette.IsOpen);
            executed++;
        }

        Assert.Equal(harness.Actions.Actions.Length, executed);
    }

    [Fact]
    public void SearchIsFuzzyAndIgnoresCaseAndAccents()
    {
        using Harness harness = new();
        ImmutableArray<RegisteredAction> actions = harness.Actions.Actions;

        Assert.Equal("palette.clef.bass", CommandSearch.Search(actions, "clave fa")[0].Id);
        Assert.Contains(CommandSearch.Search(actions, "PAGINA"), a => a.Id == "view.page");
        Assert.Contains(CommandSearch.Search(actions, "vsta pgn"), a => a.Id == "view.page");
        Assert.Empty(CommandSearch.Search(actions, "zzzzqx"));
        Assert.Equal(actions.Length, CommandSearch.Search(actions, "  ").Length);
    }

    [Fact]
    public void ChoosingAnActionAppliesItToTheSelectionThroughTheRegistry()
    {
        using Harness harness = new();
        CommandPalette palette = harness.Shell.CommandPalette!;
        harness.Input.SelectEvent(harness.FirstEventId);

        harness.Actions.TryExecute("command-palette.open");
        palette.Query = "clave fa";
        Assert.Equal(0, palette.SelectedIndex);
        Assert.Equal("palette.clef.bass", palette.Results[0].Id);
        Assert.True(palette.ExecuteSelected());

        Assert.Equal(Clef.Bass, harness.Input.CurrentScore.Instruments[0].Staves[0].InitialClef);
        harness.Input.Undo();
        Assert.Equal(Clef.Treble, harness.Input.CurrentScore.Instruments[0].Staves[0].InitialClef);
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _settings =
            Path.Combine(Path.GetTempPath(), $"tessitura-cmd-{Guid.NewGuid():N}.json");

        public Harness()
        {
            FirstEventId = new EventId(Guid.NewGuid());
            Rest rest = new(FirstEventId, Fraction.Zero, new Duration(NoteValue.Whole, 0));
            Score score = new(new ScoreMetadata("T", ""),
                [new Instrument("Piano", [new Staff("S")])],
                [new Measure(1, new TimeSignature(4, 4))],
                ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                    new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [rest])])));
            Input = new ScoreInputController(score);
            Shell = new ScoreWindowShell(new ScoreCanvas(), Input);
            Actions = ActionRegistry.LoadOrCreate(
                Input.CreateActions().AddRange(Shell.CreateActions()), _settings);
            Shell.AttachActionRegistry(Actions);
        }

        public EventId FirstEventId { get; }

        public ScoreInputController Input { get; }

        public ScoreWindowShell Shell { get; }

        public ActionRegistry Actions { get; }

        public void Dispose()
        {
            Shell.Dispose();
            File.Delete(_settings);
        }
    }
}
