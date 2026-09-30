using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Editing;
using Xunit;

namespace Tessitura.Core.Tests;

public sealed class ScoreModelTests
{
    [Fact]
    public void FourQuarterNotesFillFourFourMeasure()
    {
        Measure measure = new(1, new TimeSignature(4, 4));
        StaffMeasure content = WithQuarterNotes(4);

        Assert.True(ScoreValidator.IsMeasureValid(measure, content));
    }

    [Fact]
    public void FiveQuarterNotesOverfillFourFourMeasure()
    {
        Measure measure = new(1, new TimeSignature(4, 4));
        StaffMeasure content = WithQuarterNotes(5);

        Assert.False(ScoreValidator.IsMeasureValid(measure, content));
    }

    [Fact]
    public void LyricModelRequiresPositiveVersesTextAndKnownMarkValues()
    {
        EventId target = new(Guid.NewGuid());
        Assert.Throws<ArgumentOutOfRangeException>(() => new LyricAttachment(target, 0, "la"));
        Assert.Throws<ArgumentException>(() => new LyricAttachment(target, 1, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LyricAttachment(target, 1, "la", (LyricSyllabic)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LyricAttachment(target, 1, "la", Extender: (LyricExtender)99));
        Assert.Equal(string.Empty, new LyricAttachment(target, 1, "", Extender: LyricExtender.Stop).Text);
    }

    [Fact]
    public void GapInVoiceFailsEvenWhenDurationsSumToMeasureLength()
    {
        Measure measure = new(1, new TimeSignature(4, 4));
        ImmutableArray<MusicEvent> events =
        [
            QuarterChord(0),
            QuarterChord(2),
            QuarterChord(3),
            QuarterChord(4),
        ];
        StaffMeasure content = new([new Voice(1, events)]);

        Assert.False(ScoreValidator.IsMeasureValid(measure, content));
    }

    [Fact]
    public void ScoreSharesImmutableCollections()
    {
        Measure measure = new(1, new TimeSignature(4, 4));
        StaffMeasure content = WithQuarterNotes(4);
        Score score = new(
            new ScoreMetadata("Test", "Composer"),
            [new Instrument("Piano", [new Staff("Right hand")])],
            [measure],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0), content));

        Assert.True(ScoreValidator.IsMeasureValid(score.Measures[0], score.Content[new StaffMeasureKey(0, 0)]));
    }

    [Fact]
    public void PartProjectionUsesTheEditedSnapshotAndRemapsItsStaffContent()
    {
        EventId first = new(Guid.NewGuid());
        EventId second = new(Guid.NewGuid());
        Score score = new(new ScoreMetadata("Symphony", "Composer"),
            [
                new Instrument("Flute", [new Staff("Flute")]),
                new Instrument("Piano", [new Staff("Right"), new Staff("Left", Clef.Bass)]),
            ],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), MakeQuarter(first, new Pitch(Step.C, 0, 5)))
                .Add(new StaffMeasureKey(1, 0), MakeQuarter(second, new Pitch(Step.C, 0, 4)))
                .Add(new StaffMeasureKey(2, 0), MakeQuarter(new EventId(Guid.NewGuid()), new Pitch(Step.C, 0, 3))));
        ScorePartView part = new("Piano", [1]);
        Score withPart = score with
        {
            Parts = [part],
            Attachments =
            [
                new DynamicAttachment(first, DynamicLevel.F),
                new DynamicAttachment(second, DynamicLevel.P),
            ],
            Spanners = [new Spanner(first, second, SpannerKind.Slur)],
        };

        Score edited = new ChangePitchCommand(second, 0, new Pitch(Step.G, 1, 4))
            .Apply(withPart, new EditContext(1, 0, 1));
        Score projected = ScorePartProjector.Project(edited, part);

        Assert.Empty(projected.PartList);
        Assert.Equal("Piano", Assert.Single(projected.Instruments).Name);
        Assert.Equal(2, projected.Instruments[0].Staves.Length);
        Assert.Equal(2, projected.Content.Count);
        Assert.Equal(new DynamicAttachment(second, DynamicLevel.P), Assert.Single(projected.AttachmentList));
        Assert.Empty(projected.SpannerList);
        Assert.Contains(new StaffMeasureKey(0, 0), projected.Content.Keys);
        Assert.Contains(new StaffMeasureKey(1, 0), projected.Content.Keys);
        Assert.Equal(new Pitch(Step.G, 1, 4), Assert.IsType<Chord>(
            projected.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[0]).Notes[0].Pitch);
        Assert.Equal(new Pitch(Step.C, 0, 5), Assert.IsType<Chord>(
            score.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[0]).Notes[0].Pitch);
    }

    [Fact]
    public void PartViewRejectsInvalidInstrumentSelections()
    {
        Assert.Throws<ArgumentException>(() => new ScorePartView(" ", [0]));
        Assert.Throws<ArgumentException>(() => new ScorePartView("Flute", []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScorePartView("Flute", [-1]));
        Assert.Throws<ArgumentException>(() => new ScorePartView("Flute", [0, 0]));
    }

    [Fact]
    public void BbClarinetPartShowsWrittenAndConcertPitchesAsViewsOfTheSameScore()
    {
        EventId noteId = new(Guid.NewGuid());
        Pitch writtenPitch = new(Step.D, 0, 4);
        Score score = new(new ScoreMetadata("Clarinet", ""),
            [new Instrument("Clarinet in B-flat", [new Staff("Clarinet")], new Interval(-1, -2))],
            [new Measure(1, new TimeSignature(4, 4), new KeySignature(0))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0),
                new StaffMeasure([new Voice(1,
                [
                    new Chord(noteId, Fraction.Zero, new Duration(NoteValue.Quarter, 0),
                        [new Note(writtenPitch)], StemDirection.Auto),
                    new Rest(new EventId(Guid.NewGuid()), new Fraction(1, 4), new Duration(NoteValue.Half, 1)),
                ])])));
        ScorePartView part = new("Clarinet in B-flat", [0]);

        Score written = ScorePartProjector.Project(score, part, PitchDisplayMode.Written);
        Score concert = ScorePartProjector.Project(score, part, PitchDisplayMode.Concert);
        Chord writtenChord = Assert.IsType<Chord>(written.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[0]);
        Chord concertChord = Assert.IsType<Chord>(concert.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[0]);

        Assert.Equal(writtenPitch, writtenChord.Notes[0].Pitch);
        Assert.Equal(new Pitch(Step.C, 0, 4), concertChord.Notes[0].Pitch);
        Assert.Equal(new KeySignature(0), written.Measures[0].KeySignature);
        Assert.Equal(new KeySignature(0), concert.Measures[0].KeySignature);
        Assert.Equal(default, Assert.Single(concert.Instruments).Transposition);
        Assert.Equal(writtenPitch, Assert.IsType<Chord>(
            score.Content[new StaffMeasureKey(0, 0)].Voices[0].Events[0]).Notes[0].Pitch);
    }

    [Fact]
    public void KeySignaturesTransposeBetweenConventionalMajorKeysAndBack()
    {
        KeySignature concertC = new KeySignature(0);
        KeySignature writtenD = new KeySignature(2);
        Interval writtenToConcert = new(-1, -2);

        Assert.Equal(concertC, writtenD.Transpose(writtenToConcert));
        Assert.Equal(writtenD, concertC.Transpose(writtenToConcert.Inverse()));
    }

    private static StaffMeasure MakeQuarter(EventId id, Pitch pitch) => new(
        [new Voice(1, [new Chord(id, Fraction.Zero, new Duration(NoteValue.Quarter, 0), [new Note(pitch)], StemDirection.Auto),
            new Rest(new EventId(Guid.NewGuid()), new Fraction(1, 4), new Duration(NoteValue.Half, 1))])]);

    [Fact]
    public void RepeatInfoRejectsInvalidPassCountsEndingsAndNavigationMarks()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepeatInfo(EndRepeat: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepeatInfo(Endings: [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepeatInfo(Endings: [1, 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepeatInfo(Target: (RepeatTarget)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepeatInfo(Jump: (RepeatJump)99));
        Assert.Empty(new RepeatInfo().Endings);
    }

    private static StaffMeasure WithQuarterNotes(int count)
    {
        ImmutableArray<MusicEvent>.Builder events = ImmutableArray.CreateBuilder<MusicEvent>();
        for (int index = 0; index < count; index++)
        {
            events.Add(QuarterChord(index));
        }

        return new StaffMeasure([new Voice(1, events.ToImmutable())]);
    }

    private static Chord QuarterChord(int index) => new(
        new EventId(Guid.NewGuid()),
        new Fraction(index, 4),
        new Duration(NoteValue.Quarter, 0),
        [new Note(new Pitch(Step.C, 0, 4))],
        StemDirection.Auto);
}
