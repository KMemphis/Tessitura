using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Editing;
using Tessitura.Smufl;
using Xunit;
using DisplayGlyph = Tessitura.Engraving.DisplayLists.Glyph;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;
using DisplayPrimitive = Tessitura.Engraving.DisplayLists.DrawingPrimitive;
using DisplayText = Tessitura.Engraving.DisplayLists.Text;

namespace Tessitura.Engraving.Tests;

/// <summary>F4.8: a catalog of busy scores in which no element may overlap another.</summary>
public sealed class SkylineCatalogTests
{
    private static readonly SmuflMetadata Metadata = LoadMetadata();

    public static TheoryData<string> Names =>
    [
        "dynamics-and-hairpins", "tempo-chords-and-text-above", "articulation-stacks", "octave-and-pedal",
        "piano-with-everything", "quartet-marks", "chords-with-marks", "tuplets-with-marks",
    ];

    [Theory]
    [MemberData(nameof(Names))]
    public void NoElementOverlapsAnotherInTheCatalog(string name)
    {
        Score score = Build(name);

        List<string> overlaps = Overlaps(Compose(score));

        Assert.True(overlaps.Count == 0, string.Join("\n", overlaps));
    }

    [Fact]
    public void MarksPushStavesApartSoTheMarksOfOneStaffDoNotReachTheNext()
    {
        Score plain = Build("piano-with-everything") with { Attachments = default, Spanners = default };
        Score marked = Build("piano-with-everything");

        double plainGap = StaffGap(Compose(plain));
        double markedGap = StaffGap(Compose(marked));

        Assert.True(markedGap > plainGap, $"staves must move apart for the marks ({plainGap} vs {markedGap})");
    }

    private static double StaffGap(ImmutableArray<DisplayPrimitive> primitives)
    {
        double[] ys = [.. primitives.OfType<DisplayLine>().Where(l => l.ElementId.Value == Guid.Empty && l.Start.Y == l.End.Y).Select(l => l.Start.Y).Distinct().Order()];
        // Ten staff lines: the gap between the fifth and the sixth is the distance between the two staves.
        return ys[5] - ys[4];
    }

    private static List<string> Overlaps(ImmutableArray<DisplayPrimitive> primitives)
    {
        List<DisplayPrimitive> items = [.. primitives.Where(p => p.ElementId.Value != Guid.Empty && p is DisplayGlyph or DisplayText ||
            p is DisplayLine l && p.ElementId.Value != Guid.Empty && l.Start.Y != l.End.Y)];
        List<string> found = [];
        for (int i = 0; i < items.Count; i++)
        {
            for (int j = i + 1; j < items.Count; j++)
            {
                DisplayPrimitive a = items[i];
                DisplayPrimitive b = items[j];
                if (a.ElementId == b.ElementId)
                {
                    continue;
                }

                DisplayLists.DisplayBox x = a.Bounds;
                DisplayLists.DisplayBox y = b.Bounds;
                const double tolerance = 0.05;
                if (x.X < y.X + y.Width - tolerance && y.X < x.X + x.Width - tolerance &&
                    x.Y < y.Y + y.Height - tolerance && y.Y < x.Y + x.Height - tolerance)
                {
                    found.Add($"{Describe(a)} overlaps {Describe(b)}");
                }
            }
        }

        return found;
    }

    private static string Describe(DisplayPrimitive primitive) => primitive switch
    {
        DisplayGlyph g => $"glyph U+{g.Codepoint:X} #{g.ElementId.Value.ToString()[..4]} at ({g.Origin.X:F1},{g.Origin.Y:F1})",
        DisplayText t => $"text '{t.Content}' at ({t.Origin.X:F1},{t.Origin.Y:F1})",
        DisplayLine l => $"line ({l.Start.X:F1},{l.Start.Y:F1})-({l.End.X:F1},{l.End.Y:F1})",
        _ => primitive.GetType().Name,
    };

    private static ImmutableArray<DisplayPrimitive> Compose(Score score)
    {
        Style style = Style.CreateDefault(Metadata);
        ScorePageComposer composer = new(Metadata, style);
        return composer.Compose(score, new IncrementalScoreLayouter(Metadata).Layout(score, style, composer.GetAvailableWidth(score)), 0).Page.Primitives;
    }

    private static Score Build(string name)
    {
        // Every score has two measures of quarter notes per staff so marks share horizontal space.
        int staves = name is "piano-with-everything" or "chords-with-marks" ? 2 : name == "quartet-marks" ? 4 : 1;
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content = ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        List<Attachment> attachments = [];
        List<Spanner> spanners = [];
        List<Chord>[] notes = [.. Enumerable.Range(0, staves).Select(_ => new List<Chord>())];
        for (int staff = 0; staff < staves; staff++)
        {
            for (int m = 0; m < 2; m++)
            {
                List<MusicEvent> events = [];
                for (int beat = 0; beat < 4; beat++)
                {
                    Step step = (Step)((beat * 2 + staff + m) % 7);
                    int octave = staff == 0 ? 4 + (beat % 2) : 3;
                    Chord chord = name == "chords-with-marks"
                        ? new Chord(new EventId(Guid.NewGuid()), new Fraction(beat, 4), new Duration(NoteValue.Quarter, 0),
                            [new Note(new Pitch(step, beat == 1 ? 1 : 0, octave)), new Note(new Pitch((Step)(((int)step + 2) % 7), 0, octave)),
                                new Note(new Pitch((Step)(((int)step + 4) % 7), -1, octave))], StemDirection.Auto)
                        : new Chord(new EventId(Guid.NewGuid()), new Fraction(beat, 4), new Duration(NoteValue.Quarter, 0),
                            [new Note(new Pitch(step, beat == 2 ? -1 : 0, octave))], StemDirection.Auto);
                    events.Add(chord);
                    notes[staff].Add(chord);
                }

                content[new StaffMeasureKey(staff, m)] = new StaffMeasure([new Voice(1, [.. events])]);
            }
        }

        foreach (List<Chord> line in notes)
        {
            switch (name)
            {
                case "dynamics-and-hairpins":
                    attachments.Add(new DynamicAttachment(line[0].Id, DynamicLevel.P));
                    attachments.Add(new DynamicAttachment(line[3].Id, DynamicLevel.Ff));
                    attachments.Add(new DynamicAttachment(line[4].Id, DynamicLevel.Mf));
                    spanners.Add(new Spanner(line[0].Id, line[3].Id, SpannerKind.Crescendo));
                    spanners.Add(new Spanner(line[4].Id, line[7].Id, SpannerKind.Diminuendo));
                    break;
                case "tempo-chords-and-text-above":
                    attachments.Add(new TempoAttachment(line[0].Id, new Duration(NoteValue.Quarter, 0), 120));
                    attachments.Add(new ChordSymbolAttachment(line[0].Id, Step.C, 0, "maj7"));
                    attachments.Add(new ChordSymbolAttachment(line[1].Id, Step.F, 1, "m7", Step.A));
                    attachments.Add(new TextAttachment(line[2].Id, "dolce e legato"));
                    attachments.Add(new DynamicAttachment(line[2].Id, DynamicLevel.Mp));
                    break;
                case "articulation-stacks":
                    foreach (ArticulationKind kind in new[] { ArticulationKind.Staccato, ArticulationKind.Tenuto, ArticulationKind.Accent, ArticulationKind.Marcato })
                    {
                        attachments.Add(new ArticulationAttachment(line[0].Id, kind));
                        attachments.Add(new ArticulationAttachment(line[1].Id, kind));
                    }

                    attachments.Add(new ArticulationAttachment(line[2].Id, ArticulationKind.Fermata));
                    attachments.Add(new ArticulationAttachment(line[2].Id, ArticulationKind.Trill));
                    attachments.Add(new ArticulationAttachment(line[3].Id, ArticulationKind.Mordent));
                    attachments.Add(new ArticulationAttachment(line[3].Id, ArticulationKind.Staccatissimo));
                    break;
                case "octave-and-pedal":
                    spanners.Add(new Spanner(line[0].Id, line[3].Id, SpannerKind.OctaveUp));
                    spanners.Add(new Spanner(line[4].Id, line[7].Id, SpannerKind.OctaveDown));
                    spanners.Add(new Spanner(line[0].Id, line[6].Id, SpannerKind.Pedal));
                    attachments.Add(new DynamicAttachment(line[1].Id, DynamicLevel.F));
                    break;
                default:
                    attachments.Add(new TempoAttachment(line[0].Id, new Duration(NoteValue.Quarter, 1), 84));
                    attachments.Add(new DynamicAttachment(line[0].Id, DynamicLevel.Pp));
                    attachments.Add(new DynamicAttachment(line[4].Id, DynamicLevel.Ff));
                    attachments.Add(new ChordSymbolAttachment(line[2].Id, Step.G, 0, "7"));
                    attachments.Add(new TextAttachment(line[1].Id, "espressivo"));
                    attachments.Add(new ArticulationAttachment(line[1].Id, ArticulationKind.Staccato));
                    attachments.Add(new ArticulationAttachment(line[5].Id, ArticulationKind.Accent));
                    attachments.Add(new ArticulationAttachment(line[6].Id, ArticulationKind.Fermata));
                    spanners.Add(new Spanner(line[0].Id, line[4].Id, SpannerKind.Crescendo));
                    spanners.Add(new Spanner(line[0].Id, line[7].Id, SpannerKind.Slur));
                    spanners.Add(new Spanner(line[2].Id, line[6].Id, SpannerKind.OctaveUp));
                    break;
            }
        }

        ImmutableArray<Instrument> instruments = staves == 2
            ? [new Instrument("Piano", [new Staff("R"), new Staff("L", Clef.Bass)])]
            : [.. Enumerable.Range(0, staves).Select(i => new Instrument($"I{i}", [new Staff("S")]))];
        return new Score(new ScoreMetadata("Catalog", ""), instruments,
            [new Measure(1, new TimeSignature(4, 4)), new Measure(2, new TimeSignature(4, 4))], content.ToImmutable(), [.. attachments], [.. spanners]);
    }

    private static SmuflMetadata LoadMetadata()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Tessitura.sln")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        return SmuflMetadata.Load(Path.Combine(root, "assets", "fonts", "Bravura.json"), Path.Combine(root, "assets", "fonts", "smufl_glyph_names.json"));
    }
}
