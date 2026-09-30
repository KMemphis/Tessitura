using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Editing;
using Xunit;

namespace Tessitura.Core.Tests;

public sealed class TupletTests
{
    [Theory]
    [InlineData(3, 2, NoteValue.Eighth, 1, 4)]
    [InlineData(5, 4, NoteValue.Sixteenth, 1, 4)]
    [InlineData(7, 4, NoteValue.Sixteenth, 1, 4)]
    [InlineData(3, 2, NoteValue.Quarter, 1, 2)]
    [InlineData(6, 4, NoteValue.Sixteenth, 1, 4)]
    [InlineData(2, 3, NoteValue.Eighth, 3, 8)]
    public void GroupLengthAndMemberDurationsAddUpExactlyWithFractions(int actual, int normal, NoteValue unit, int num, int den)
    {
        TupletGroup group = Group(actual, normal, unit);

        Assert.True(group.IsConsistent());
        Assert.Equal(new Fraction(num, den), group.Length);
        Fraction total = Fraction.Zero;
        foreach ((MusicEvent _, Fraction _, Fraction length) in new MusicEvent[] { group }.Flatten())
        {
            total += length;
        }

        Assert.Equal(group.Length, total);
    }

    [Fact]
    public void FlattenGivesExactSoundingOnsetsAndLengthsIncludingNestedGroups()
    {
        // A quarter-note triplet (3:2 of quarters, half-note span) whose second member is an eighth triplet.
        Rest first = Rest(Fraction.Zero, NoteValue.Quarter);
        TupletGroup inner = Group(3, 2, NoteValue.Eighth) with { Onset = new Fraction(1, 6) };
        Rest third = Rest(new Fraction(1, 3), NoteValue.Quarter);
        TupletGroup outer = new(Id(), Fraction.Zero, new Duration(NoteValue.Quarter, 0), 3, 2, [first, inner, third]);

        Assert.True(outer.IsConsistent());
        var leaves = new MusicEvent[] { outer }.Flatten();

        Assert.Equal(Fraction.One / new Fraction(6, 1), leaves[0].Length);
        Assert.Equal(new Fraction(1, 6), leaves[1].Onset);
        Assert.Equal(new Fraction(1, 18), leaves[1].Length);
        Assert.Equal(new Fraction(1, 6) + new Fraction(1, 18) * new Fraction(3, 1), leaves[4].Onset);
        Assert.Equal(new Fraction(1, 2), outer.Length);
    }

    [Fact]
    public void InconsistentGroupsAreRejected()
    {
        Assert.False((Group(3, 2, NoteValue.Eighth) with { Children = [] }).IsConsistent());
        Assert.False((Group(3, 3, NoteValue.Eighth)).IsConsistent());
        Assert.False((Group(3, 2, NoteValue.Eighth) with { Actual = 4 }).IsConsistent());
        TupletGroup gap = Group(3, 2, NoteValue.Eighth);
        Assert.False((gap with { Children = gap.Children.RemoveAt(1) }).IsConsistent());
        TupletGroup moved = Group(3, 2, NoteValue.Eighth);
        Assert.False((moved with { Children = moved.Children.SetItem(1, moved.Children[1].WithOnset(new Fraction(1, 5))) }).IsConsistent());
    }

    [Fact]
    public void MeasureValidityCountsAGroupByItsRealLength()
    {
        Score score = ScoreWith([Group(3, 2, NoteValue.Eighth), Rest(new Fraction(1, 4), NoteValue.Half, 1)]);
        Score broken = ScoreWith([Group(3, 2, NoteValue.Eighth) with { Actual = 4 }, Rest(new Fraction(1, 4), NoteValue.Half, 1)]);

        Assert.True(ScoreValidator.IsMeasureValid(score.Measures[0], score.Content[new StaffMeasureKey(0, 0)]));
        Assert.False(ScoreValidator.IsMeasureValid(broken.Measures[0], broken.Content[new StaffMeasureKey(0, 0)]));
    }

    [Fact]
    public void CreateTupletTurnsARestIntoAValidGroupAndCanBeEditedInside()
    {
        Rest quarter = Rest(new Fraction(1, 4), NoteValue.Quarter);
        Score score = ScoreWith([Rest(Fraction.Zero, NoteValue.Quarter), quarter, Rest(new Fraction(1, 2), NoteValue.Half)]);
        EditContext context = new(0, 0, 1);

        Score created = new CreateTupletCommand(quarter.Id, 3, 2).Apply(score, context);

        TupletGroup group = Assert.IsType<TupletGroup>(created.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[1]);
        Assert.Equal((3, 2, new Duration(NoteValue.Eighth, 0)), (group.Actual, group.Normal, group.Duration));
        Assert.Equal(new Fraction(1, 4), group.Onset);
        Assert.True(ScoreValidator.IsMeasureValid(created.Measures[0], created.Content[new StaffMeasureKey(0, 0)]));
        Assert.Same(score.Content, score.Content); // the original snapshot is untouched
        Assert.IsType<Rest>(score.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[1]);

        Score noted = new InsertNoteCommand(group.Children[1].Id, new Pitch(Step.G, 1, 4)).Apply(created, context);

        TupletGroup after = Assert.IsType<TupletGroup>(noted.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[1]);
        Assert.IsType<Chord>(after.Children[1]);
        Assert.Equal(group.Children[1].Onset, after.Children[1].Onset);
        Assert.True(ScoreValidator.IsMeasureValid(noted.Measures[0], noted.Content[new StaffMeasureKey(0, 0)]));
        Assert.Throws<InvalidOperationException>(() =>
            new ChangeDurationCommand(group.Children[0].Id, new Duration(NoteValue.Quarter, 0)).Apply(created, context));
    }

    [Fact]
    public void CreateTupletRejectsNonRestsAndUnrepresentableUnits()
    {
        Chord chord = new(Id(), Fraction.Zero, new Duration(NoteValue.Quarter, 0), [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Rest half = Rest(new Fraction(1, 4), NoteValue.Half);
        Score score = ScoreWith([chord, half, Rest(new Fraction(3, 4), NoteValue.Quarter)]);
        EditContext context = new(0, 0, 1);

        Assert.Throws<InvalidOperationException>(() => new CreateTupletCommand(chord.Id, 3, 2).Apply(score, context));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CreateTupletCommand(half.Id, 4, 4).Apply(score, context));
        // A half rest split into three units would need 1/6-note values, which do not exist.
        Assert.Throws<InvalidOperationException>(() => new CreateTupletCommand(half.Id, 4, 3).Apply(score, context));
    }

    private static TupletGroup Group(int actual, int normal, NoteValue unit)
    {
        Duration duration = new(unit, 0);
        Fraction step = duration.Length * new Fraction(normal, actual);
        ImmutableArray<MusicEvent>.Builder children = ImmutableArray.CreateBuilder<MusicEvent>();
        for (int i = 0; i < actual; i++)
        {
            children.Add(new Rest(Id(), step * new Fraction(i, 1), duration));
        }

        return new TupletGroup(Id(), Fraction.Zero, duration, actual, normal, children.ToImmutable());
    }

    private static Rest Rest(Fraction onset, NoteValue value, int dots = 0) => new(Id(), onset, new Duration(value, dots));

    private static EventId Id() => new(Guid.NewGuid());

    private static Score ScoreWith(MusicEvent[] events)
    {
        List<MusicEvent> list = [.. events];
        Fraction end = list[^1].Onset + list[^1].Length;
        if (end < Fraction.One)
        {
            list.Add(Rest(end, NoteValue.Quarter));
        }

        return new Score(new ScoreMetadata("T", ""), [new Instrument("I", [new Staff("S")])],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [.. list])])));
    }
}
