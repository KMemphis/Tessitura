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
