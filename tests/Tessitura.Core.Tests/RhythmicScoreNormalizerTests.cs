using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Editing;
using Xunit;

namespace Tessitura.Core.Tests;

public sealed class RhythmicScoreNormalizerTests
{
    [Fact]
    public void EditingOneMeasurePreservesOtherMeasureSnapshots()
    {
        Chord firstChord = MakeChord(0);
        StaffMeasure firstMeasure = MakeMeasure(firstChord);
        StaffMeasure unchangedMeasure = MakeMeasure(MakeChord(0));
        Score score = new(new ScoreMetadata("Sharing", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, new TimeSignature(4, 4)), new Measure(2, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), firstMeasure)
                .Add(new StaffMeasureKey(0, 1), unchangedMeasure));

        Score edited = new ChangePitchCommand(firstChord.Id, 0,
            new Pitch(Step.D, 0, 4)).Apply(score, new EditContext(0, 0, 1));

        Assert.NotSame(firstMeasure, edited.Content[new StaffMeasureKey(0, 0)]);
        Assert.Same(unchangedMeasure, edited.Content[new StaffMeasureKey(0, 1)]);
        Assert.True(ScoreValidator.IsMeasureValid(edited.Measures[0],
            edited.Content[new StaffMeasureKey(0, 0)]));
        Assert.True(ScoreValidator.IsMeasureValid(edited.Measures[1],
            edited.Content[new StaffMeasureKey(0, 1)]));
    }

    private static StaffMeasure MakeMeasure(Chord first)
    {
        ImmutableArray<MusicEvent>.Builder events = ImmutableArray.CreateBuilder<MusicEvent>(4);
        for (int beat = 0; beat < 4; beat++)
        {
            events.Add(beat == 0
                ? first
                : MakeChord(beat));
        }

        return new StaffMeasure([new Voice(1, events.MoveToImmutable())]);
    }

    private static Chord MakeChord(int beat) =>
        new(new EventId(Guid.NewGuid()), new Fraction(beat, 4), new Duration(NoteValue.Quarter, 0),
            [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
}
