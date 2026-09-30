using System.Collections.Immutable;
using Tessitura.App;
using Tessitura.Core;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class RepeatEditingTests
{
    [Fact]
    public void RegisteredRepeatActionsEditTheMeasureContainingTheSelectionAndUndo()
    {
        EventId firstId = new(Guid.NewGuid());
        EventId secondId = new(Guid.NewGuid());
        Score score = new(new ScoreMetadata("Repeats", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, new TimeSignature(4, 4)), new Measure(2, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), MeasureWith(firstId))
                .Add(new StaffMeasureKey(0, 1), MeasureWith(secondId)));
        ScoreInputController input = new(score);
        input.SelectEvent(secondId);
        ImmutableArray<ActionDefinition> actions = input.CreateActions();

        ActionDefinition startRepeat = Assert.Single(actions, static action => action.Id == "repeat.start.toggle");
        ActionDefinition ending = Assert.Single(actions, static action => action.Id == "repeat.ending.1.toggle");
        ActionDefinition segno = Assert.Single(actions, static action => action.Id == "repeat.target.segno");
        ActionDefinition dalSegno = Assert.Single(actions, static action => action.Id == "repeat.jump.ds-coda");

        startRepeat.Execute();
        ending.Execute();
        segno.Execute();
        dalSegno.Execute();

        RepeatInfo repeat = Assert.IsType<RepeatInfo>(input.CurrentScore.Measures[1].Repeat);
        Assert.Null(input.CurrentScore.Measures[0].Repeat);
        Assert.True(repeat.StartRepeat);
        Assert.Equal([1], repeat.Endings.ToArray());
        Assert.Equal(RepeatTarget.Segno, repeat.Target);
        Assert.Equal(RepeatJump.DalSegnoAlCoda, repeat.Jump);

        input.Undo();
        input.Undo();
        input.Undo();
        input.Undo();

        Assert.Equal(score.Measures.AsEnumerable(), input.CurrentScore.Measures.AsEnumerable());
    }

    private static StaffMeasure MeasureWith(EventId id) => new([
        new Voice(1, [new Rest(id, Fraction.Zero, new Duration(NoteValue.Whole, 0))])
    ]);
}
