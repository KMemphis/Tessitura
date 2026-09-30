using System.Collections.Immutable;
using Tessitura.App;
using Tessitura.Core;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class ScoreInspectorTests
{
    [Fact]
    public void ChangingSelectedDurationThroughRegisteredActionCanBeUndone()
    {
        Chord chord = new(new EventId(Guid.NewGuid()), Fraction.Zero,
            new Duration(NoteValue.Quarter, 0),
            [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Score original = CreateScore(chord);
        ScoreInputController input = new(original);
        using ScoreWindowShell shell = new(new ScoreCanvas(), input);
        string settings = Path.Combine(Path.GetTempPath(), $"tessitura-inspector-{Guid.NewGuid():N}.json");
        try
        {
            ActionRegistry actions = ActionRegistry.LoadOrCreate(shell.CreateActions(), settings);
            shell.AttachActionRegistry(actions);
            input.SelectEvent(chord.Id);
            Assert.Contains("Do 4", shell.InspectorDetailsText);
            Assert.Contains("Negra", shell.InspectorDetailsText);

            Assert.True(actions.TryExecute("inspector.duration.half"));

            Chord changed = Assert.IsType<Chord>(CurrentEvent(input));
            Assert.Equal(new Duration(NoteValue.Half, 0), changed.Duration);
            Assert.Equal(chord.Id, Assert.Single(input.CurrentSelection.Items).EventId);
            Assert.Contains("Blanca", shell.InspectorDetailsText);

            input.Undo();

            Assert.Equal(original, input.CurrentScore);
            Assert.Equal(chord.Id, Assert.Single(input.CurrentSelection.Items).EventId);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void DotAlterationAndTieButtonsUseScoreCommands()
    {
        Chord chord = new(new EventId(Guid.NewGuid()), Fraction.Zero,
            new Duration(NoteValue.Quarter, 0),
            [new Note(new Pitch(Step.C, 1, 4))], StemDirection.Auto);
        ScoreInputController input = new(CreateScore(chord));
        using ScoreWindowShell shell = new(new ScoreCanvas(), input);
        string settings = Path.Combine(Path.GetTempPath(), $"tessitura-inspector-{Guid.NewGuid():N}.json");
        try
        {
            ActionRegistry actions = ActionRegistry.LoadOrCreate(shell.CreateActions(), settings);
            shell.AttachActionRegistry(actions);
            input.SelectEvent(chord.Id);

            Assert.True(actions.TryExecute("inspector.dot.toggle"));
            Assert.True(actions.TryExecute("inspector.alteration.sharp"));
            Assert.Equal(1, Assert.IsType<Chord>(CurrentEvent(input)).Notes[0].Pitch.Alter);
            Assert.True(actions.TryExecute("inspector.alteration.flat"));
            Assert.Equal(-1, Assert.IsType<Chord>(CurrentEvent(input)).Notes[0].Pitch.Alter);
            Assert.True(actions.TryExecute("inspector.alteration.natural"));
            Assert.True(actions.TryExecute("inspector.tie.toggle"));

            Chord changed = Assert.IsType<Chord>(CurrentEvent(input));
            Assert.Equal(new Duration(NoteValue.Quarter, 1), changed.Duration);
            Assert.Equal(0, changed.Notes[0].Pitch.Alter);
            Assert.True(changed.Notes[0].TiedToNext);
        }
        finally
        {
            File.Delete(settings);
        }
    }

    [Fact]
    public void ChordLevelSelectionDoesNotInventANoteTarget()
    {
        Chord chord = new(new EventId(Guid.NewGuid()), Fraction.Zero,
            new Duration(NoteValue.Quarter, 0),
            [new Note(new Pitch(Step.C, 0, 4)), new Note(new Pitch(Step.E, 0, 4))],
            StemDirection.Auto);
        ScoreInputController input = new(CreateScore(chord));
        input.SelectEvent(chord.Id);

        Assert.False(input.SelectedEventProperties!.HasSelectedNote);
        Assert.False(input.ChangeSelectedAlteration(1));
        Assert.False(input.ToggleSelectedTie());
        Assert.Equal(0, Assert.IsType<Chord>(CurrentEvent(input)).Notes[0].Pitch.Alter);
    }

    private static MusicEvent CurrentEvent(ScoreInputController input) =>
        input.CurrentScore.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[0];

    private static Score CreateScore(MusicEvent musicEvent)
    {
        Rest remainder = new(new EventId(Guid.NewGuid()), musicEvent.Duration.Length,
            new Duration(NoteValue.Half, 1));
        return new Score(new ScoreMetadata("Test", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                new StaffMeasureKey(0, 0),
                new StaffMeasure([new Voice(1, [musicEvent, remainder])])));
    }
}
