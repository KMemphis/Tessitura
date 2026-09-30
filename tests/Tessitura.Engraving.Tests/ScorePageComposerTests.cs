using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Engraving;
using Tessitura.Smufl;
using Xunit;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;

namespace Tessitura.Engraving.Tests;

public sealed class ScorePageComposerTests
{
    [Fact]
    public void ComposesTheEditedMeasureIntoAnA4PageWithStableEventIds()
    {
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        ScorePageComposer composer = new(metadata, style);
        EventId eventId = new(Guid.NewGuid());
        EventId restId = new(Guid.NewGuid());
        Score score = CreateScore(eventId, restId);
        IncrementalScoreLayouter layouter = new(metadata);
        ScoreLayoutResult layout = layouter.Layout(score, style, composer.GetAvailableWidth(score));

        ScorePageComposition composition = composer.Compose(score, layout, measureIndex: 0);

        Assert.Equal(0, composition.SystemIndex);
        Assert.Equal(new SystemLineMeasureRange(0, 1), composition.MeasureRange);
        Assert.Equal(595, composition.Page.Width * composition.StaffSpacePoints, precision: 6);
        Assert.Equal(842, composition.Page.Height * composition.StaffSpacePoints, precision: 6);
        Assert.True(composition.Page.Primitives.OfType<DisplayLine>()
            .Count(primitive => primitive.ElementId.Value == Guid.Empty) >= 5);
        double longestStaffLine = composition.Page.Primitives.OfType<DisplayLine>()
            .Max(static primitive => primitive.Bounds.Width) * composition.StaffSpacePoints;
        Assert.True(longestStaffLine > 500);
        Assert.Contains(composition.Page.Primitives.OfType<DisplayGlyph>(), glyph =>
            glyph.ElementId.Value == eventId.Value &&
            glyph.Codepoint == metadata.GetGlyphCodepoint("noteheadBlack"));
        Assert.Contains(composition.Page.Primitives.OfType<DisplayGlyph>(), glyph =>
            glyph.ElementId.Value == restId.Value &&
            glyph.Codepoint == metadata.GetGlyphCodepoint("restHalf"));
    }

    [Theory]
    [InlineData(4, 4, NoteValue.Whole, 0)]
    [InlineData(3, 4, NoteValue.Half, 1)]
    public void WholeBarRestUsesTheWholeRestGlyphAtTheMiddleOfTheMeasure(
        int numerator, int denominator, NoteValue restValue, int dots)
    {
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        ScorePageComposer composer = new(metadata, style);
        EventId restId = new(Guid.NewGuid());
        Rest rest = new(restId, Fraction.Zero, new Duration(restValue, dots));
        Score score = new(new ScoreMetadata("Empty", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, new TimeSignature(numerator, denominator))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [rest])])));
        ScoreLayoutResult layout = new IncrementalScoreLayouter(metadata).Layout(
            score, style, composer.GetAvailableWidth(score));

        ScorePageComposition composition = composer.Compose(score, layout, 0);
        DisplayGlyph restGlyph = Assert.Single(composition.Page.Primitives.OfType<DisplayGlyph>(),
            glyph => glyph.ElementId.Value == restId.Value);
        double[] barlines = composition.Page.Primitives.OfType<DisplayLine>()
            .Where(line => Math.Abs(line.Start.X - line.End.X) < 1e-8 &&
                Math.Abs(line.Start.Y - line.End.Y) >= 3.9)
            .Select(line => line.Start.X).Order().ToArray();

        Assert.Equal(metadata.GetGlyphCodepoint("restWhole"), restGlyph.Codepoint);
        Assert.Equal(2, barlines.Length);
        double restCenter = restGlyph.Bounds.X + restGlyph.Bounds.Width / 2;
        Assert.InRange(Math.Abs(restCenter - (barlines[0] + barlines[1]) / 2),
            0, style.MinimumRhythmicGap);
    }

    [Fact]
    public void UsesStaffClefAndKeySignatureForHeaderAndAccidentalPlacement()
    {
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        ScorePageComposer composer = new(metadata, style);
        EventId sharpNoteId = new(Guid.NewGuid());
        EventId naturalNoteId = new(Guid.NewGuid());
        Chord sharpNote = new(sharpNoteId, Fraction.Zero,
            new Duration(NoteValue.Quarter, 0),
            [new Note(new Pitch(Step.F, 1, 3))], StemDirection.Auto);
        Chord naturalNote = new(naturalNoteId, new Fraction(1, 4),
            new Duration(NoteValue.Quarter, 0),
            [new Note(new Pitch(Step.F, 0, 3))], StemDirection.Auto);
        Rest rest = new(new EventId(Guid.NewGuid()), new Fraction(1, 2),
            new Duration(NoteValue.Half, 0));
        Score score = new(new ScoreMetadata("Bass", ""),
            [new Instrument("Bass", [new Staff("Bass", Clef.Bass)])],
            [new Measure(1, new TimeSignature(4, 4), new KeySignature(2))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                new StaffMeasureKey(0, 0), new StaffMeasure([
                    new Voice(1, [sharpNote, naturalNote, rest])])));
        IncrementalScoreLayouter layouter = new(metadata);
        ScoreLayoutResult layout = layouter.Layout(score, style, composer.GetAvailableWidth(score));

        ScorePageComposition composition = composer.Compose(score, layout, measureIndex: 0);
        DisplayGlyph[] glyphs = composition.Page.Primitives.OfType<DisplayGlyph>().ToArray();
        double staffTop = composition.Page.Primitives.OfType<DisplayLine>()
            .First(line => line.ElementId.Value == Guid.Empty).Start.Y;

        Assert.Contains(glyphs, glyph => glyph.Codepoint == metadata.GetGlyphCodepoint("fClef"));
        Assert.Equal(2, glyphs.Count(glyph => glyph.ElementId.Value == Guid.Empty &&
            glyph.Codepoint == metadata.GetGlyphCodepoint("accidentalSharp")));
        Assert.DoesNotContain(glyphs, glyph => glyph.ElementId.Value == sharpNoteId.Value &&
            glyph.Codepoint == metadata.GetGlyphCodepoint("accidentalSharp"));
        Assert.Contains(glyphs, glyph => glyph.ElementId.Value == naturalNoteId.Value &&
            glyph.Codepoint == metadata.GetGlyphCodepoint("accidentalNatural"));
        DisplayGlyph bassNotehead = Assert.Single(glyphs, glyph =>
            glyph.ElementId.Value == sharpNoteId.Value &&
            glyph.Codepoint == metadata.GetGlyphCodepoint("noteheadBlack"));
        Assert.Equal(staffTop + 1, bassNotehead.Origin.Y, precision: 6);
    }

    [Fact]
    public void StaffSpaceShrinksToKeepThirtyStavesInsideTheA4Page()
    {
        SmuflMetadata metadata = LoadMetadata();
        ScorePageComposer composer = new(metadata, Style.CreateDefault(metadata));
        ImmutableArray<Staff>.Builder staves = ImmutableArray.CreateBuilder<Staff>(30);
        for (int index = 0; index < 30; index++)
        {
            staves.Add(new Staff($"Staff {index + 1}"));
        }

        Score score = new(new ScoreMetadata("Reference", ""),
            [new Instrument("Orchestra", staves.MoveToImmutable())],
            [new Measure(1, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty);

        double staffSpace = composer.GetStaffSpacePoints(score);

        Assert.InRange(staffSpace, 1, 5);
        Assert.True(composer.GetAvailableWidth(score) > 100);
    }

    [Fact]
    public void ContinuousCompositionKeepsEverySystemOnOneTallPage()
    {
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        ScorePageComposer composer = new(metadata, style);
        const int measureCount = 12;
        ImmutableArray<Measure>.Builder measures = ImmutableArray.CreateBuilder<Measure>(measureCount);
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        for (int measureIndex = 0; measureIndex < measureCount; measureIndex++)
        {
            measures.Add(new Measure(measureIndex + 1, new TimeSignature(4, 4)));
            Rest rest = new(new EventId(Guid.NewGuid()), Fraction.Zero,
                new Duration(NoteValue.Whole, 0));
            content.Add(new StaffMeasureKey(0, measureIndex),
                new StaffMeasure([new Voice(1, [rest])]));
        }

        Score score = new(new ScoreMetadata("Galley", ""),
            [new Instrument("Flute", [new Staff("Flute")])], measures.ToImmutable(),
            content.ToImmutable());
        IncrementalScoreLayouter layouter = new(metadata);
        ScoreLayoutResult measured = layouter.Layout(score, style, composer.GetAvailableWidth(score));
        ImmutableArray<SystemLine>.Builder systems = ImmutableArray.CreateBuilder<SystemLine>(measureCount);
        for (int measureIndex = 0; measureIndex < measureCount; measureIndex++)
        {
            double width = measured.MeasureWidths[measureIndex].IdealWidth;
            systems.Add(new SystemLine(new SystemLineMeasureRange(measureIndex, 1), width, width,
                [width], measureIndex == measureCount - 1));
        }

        ScoreLayoutResult layout = measured with { Systems = systems.MoveToImmutable() };

        ScorePageComposition composition = composer.ComposeContinuous(score, layout, measureIndex: 11);

        Assert.Equal(1, composition.Page.Number);
        Assert.Equal(layout.Systems.Length - 1, composition.SystemIndex);
        Assert.True(composition.Page.Height * composition.StaffSpacePoints > 842);
        Assert.True(composition.Page.Primitives.OfType<DisplayLine>()
            .Count(primitive => primitive.ElementId.Value == Guid.Empty) >= layout.Systems.Length * 5);
    }

    [Fact]
    public void SkylineMeasurementsAreReusedForUnchangedSystemsInANewScoreSnapshot()
    {
        SmuflMetadata metadata = LoadMetadata();
        Style style = Style.CreateDefault(metadata);
        ScorePageComposer composer = new(metadata, style);
        Chord first = new(new EventId(Guid.NewGuid()), Fraction.Zero,
            new Duration(NoteValue.Whole, 0), [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Chord second = new(new EventId(Guid.NewGuid()), Fraction.Zero,
            new Duration(NoteValue.Whole, 0), [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Score score = new(new ScoreMetadata("Cache", ""),
            [new Instrument("Piano", [new Staff("Treble")])],
            [new Measure(1, new TimeSignature(4, 4)), new Measure(2, new TimeSignature(4, 4))],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty
                .Add(new StaffMeasureKey(0, 0), new StaffMeasure([new Voice(1, [first])]))
                .Add(new StaffMeasureKey(0, 1), new StaffMeasure([new Voice(1, [second])])),
            [new DynamicAttachment(first.Id, DynamicLevel.Mf), new DynamicAttachment(second.Id, DynamicLevel.Mp)]);
        IncrementalScoreLayouter layouter = new(metadata);
        ScoreLayoutResult measured = layouter.Layout(score, style, composer.GetAvailableWidth(score));
        ImmutableArray<SystemLine>.Builder systems = ImmutableArray.CreateBuilder<SystemLine>(2);
        for (int measureIndex = 0; measureIndex < 2; measureIndex++)
        {
            double width = measured.MeasureWidths[measureIndex].IdealWidth;
            systems.Add(new SystemLine(new SystemLineMeasureRange(measureIndex, 1), width, width,
                [width], measureIndex == 1));
        }

        ScoreLayoutResult layout = measured with { Systems = systems.MoveToImmutable() };

        _ = composer.Compose(score, layout, 0);
        Assert.Equal(2, composer.SkylineSystemMeasureCount);
        _ = composer.Compose(score, layout, 0);
        Assert.Equal(2, composer.SkylineSystemMeasureCount);

        Chord changedFirst = first with { Notes = [new Note(new Pitch(Step.D, 0, 4))] };
        Score changedScore = score with
        {
            Content = score.Content.SetItem(new StaffMeasureKey(0, 0),
                new StaffMeasure([new Voice(1, [changedFirst])])),
        };
        _ = composer.Compose(changedScore, layout, 0);

        Assert.Equal(3, composer.SkylineSystemMeasureCount);
    }

    private static Score CreateScore(EventId eventId, EventId restId)
    {
        Measure measure = new(1, new TimeSignature(4, 4));
        Chord chord = new(eventId, Fraction.Zero, new Duration(NoteValue.Quarter, 0),
            [new Note(new Pitch(Step.C, 0, 4))], StemDirection.Auto);
        Rest rest = new(restId, new Fraction(1, 4),
            new Duration(NoteValue.Half, 1));
        StaffMeasure staffMeasure = new([new Voice(1, [chord, rest])]);
        return new Score(new ScoreMetadata("Test", ""),
            [new Instrument("Piano", [new Staff("Treble")])], [measure],
            ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Empty.Add(
                new StaffMeasureKey(0, 0), staffMeasure));
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
