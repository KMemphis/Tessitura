using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Engraving.DisplayLists;
using Tessitura.Smufl;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;

namespace Tessitura.Engraving.Tests;

internal sealed record ReferenceExample(string Id, string Title, string Description, Page Page);

internal abstract record ReferenceMusicEvent;

internal sealed record ReferenceNote(
    Pitch Pitch, Duration Duration, AccidentalMark Accidental, int BeamGroup) : ReferenceMusicEvent;

internal sealed record ReferenceRest(Duration Duration) : ReferenceMusicEvent;

internal sealed record ReferenceStaff(
    string Clef, int KeyFifths, TimeSignature Meter, ImmutableArray<ReferenceMusicEvent> Events);

internal static class ReferenceCatalog
{
    private const double PageWidth = 124;
    private const double PageHeight = 45;
    private const double MusicStartX = 37;
    private const double MusicEndX = 115;
    private static readonly EventId CatalogEventId =
        new(Guid.Parse("f116f116-f116-f116-f116-f116f116f116"));

    public static ImmutableArray<ReferenceExample> Create(SmuflMetadata metadata, Style style)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(style);
        ImmutableArray<ReferenceExample>.Builder examples =
            ImmutableArray.CreateBuilder<ReferenceExample>(20);
        ReferenceExample One(string id, string title, string description, string clef,
            int keyFifths, TimeSignature meter, params ReferenceMusicEvent[] events) =>
            Single(metadata, style, id, title, description, clef, keyFifths, meter, events);
        ReferenceExample Many(string id, string title, string description,
            params ReferenceStaff[] staves) =>
            Multi(metadata, style, id, title, description, staves);

        examples.Add(One("c-major-scale", "C major scale",
            "Ascending diatonic notes in treble clef.", "gClef", 0,
            new TimeSignature(4, 4), Scale(new Pitch(Step.C, 0, 4), 8)));
        examples.Add(One("g-major-one-sharp", "G major",
            "One sharp in the key signature; F remains unmarked in the melody.", "gClef", 1,
            new TimeSignature(4, 4), Scale(new Pitch(Step.G, 0, 4), 8)));
        examples.Add(One("d-major-two-sharps", "D major",
            "Two sharps in the key signature.", "gClef", 2,
            new TimeSignature(4, 4), Scale(new Pitch(Step.D, 0, 4), 8)));
        examples.Add(One("f-major-one-flat", "F major",
            "One flat in the key signature.", "gClef", -1,
            new TimeSignature(4, 4), Scale(new Pitch(Step.F, 0, 4), 8)));
        examples.Add(One("chromatic-sharps-and-flats", "Chromatic alterations",
            "Sharps, flats and natural notes retain their written spelling.", "gClef", 0,
            new TimeSignature(4, 4),
            Note(Step.C, 4), Note(Step.C, 4, 1, AccidentalMark.Sharp),
            Note(Step.D, 4), Note(Step.E, 4, -1, AccidentalMark.Flat),
            Note(Step.E, 4), Note(Step.F, 4), Note(Step.F, 4, 1, AccidentalMark.Sharp),
            Note(Step.G, 4)));
        examples.Add(One("whole-notes", "Whole notes",
            "Open noteheads without stems.", "gClef", 0,
            new TimeSignature(4, 4),
            Note(Step.C, 4, value: NoteValue.Whole), Note(Step.E, 4, value: NoteValue.Whole),
            Note(Step.G, 4, value: NoteValue.Whole), Note(Step.C, 5, value: NoteValue.Whole)));
        examples.Add(One("half-and-quarter-values", "Half and quarter notes",
            "Open and filled noteheads with their stems.", "gClef", 0,
            new TimeSignature(4, 4),
            Note(Step.C, 4, value: NoteValue.Half), Note(Step.D, 4),
            Note(Step.E, 4), Note(Step.G, 4, value: NoteValue.Half)));
        examples.Add(One("eighth-note-groups", "Eighth-note groups",
            "Two groups of four beamed eighth notes.", "gClef", 0,
            new TimeSignature(4, 4), BeamedNotes(8, NoteValue.Eighth, 4)));
        examples.Add(One("sixteenth-note-groups", "Sixteenth-note groups",
            "Four groups of four beamed sixteenth notes.", "gClef", 0,
            new TimeSignature(4, 4), BeamedNotes(16, NoteValue.Sixteenth, 4)));
        examples.Add(One("rests-in-all-values", "Rests",
            "Whole, half, quarter and flagged rests.", "gClef", 0,
            new TimeSignature(4, 4),
            Rest(NoteValue.Whole), Rest(NoteValue.Half), Rest(NoteValue.Quarter), Rest(NoteValue.Eighth),
            Rest(NoteValue.Sixteenth), Rest(NoteValue.ThirtySecond),
            Rest(NoteValue.SixtyFourth), Rest(NoteValue.HundredTwentyEighth)));
        examples.Add(One("dotted-values", "Dotted values",
            "Single and double augmentation dots.", "gClef", 0,
            new TimeSignature(4, 4),
            Note(Step.C, 4, value: NoteValue.Quarter, dots: 1),
            Note(Step.D, 4, value: NoteValue.Eighth),
            Note(Step.E, 4, value: NoteValue.Half, dots: 2),
            Note(Step.G, 4, value: NoteValue.Quarter)));
        examples.Add(One("simple-meter-two-four", "Simple metre: 2/4",
            "Two quarter-note beats.", "gClef", 0,
            new TimeSignature(2, 4), Note(Step.C, 4), Note(Step.E, 4)));
        examples.Add(One("simple-meter-three-four", "Simple metre: 3/4",
            "Three quarter-note beats.", "gClef", 0,
            new TimeSignature(3, 4), Note(Step.C, 4), Note(Step.E, 4), Note(Step.G, 4)));
        examples.Add(One("common-time-four-four", "Common time: 4/4",
            "Four quarter-note beats.", "gClef", 0,
            new TimeSignature(4, 4), Note(Step.C, 4), Note(Step.E, 4),
            Note(Step.G, 4), Note(Step.C, 5)));
        examples.Add(One("six-eight-compound-meter", "Compound metre: 6/8",
            "Two groups of three beamed eighth notes.", "gClef", 0,
            new TimeSignature(6, 8), BeamedNotes(6, NoteValue.Eighth, 3)));
        examples.Add(One("nine-eight-compound-meter", "Compound metre: 9/8",
            "Three groups of three beamed eighth notes.", "gClef", 0,
            new TimeSignature(9, 8), BeamedNotes(9, NoteValue.Eighth, 3)));
        examples.Add(Many("piano-grand-staff", "Piano grand staff",
            "Aligned treble and bass staves with a shared key and metre.",
            Staff("gClef", 0, new TimeSignature(4, 4),
                Note(Step.C, 5), Note(Step.G, 4), Note(Step.E, 4), Note(Step.C, 4)),
            Staff("fClef", 0, new TimeSignature(4, 4),
                Note(Step.C, 3), Note(Step.G, 2), Note(Step.E, 3), Note(Step.C, 3))));
        examples.Add(Many("string-quartet", "String quartet",
            "Four staves for two violins, viola and cello.",
            Staff("gClef", 0, new TimeSignature(4, 4), Note(Step.G, 4), Note(Step.A, 4),
                Note(Step.B, 4), Note(Step.C, 5)),
            Staff("gClef", 0, new TimeSignature(4, 4), Note(Step.D, 4), Note(Step.E, 4),
                Note(Step.F, 4), Note(Step.G, 4)),
            Staff("cClef", 0, new TimeSignature(4, 4), Note(Step.C, 4), Note(Step.D, 4),
                Note(Step.E, 4), Note(Step.F, 4)),
            Staff("fClef", 0, new TimeSignature(4, 4), Note(Step.C, 3), Note(Step.D, 3),
                Note(Step.E, 3), Note(Step.F, 3))));
        examples.Add(One("ledger-line-range", "Ledger-line range",
            "Notes above and below the five-line staff.", "gClef", 0,
            new TimeSignature(4, 4),
            Note(Step.C, 3), Note(Step.C, 4), Note(Step.C, 5), Note(Step.C, 6)));
        examples.Add(One("mixed-rhythm-and-accidentals", "Mixed rhythm and accidentals",
            "A beamed group, a rest, and a dotted note with a sharp.", "gClef", 0,
            new TimeSignature(4, 4),
            Note(Step.C, 4, value: NoteValue.Eighth, beamGroup: 0),
            Note(Step.D, 4, value: NoteValue.Eighth, beamGroup: 0),
            Rest(NoteValue.Quarter),
            Note(Step.F, 4, 1, AccidentalMark.Sharp, NoteValue.Quarter, 1),
            Note(Step.G, 4, value: NoteValue.Quarter, dots: 1)));

        return examples.MoveToImmutable();
    }

    private static ReferenceExample Single(SmuflMetadata metadata, Style style, string id,
        string title, string description, string clef, int keyFifths,
        TimeSignature meter, params ReferenceMusicEvent[] events) =>
        Multi(metadata, style, id, title, description,
            new ReferenceStaff(clef, keyFifths, meter, [.. events]));

    private static ReferenceExample Multi(SmuflMetadata metadata, Style style,
        string id, string title, string description, params ReferenceStaff[] staves)
    {
        ImmutableArray<DrawingPrimitive>.Builder primitives = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        ElementId titleId = new(CatalogEventId.Value);
        primitives.Add(new Text(titleId, new DisplayBox(6, 1, PageWidth - 12, 2.5),
            title, new DisplayPoint(6, 4), 2));
        primitives.Add(new Text(titleId, new DisplayBox(6, 5, PageWidth - 12, 1.5),
            description, new DisplayPoint(6, 7.2), 1));

        double firstTop = staves.Length switch { 1 => 17, 2 => 14.5, _ => 11.5 };
        double staffGap = staves.Length switch { 2 => 14, 4 => 8.5, _ => 0 };
        StaffElementPlacer staffPlacer = new(metadata, style);
        BeamPlacer beamPlacer = new(style);
        for (int staffIndex = 0; staffIndex < staves.Length; staffIndex++)
        {
            ReferenceStaff staff = staves[staffIndex];
            double staffTop = firstTop + staffIndex * staffGap;
            primitives.AddRange(staffPlacer.PlaceStaffLines(CatalogEventId, 5, 119, staffTop));
            AddHeader(primitives, metadata, style, staff, staffTop);
            AddBarline(primitives, style, MusicStartX - 2, staffTop);
            AddBarline(primitives, style, MusicEndX, staffTop);
            AddEvents(primitives, staffPlacer, beamPlacer, metadata, staff,
                staffTop, staffIndex);
        }

        return new ReferenceExample(id, title, description,
            new Page(1, PageWidth, PageHeight, primitives.ToImmutable()));
    }

    private static void AddHeader(ImmutableArray<DrawingPrimitive>.Builder primitives,
        SmuflMetadata metadata, Style style, ReferenceStaff staff, double staffTop)
    {
        double clefY = staff.Clef == "fClef" ? staffTop + 2.5 :
            staff.Clef == "cClef" ? staffTop + 3 : staffTop + 3.5;
        AddGlyph(primitives, metadata, staff.Clef, 6, clefY);

        double keyX = 15;
        string accidental = staff.KeyFifths > 0 ? "accidentalSharp" : "accidentalFlat";
        int count = Math.Abs(staff.KeyFifths);
        double[] sharpPositions = [0, 1.5, -0.5, 1, 2.5, 0.5, 2];
        double[] flatPositions = [2, 0.5, 2.5, 1, -0.5, 1.5, 0];
        double[] positions = staff.KeyFifths > 0 ? sharpPositions : flatPositions;
        double clefOffset = staff.Clef == "fClef" ? 1 : staff.Clef == "cClef" ? 0.5 : 0;
        for (int index = 0; index < count; index++)
        {
            AddGlyph(primitives, metadata, accidental, keyX,
                staffTop + positions[index] + clefOffset);
            SmuflBoundingBox box = metadata.GetBoundingBox(accidental);
            keyX += box.NorthEast.X - box.SouthWest.X + style.MinimumAccidentalGap;
        }

        double meterX = Math.Max(27, keyX + 1.5);
        AddDigitRow(primitives, metadata, staff.Meter.Numerator,
            meterX, staffTop + 1.5);
        AddDigitRow(primitives, metadata, staff.Meter.Denominator,
            meterX, staffTop + 3.6);
    }

    private static void AddEvents(ImmutableArray<DrawingPrimitive>.Builder primitives,
        StaffElementPlacer staffPlacer, BeamPlacer beamPlacer, SmuflMetadata metadata,
        ReferenceStaff staff, double staffTop, int staffIndex)
    {
        int eventCount = staff.Events.Length;
        double step = eventCount <= 1 ? 0 : (MusicEndX - MusicStartX - 5) / (eventCount - 1);
        List<BeamNote> beamNotes = [];
        int activeBeamGroup = -1;
        for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
        {
            ReferenceMusicEvent musicEvent = staff.Events[eventIndex];
            double x = MusicStartX + 2 + eventIndex * step;
            if (musicEvent is ReferenceNote note)
            {
                int beamCount = BeamCount(note.Duration.Value);
                if (beamCount > 0 && note.BeamGroup >= 0)
                {
                    if (activeBeamGroup != note.BeamGroup)
                    {
                        FlushBeam(primitives, beamPlacer, beamNotes);
                        activeBeamGroup = note.BeamGroup;
                    }

                    ImmutableArray<DrawingPrimitive> placed = staffPlacer.PlaceNote(
                        EventIdFor(staffIndex, eventIndex),
                        PitchForClef(note.Pitch, staff.Clef), note.Duration,
                        note.Accidental, x, staffTop);
                    for (int primitiveIndex = 0; primitiveIndex < placed.Length; primitiveIndex++)
                    {
                        if (placed[primitiveIndex] is Glyph glyph)
                        {
                            primitives.Add(glyph);
                        }
                    }

                    SmuflPoint anchor = metadata.GetAnchor("noteheadBlack", "stemUpSE");
                    Glyph notehead = FindNotehead(placed, metadata);
                    beamNotes.Add(new BeamNote(EventIdFor(staffIndex, eventIndex),
                        new DisplayPoint(notehead.Origin.X + anchor.X,
                            notehead.Origin.Y - anchor.Y), beamCount));
                }
                else
                {
                    FlushBeam(primitives, beamPlacer, beamNotes);
                    activeBeamGroup = -1;
                    primitives.AddRange(staffPlacer.PlaceNote(EventIdFor(staffIndex, eventIndex),
                        PitchForClef(note.Pitch, staff.Clef),
                        note.Duration, note.Accidental, x, staffTop));
                }
            }
            else if (musicEvent is ReferenceRest rest)
            {
                FlushBeam(primitives, beamPlacer, beamNotes);
                activeBeamGroup = -1;
                primitives.AddRange(staffPlacer.PlaceRest(EventIdFor(staffIndex, eventIndex),
                    rest.Duration, x, staffTop));
            }
        }

        FlushBeam(primitives, beamPlacer, beamNotes);
    }

    private static void FlushBeam(ImmutableArray<DrawingPrimitive>.Builder primitives,
        BeamPlacer beamPlacer, List<BeamNote> notes)
    {
        if (notes.Count >= 2)
        {
            primitives.AddRange(beamPlacer.Place(notes.ToArray(), StemDirection.Up));
        }
        else if (notes.Count == 1)
        {
            throw new InvalidOperationException("Reference beam groups must contain at least two notes.");
        }

        notes.Clear();
    }

    private static Glyph FindNotehead(ImmutableArray<DrawingPrimitive> primitives,
        SmuflMetadata metadata)
    {
        int codepoint = metadata.GetGlyphCodepoint("noteheadBlack");
        for (int index = 0; index < primitives.Length; index++)
        {
            if (primitives[index] is Glyph glyph && glyph.Codepoint == codepoint)
            {
                return glyph;
            }
        }

        throw new InvalidOperationException("A beamed note has no black notehead glyph.");
    }

    private static int BeamCount(NoteValue value) => value switch
    {
        NoteValue.Eighth => 1,
        NoteValue.Sixteenth => 2,
        NoteValue.ThirtySecond => 3,
        NoteValue.SixtyFourth => 4,
        _ => 0,
    };

    private static Pitch PitchForClef(Pitch pitch, string clef)
    {
        // StaffElementPlacer uses treble-clef staff positions; shift written pitches
        // so these catalog examples retain the same diatonic position in bass and alto clefs.
        int offset = clef switch
        {
            "fClef" => 12,
            "cClef" => 6,
            _ => 0,
        };
        int pitchIndex = pitch.Octave * 7 + (int)pitch.Step + offset;
        int octave = Math.DivRem(pitchIndex, 7, out int step);
        return new Pitch((Step)step, pitch.Alter, octave);
    }

    private static void AddDigitRow(ImmutableArray<DrawingPrimitive>.Builder primitives,
        SmuflMetadata metadata, int value, double x, double y)
    {
        string digits = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (char digit in digits)
        {
            string glyphName = $"timeSig{digit}";
            AddGlyph(primitives, metadata, glyphName, x, y);
            SmuflBoundingBox box = metadata.GetBoundingBox(glyphName);
            x += box.NorthEast.X - box.SouthWest.X;
        }
    }

    private static void AddBarline(ImmutableArray<DrawingPrimitive>.Builder primitives,
        Style style, double x, double staffTop)
    {
        ElementId id = new(CatalogEventId.Value);
        double thickness = style.StaffLineThickness;
        primitives.Add(new DisplayLine(id,
            new DisplayBox(x - thickness / 2, staffTop, thickness, 4),
            new DisplayPoint(x, staffTop), new DisplayPoint(x, staffTop + 4), thickness));
    }

    private static void AddGlyph(ImmutableArray<DrawingPrimitive>.Builder primitives,
        SmuflMetadata metadata, string name, double x, double y)
    {
        SmuflBoundingBox box = metadata.GetBoundingBox(name);
        primitives.Add(new Glyph(new ElementId(CatalogEventId.Value),
            new DisplayBox(x + box.SouthWest.X, y - box.NorthEast.Y,
                box.NorthEast.X - box.SouthWest.X, box.NorthEast.Y - box.SouthWest.Y),
            metadata.GetGlyphCodepoint(name), new DisplayPoint(x, y), 4));
    }

    private static ReferenceMusicEvent[] Scale(Pitch start, int count)
    {
        ReferenceMusicEvent[] result = new ReferenceMusicEvent[count];
        int startIndex = start.Octave * 7 + (int)start.Step;
        for (int index = 0; index < count; index++)
        {
            int pitchIndex = startIndex + index;
            int octave = Math.DivRem(pitchIndex, 7, out int step);
            result[index] = new ReferenceNote(new Pitch((Step)step, 0, octave),
                new Duration(NoteValue.Quarter, 0), AccidentalMark.None, -1);
        }

        return result;
    }

    private static ReferenceMusicEvent[] BeamedNotes(int count, NoteValue value, int groupSize)
    {
        ReferenceMusicEvent[] result = new ReferenceMusicEvent[count];
        Step[] melody = [Step.C, Step.D, Step.E, Step.F, Step.G, Step.A, Step.G, Step.F];
        for (int index = 0; index < count; index++)
        {
            int group = index / groupSize;
            result[index] = new ReferenceNote(
                new Pitch(melody[index % melody.Length], 0, 4),
                new Duration(value, 0), AccidentalMark.None, group);
        }

        return result;
    }

    private static ReferenceNote Note(Step step, int octave, int alter = 0,
        AccidentalMark accidental = AccidentalMark.None,
        NoteValue value = NoteValue.Quarter, int dots = 0, int beamGroup = -1) =>
        new(new Pitch(step, alter, octave), new Duration(value, dots), accidental, beamGroup);

    private static ReferenceRest Rest(NoteValue value, int dots = 0) =>
        new(new Duration(value, dots));

    private static ReferenceStaff Staff(string clef, int keyFifths, TimeSignature meter,
        params ReferenceMusicEvent[] events) =>
        new(clef, keyFifths, meter, [.. events]);

    private static EventId EventIdFor(int staff, int index) =>
        new(Guid.Parse($"f1160000-0000-0000-{staff + 1:0000}-{index + 1:000000000000}"));

}
