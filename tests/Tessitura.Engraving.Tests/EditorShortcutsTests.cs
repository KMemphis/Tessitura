using System.Text.RegularExpressions;
using Tessitura.App;
using Tessitura.Core;
using Xunit;

namespace Tessitura.Engraving.Tests;

/// <summary>Builds the complete action list of the editor, as EditorSession does, so a shortcut clash fails here and not at startup.</summary>
public sealed class EditorShortcutsTests
{
    [Fact]
    public void EveryEditorActionCanBeRegisteredTogetherWithoutShortcutConflicts()
    {
        Score score = new(new ScoreMetadata("T", ""),
            [.. Enumerable.Range(0, 9).Select(i => new Instrument($"I{i}", [new Staff("S")]))],
            [new Measure(1, new TimeSignature(4, 4))],
            System.Collections.Immutable.ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0),
                new StaffMeasure([new Voice(1, [new Rest(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Whole, 0))])])));
        ScoreInputController input = new(score);
        using ScoreWindowShell shell = new(new ScoreCanvas(), input);
        using PlaybackController playback = new(input, _ => throw new NotSupportedException(), () => throw new NotSupportedException());
        List<ActionDefinition> all = [.. input.CreateActions(), .. shell.CreateActions(), .. playback.CreateActions()];
        // The extra actions that EditorSession defines itself are read from its source so this list cannot drift.
        string source = File.ReadAllText(FindFile("src/Tessitura.App/EditorSession.cs"));
        foreach (Match match in Regex.Matches(source, "new\\(\"((?:view|file|midi)\\.[a-z0-9.\\-]+)\", \"[^\"]*\", \"([^\"]+)\""))
        {
            all.Add(new ActionDefinition(match.Groups[1].Value, match.Groups[1].Value, match.Groups[2].Value, () => { }));
        }

        Assert.Contains(all, a => a.Id == "file.save");
        Assert.Contains(all, a => a.Id == "midi.connect");
        Assert.Contains(all, a => a.Id == "text.lyric");
        Assert.Contains(all, a => a.Id == "repeat.start.toggle");
        Assert.Contains(all, a => a.Id == "repeat.end.toggle");
        Assert.Contains(all, a => a.Id == "repeat.ending.1.toggle");
        Assert.Contains(all, a => a.Id == "repeat.ending.2.toggle");
        Assert.Contains(all, a => a.Id == "repeat.target.segno");
        Assert.Contains(all, a => a.Id == "repeat.target.coda");
        Assert.Contains(all, a => a.Id == "repeat.jump.dc");
        Assert.Contains(all, a => a.Id == "repeat.jump.ds");
        Assert.Contains(all, a => a.Id == "repeat.jump.to-coda");
        Assert.Contains(all, a => a.Id == "repeat.jump.fine");
        string settings = Path.Combine(Path.GetTempPath(), $"tessitura-all-{Guid.NewGuid():N}.json");
        try
        {
            ActionRegistry registry = ActionRegistry.LoadOrCreate(all, settings);
            Assert.Equal(all.Count, registry.Actions.Length);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    private static string FindFile(string relative)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Tessitura.sln")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        return Path.Combine(root, relative);
    }
}
