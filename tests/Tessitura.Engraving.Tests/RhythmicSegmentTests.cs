using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Engraving;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class RhythmicSegmentTests
{
    [Fact]
    public void PianoStavesShareColumnsAtCommonOnsets()
    {
        Score score = PianoScore();

        ImmutableArray<RhythmicSegment> segments = new RhythmicSegmentBuilder().Build(score, 0);

        Assert.Equal([new Fraction(0, 1), new Fraction(1, 4),
            new Fraction(1, 2), new Fraction(3, 4)], segments.Select(segment => segment.Onset));
        Assert.Equal([0, 1], segments[0].Events.Select(item => item.StaffIndex));
        Assert.Equal([0], segments[1].Events.Select(item => item.StaffIndex));
        Assert.Equal([0, 1], segments[2].Events.Select(item => item.StaffIndex));
        Assert.Equal([0], segments[3].Events.Select(item => item.StaffIndex));
        Assert.Equal(score.Content[new StaffMeasureKey(1, 0)].Voices[0].Events[1].Id,
            segments[2].Events[1].EventId);
    }

    [Fact]
    public void DifferentVoicesAtTheSameOnsetShareOneSegment()
    {
        Score score = PianoScore();
        StaffMeasure upper = score.Content[new StaffMeasureKey(0, 0)];
        Voice secondVoice = new(2, [
            ChordAt(0, NoteValue.Half),
            ChordAt(2, NoteValue.Half),
        ]);
        score = score with
        {
            Content = score.Content.SetItem(new StaffMeasureKey(0, 0),
                new StaffMeasure(upper.Voices.Add(secondVoice))),
        };

        ImmutableArray<RhythmicSegment> segments = new RhythmicSegmentBuilder().Build(score, 0);

        Assert.Equal([1, 2, 1], segments[0].Events.Select(item => item.VoiceNumber));
        Assert.Equal(3, segments[2].Events.Length);
    }

    private static Score PianoScore()
    {
        Voice right = new(1, [
            ChordAt(0, NoteValue.Quarter),
            ChordAt(1, NoteValue.Quarter),
            ChordAt(2, NoteValue.Quarter),
            ChordAt(3, NoteValue.Quarter),
        ]);
        Voice left = new(1, [
            ChordAt(0, NoteValue.Half),
            ChordAt(2, NoteValue.Half),
        ]);
        ImmutableDictionary<StaffMeasureKey, StaffMeasure> content =
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), new StaffMeasure([right]))
                .Add(new StaffMeasureKey(1, 0), new StaffMeasure([left]));
        return new Score(new ScoreMetadata("Piano", ""),
            [new Instrument("Piano", [new Staff("Right"), new Staff("Left")])],
            [new Measure(1, new TimeSignature(4, 4))], content);
    }

    private static Chord ChordAt(int quarterIndex, NoteValue value) => new(
        new EventId(Guid.NewGuid()),
        new Fraction(quarterIndex, 4),
        new Duration(value, 0),
        [new Note(new Pitch(Step.C, 0, 4))],
        StemDirection.Auto);
}
