using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Smufl;
using Xunit;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;
using DisplayText = Tessitura.Engraving.DisplayLists.Text;

namespace Tessitura.Engraving.Tests;

public sealed class PartViewEngravingTests
{
    [Fact]
    public void ConsecutiveFullMeasureRestsAreGroupedAndComposedAsOnePartRest()
    {
        Score score = CreateRestScore(3);
        ScorePartView part = new("Oboe", [0]);
        Score projected = ScorePartProjector.Project(score, part);
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        ScorePageComposer composer = new(metadata, style);
        ScoreLayoutResult layout = new IncrementalScoreLayouter(metadata).Layout(
            projected, style, composer.GetAvailableWidth(projected));

        ImmutableArray<MultiMeasureRestGroup> groups = MultiMeasureRestGrouper.FindGroups(projected);
        ScorePageComposition composition = composer.ComposePart(score, part, layout, measureIndex: 0);

        MultiMeasureRestGroup group = Assert.Single(groups);
        Assert.Equal(0, group.StartMeasure);
        Assert.Equal(3, group.MeasureCount);
        Assert.Contains(composition.Page.Primitives.OfType<DisplayText>(), item => item.Content == "3");
        foreach (EventId restId in GetRestIds(score))
        {
            Assert.DoesNotContain(composition.Page.Primitives.OfType<DisplayGlyph>(),
                glyph => glyph.ElementId.Value == restId.Value);
        }

        Assert.Contains(composition.Page.Primitives.OfType<DisplayLine>(),
            line => line.StrokeWidth > style.StaffLineThickness && line.Bounds.Width > 1);
    }

    [Fact]
    public void AFullMeasureWithARepeatMarkDoesNotJoinTheMultiMeasureRestGroup()
    {
        Score score = CreateRestScore(3) with
        {
            Measures =
            [
                new Measure(1, new TimeSignature(4, 4)),
                new Measure(2, new TimeSignature(4, 4), Repeat: new RepeatInfo(StartRepeat: true)),
                new Measure(3, new TimeSignature(4, 4)),
            ],
        };

        Assert.Empty(MultiMeasureRestGrouper.FindGroups(score));
    }

    [Fact]
    public void BbClarinetComposesWrittenAndConcertPitchPartViewsWithTheirOwnKeySignatures()
    {
        EventId noteId = new(Guid.NewGuid());
        Score score = new(new ScoreMetadata("Clarinet", ""),
            [new Instrument("Clarinet in B-flat", [new Staff("Clarinet")], new Interval(-1, -2))],
            [new Measure(1, new TimeSignature(4, 4), new KeySignature(0))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(new StaffMeasureKey(0, 0),
                new StaffMeasure([new Voice(1,
                [
                    new Chord(noteId, Fraction.Zero, new Duration(NoteValue.Quarter, 0),
                        [new Note(new Pitch(Step.D, 0, 4))], StemDirection.Auto),
                    new Rest(new EventId(Guid.NewGuid()), new Fraction(1, 4), new Duration(NoteValue.Half, 1)),
                ])])));
        ScorePartView part = new("Clarinet in B-flat", [0]);
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        ScorePageComposer composer = new(metadata, style);
        Score writtenScore = ScorePartProjector.Project(score, part, PitchDisplayMode.Written);
        Score concertScore = ScorePartProjector.Project(score, part, PitchDisplayMode.Concert);
        ScoreLayoutResult writtenLayout = new IncrementalScoreLayouter(metadata).Layout(
            writtenScore, style, composer.GetAvailableWidth(writtenScore));
        ScoreLayoutResult concertLayout = new IncrementalScoreLayouter(metadata).Layout(
            concertScore, style, composer.GetAvailableWidth(concertScore));

        ScorePageComposition written = composer.ComposePart(score, part, writtenLayout, measureIndex: 0,
            pitchDisplayMode: PitchDisplayMode.Written);
        ScorePageComposition concert = composer.ComposePart(score, part, concertLayout, measureIndex: 0,
            pitchDisplayMode: PitchDisplayMode.Concert);
        Score fullConcertScore = ScorePitchView.Project(score, PitchDisplayMode.Concert);
        ScoreLayoutResult fullConcertLayout = new IncrementalScoreLayouter(metadata).Layout(
            fullConcertScore, style, composer.GetAvailableWidth(fullConcertScore));
        ScorePageComposition fullConcert = composer.Compose(score, fullConcertLayout,
            measureIndex: 0, pitchDisplayMode: PitchDisplayMode.Concert);
        DisplayGlyph writtenHead = Assert.Single(written.Page.Primitives.OfType<DisplayGlyph>(),
            glyph => glyph.ElementId.Value == noteId.Value && glyph.Codepoint == metadata.GetGlyphCodepoint("noteheadBlack"));
        DisplayGlyph concertHead = Assert.Single(concert.Page.Primitives.OfType<DisplayGlyph>(),
            glyph => glyph.ElementId.Value == noteId.Value && glyph.Codepoint == metadata.GetGlyphCodepoint("noteheadBlack"));
        DisplayGlyph fullConcertHead = Assert.Single(fullConcert.Page.Primitives.OfType<DisplayGlyph>(),
            glyph => glyph.ElementId.Value == noteId.Value && glyph.Codepoint == metadata.GetGlyphCodepoint("noteheadBlack"));

        Assert.Equal(2, written.Page.Primitives.OfType<DisplayGlyph>().Count(glyph =>
            glyph.ElementId.Value == Guid.Empty && glyph.Codepoint == metadata.GetGlyphCodepoint("accidentalSharp")));
        Assert.DoesNotContain(concert.Page.Primitives.OfType<DisplayGlyph>(), glyph =>
            glyph.ElementId.Value == Guid.Empty && glyph.Codepoint == metadata.GetGlyphCodepoint("accidentalSharp"));
        Assert.Equal(-0.5, writtenHead.Origin.Y - concertHead.Origin.Y, precision: 6);
        Assert.Equal(concertHead.Origin.Y, fullConcertHead.Origin.Y);
    }

    private static Score CreateRestScore(int measureCount)
    {
        ImmutableArray<Measure>.Builder measures = ImmutableArray.CreateBuilder<Measure>(measureCount);
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        for (int index = 0; index < measureCount; index++)
        {
            measures.Add(new Measure(index + 1, new TimeSignature(4, 4)));
            Rest rest = new(new EventId(Guid.NewGuid()), Fraction.Zero, new Duration(NoteValue.Whole, 0));
            content.Add(new StaffMeasureKey(0, index), new StaffMeasure([new Voice(1, [rest])]));
        }

        return new Score(new ScoreMetadata("Part", ""),
            [new Instrument("Oboe", [new Staff("Oboe")])], measures.MoveToImmutable(), content.ToImmutable());
    }

    private static IEnumerable<EventId> GetRestIds(Score score)
    {
        foreach (StaffMeasure staffMeasure in score.Content.Values)
        {
            foreach (Voice voice in staffMeasure.Voices)
            {
                foreach (MusicEvent musicEvent in voice.Events)
                {
                    yield return musicEvent.Id;
                }
            }
        }
    }

    private static SmuflMetadata LoadMetadata()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string metadataPath = Path.Combine(directory.FullName, "assets", "fonts", "Bravura.json");
            string glyphNamesPath = Path.Combine(directory.FullName, "assets", "fonts", "smufl_glyph_names.json");
            if (File.Exists(metadataPath) && File.Exists(glyphNamesPath))
            {
                return SmuflMetadata.Load(metadataPath, glyphNamesPath);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Bravura metadata was not found above the test directory.");
    }
}
