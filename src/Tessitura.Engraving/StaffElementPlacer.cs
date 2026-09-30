using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Engraving.DisplayLists;

namespace Tessitura.Engraving;

/// <summary>Places basic notes, rests, accidentals, dots, and ledger lines in staff spaces.</summary>
public sealed class StaffElementPlacer
{
    private readonly Smufl.SmuflMetadata _metadata;
    private readonly Style _style;

    /// <summary>Creates a placer for one font and engraving style.</summary>
    /// <param name="metadata">The SMuFL font metrics.</param>
    /// <param name="style">The engraving measurements.</param>
    public StaffElementPlacer(Smufl.SmuflMetadata metadata, Style style)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _style = style ?? throw new ArgumentNullException(nameof(style));
    }

    /// <summary>Places five horizontal staff lines.</summary>
    /// <param name="id">The originating score element.</param>
    /// <param name="startX">The left endpoint.</param>
    /// <param name="endX">The right endpoint.</param>
    /// <param name="staffTop">The top staff-line position.</param>
    /// <returns>The five line primitives.</returns>
    public ImmutableArray<DrawingPrimitive> PlaceStaffLines(EventId id,
        double startX, double endX, double staffTop)
    {
        ImmutableArray<DrawingPrimitive>.Builder result = ImmutableArray.CreateBuilder<DrawingPrimitive>(5);
        ElementId elementId = new(id.Value);
        for (int index = 0; index < 5; index++)
        {
            double y = staffTop + index;
            result.Add(HorizontalLine(elementId, startX, endX, y, _style.StaffLineThickness));
        }

        return result.MoveToImmutable();
    }

    /// <summary>Places a single written note with its basic notation.</summary>
    /// <param name="id">The originating score event.</param>
    /// <param name="pitch">The written pitch.</param>
    /// <param name="duration">The notated duration.</param>
    /// <param name="accidental">The visible accidental decision.</param>
    /// <param name="x">The notehead glyph origin.</param>
    /// <param name="staffTop">The top staff-line position.</param>
    /// <param name="clef">The staff clef used to place the written pitch.</param>
    /// <param name="forcedStem">The stem direction imposed by the voice, or null to choose it from the pitch.</param>
    /// <returns>The notehead, stem, optional accidental and dots, and ledger lines.</returns>
    public ImmutableArray<DrawingPrimitive> PlaceNote(EventId id, Pitch pitch,
        Duration duration, AccidentalMark accidental, double x, double staffTop,
        Clef clef = Clef.Treble, StemDirection? forcedStem = null)
    {
        ElementId elementId = new(id.Value);
        int staffPosition = StaffPitchPosition.Get(pitch, clef);
        double y = StaffPitchPosition.GetY(pitch, clef, staffTop);
        string headName = duration.Value switch
        {
            NoteValue.Whole => "noteheadWhole",
            NoteValue.Half => "noteheadHalf",
            _ => "noteheadBlack",
        };
        Glyph head = MakeGlyph(elementId, headName, x, y);
        ImmutableArray<DrawingPrimitive>.Builder result = ImmutableArray.CreateBuilder<DrawingPrimitive>();

        if (accidental != AccidentalMark.None)
        {
            string accidentalName = AccidentalGlyphName(accidental);
            Smufl.SmuflBoundingBox accidentalBox = _metadata.GetBoundingBox(accidentalName);
            double accidentalX = head.Bounds.X - _style.MinimumAccidentalGap - accidentalBox.NorthEast.X;
            result.Add(MakeGlyph(elementId, accidentalName, accidentalX, y));
        }

        // SMuFL engravingDefaults.legerLineExtension and legerLineThickness:
        // extend every ledger line beyond the notehead on both sides.
        double ledgerLeft = head.Bounds.X - _style.LedgerLineExtension;
        double ledgerRight = head.Bounds.X + head.Bounds.Width + _style.LedgerLineExtension;
        if (staffPosition < 0)
        {
            for (int position = -2; position >= staffPosition; position -= 2)
            {
                double ledgerY = staffTop + 4 - position * 0.5;
                result.Add(HorizontalLine(elementId, ledgerLeft, ledgerRight,
                    ledgerY, _style.LedgerLineThickness));
            }
        }
        else if (staffPosition > 8)
        {
            for (int position = 10; position <= staffPosition; position += 2)
            {
                double ledgerY = staffTop + 4 - position * 0.5;
                result.Add(HorizontalLine(elementId, ledgerLeft, ledgerRight,
                    ledgerY, _style.LedgerLineThickness));
            }
        }

        result.Add(head);

        if (duration.Value != NoteValue.Whole)
        {
            StemDirection direction = forcedStem is StemDirection.Up or StemDirection.Down
                ? forcedStem.Value
                : BeamGrouper.ChooseStemDirection(staffPosition - 4);
            string anchorName = direction == StemDirection.Up ? "stemUpSE" : "stemDownNW";
            Smufl.SmuflPoint anchor = _metadata.GetAnchor(headName, anchorName);
            double stemX = x + anchor.X;
            double stemY = y - anchor.Y;
            double stemEndY = stemY + (direction == StemDirection.Up ? -_style.StemLength : _style.StemLength);
            // Behind Bars, Ground Rules > Stems (p. 14): stems on notes with
            // more than one ledger line extend to the middle staff line.
            if (staffPosition <= -4 || staffPosition >= 12)
            {
                stemEndY = staffTop + 2;
            }

            double top = Math.Min(stemY, stemEndY);
            double stemLength = Math.Abs(stemEndY - stemY);
            result.Add(new Line(elementId,
                new DisplayBox(stemX - _style.StemThickness / 2, top,
                    _style.StemThickness, stemLength),
                new DisplayPoint(stemX, stemY), new DisplayPoint(stemX, stemEndY),
                _style.StemThickness));
        }

        AddDots(result, elementId, head.Bounds.X + head.Bounds.Width,
            y - (staffPosition % 2 == 0 ? 0.5 : 0), duration.Dots);
        return result.ToImmutable();
    }

    /// <summary>Places a chord of several notes: heads with seconds displaced to opposite sides of one stem,
    /// stacked accidental columns, one column of dots and shared ledger lines.</summary>
    /// <param name="id">The originating score event.</param>
    /// <param name="notes">The written pitches with their visible accidentals.</param>
    /// <param name="duration">The notated duration.</param>
    /// <param name="x">The origin of the undisplaced noteheads.</param>
    /// <param name="staffTop">The top staff-line position.</param>
    /// <param name="clef">The staff clef.</param>
    /// <param name="forcedStem">The stem direction imposed by the voice, or null to choose it.</param>
    /// <returns>The chord's primitives.</returns>
    public ImmutableArray<DrawingPrimitive> PlaceChord(EventId id,
        IReadOnlyList<(Pitch Pitch, AccidentalMark Accidental)> notes, Duration duration, double x, double staffTop,
        Clef clef = Clef.Treble, StemDirection? forcedStem = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (notes.Count == 0)
        {
            throw new ArgumentException("A chord needs at least one note.", nameof(notes));
        }

        ElementId elementId = new(id.Value);
        string headName = duration.Value switch
        {
            NoteValue.Whole => "noteheadWhole",
            NoteValue.Half => "noteheadHalf",
            _ => "noteheadBlack",
        };
        // Ascending by staff position; equal positions keep their order.
        int[] order = [.. Enumerable.Range(0, notes.Count).OrderBy(i => StaffPitchPosition.Get(notes[i].Pitch, clef))];
        int[] positions = [.. order.Select(i => StaffPitchPosition.Get(notes[i].Pitch, clef))];
        int lowest = positions[0];
        int highest = positions[^1];
        // Behind Bars, Chords > Stem direction: follow the note farthest from the middle line.
        StemDirection direction = forcedStem is StemDirection.Up or StemDirection.Down
            ? forcedStem.Value
            : BeamGrouper.ChooseStemDirection(highest - 4 >= 4 - lowest ? Math.Max(0, highest - 4) : lowest - 4);
        Smufl.SmuflBoundingBox headBox = _metadata.GetBoundingBox(headName);
        double headWidth = headBox.NorthEast.X - headBox.SouthWest.X;
        bool hasStem = duration.Value != NoteValue.Whole;

        // Behind Bars, Chords > Seconds: with the stem up the upper head of a second goes right of the stem, with the
        // stem down the lower head goes left; three stacked notes alternate.
        double[] shift = new double[positions.Length];
        if (direction == StemDirection.Up)
        {
            for (int i = 1; i < positions.Length; i++)
            {
                if (positions[i] - positions[i - 1] == 1 && shift[i - 1] == 0)
                {
                    shift[i] = headWidth;
                }
            }
        }
        else
        {
            for (int i = positions.Length - 2; i >= 0; i--)
            {
                if (positions[i + 1] - positions[i] == 1 && shift[i + 1] == 0)
                {
                    shift[i] = -headWidth;
                }
            }
        }

        double leftMostHead = x + Math.Min(0, shift.Min());
        double rightMostHead = x + headWidth + Math.Max(0, shift.Max());
        ImmutableArray<DrawingPrimitive>.Builder result = ImmutableArray.CreateBuilder<DrawingPrimitive>();

        // Ledger lines span every head that needs them, displaced ones included.
        double ledgerLeft = leftMostHead - _style.LedgerLineExtension;
        double ledgerRight = rightMostHead + _style.LedgerLineExtension;
        for (int position = -2; position >= lowest; position -= 2)
        {
            result.Add(HorizontalLine(elementId, ledgerLeft, ledgerRight, staffTop + 4 - position * 0.5, _style.LedgerLineThickness));
        }

        for (int position = 10; position <= highest; position += 2)
        {
            result.Add(HorizontalLine(elementId, ledgerLeft, ledgerRight, staffTop + 4 - position * 0.5, _style.LedgerLineThickness));
        }

        Glyph[] heads = new Glyph[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            double y = staffTop + 4 - positions[i] * 0.5;
            heads[i] = MakeGlyph(elementId, headName, x + shift[i], y);
            result.Add(heads[i]);
        }

        // Behind Bars, Accidentals > Chords: stack from the top down in columns to the left, reusing a column when
        // the vertical distance to its last accidental is at least a seventh (six staff steps).
        List<List<(double Top, double Bottom)>> columns = [];
        double accidentalGap = _style.MinimumAccidentalGap;
        // Columns are as wide as the widest accidental so that mixed sharps and flats never touch.
        double columnWidth = 0;
        foreach ((Pitch _, AccidentalMark mark) in notes)
        {
            if (mark != AccidentalMark.None)
            {
                Smufl.SmuflBoundingBox widest = _metadata.GetBoundingBox(AccidentalGlyphName(mark));
                columnWidth = Math.Max(columnWidth, widest.NorthEast.X - widest.SouthWest.X);
            }
        }

        for (int i = positions.Length - 1; i >= 0; i--)
        {
            AccidentalMark mark = notes[order[i]].Accidental;
            if (mark == AccidentalMark.None)
            {
                continue;
            }

            string name = AccidentalGlyphName(mark);
            Smufl.SmuflBoundingBox box = _metadata.GetBoundingBox(name);
            double y = staffTop + 4 - positions[i] * 0.5;
            (double top, double bottom) = (y - box.NorthEast.Y - 0.1, y - box.SouthWest.Y + 0.1);
            int column = 0;
            while (column < columns.Count && columns[column].Exists(o => top < o.Bottom && o.Top < bottom))
            {
                column++;
            }

            if (column == columns.Count)
            {
                columns.Add([]);
            }

            columns[column].Add((top, bottom));
            double accidentalX = leftMostHead - accidentalGap - box.NorthEast.X - column * (columnWidth + accidentalGap);
            result.Add(MakeGlyph(elementId, name, accidentalX, y));
        }

        if (hasStem)
        {
            string anchorName = direction == StemDirection.Up ? "stemUpSE" : "stemDownNW";
            Smufl.SmuflPoint anchor = _metadata.GetAnchor(headName, anchorName);
            // The stem sits on the undisplaced column: right edge for up stems, left edge for down stems.
            double stemX = x + anchor.X;
            double startY = direction == StemDirection.Up
                ? staffTop + 4 - lowest * 0.5 - anchor.Y
                : staffTop + 4 - highest * 0.5 - anchor.Y;
            double endY = direction == StemDirection.Up
                ? staffTop + 4 - highest * 0.5 - _style.StemLength
                : staffTop + 4 - lowest * 0.5 + _style.StemLength;
            // Behind Bars, Ground Rules > Stems: reach the middle line when the far note lies beyond it.
            if (direction == StemDirection.Up && endY > staffTop + 2)
            {
                endY = staffTop + 2;
            }
            else if (direction == StemDirection.Down && endY < staffTop + 2)
            {
                endY = staffTop + 2;
            }

            result.Add(new Line(elementId,
                new DisplayBox(stemX - _style.StemThickness / 2, Math.Min(startY, endY), _style.StemThickness, Math.Abs(endY - startY)),
                new DisplayPoint(stemX, startY), new DisplayPoint(stemX, endY), _style.StemThickness));
        }

        // Behind Bars, Dots > Chords: one column right of the widest head; a dot on a line moves to the space above,
        // and two dots that would share a space are separated by moving the lower one down.
        if (duration.Dots > 0)
        {
            double lastDotY = double.NaN;
            for (int i = positions.Length - 1; i >= 0; i--)
            {
                double y = staffTop + 4 - positions[i] * 0.5 - (positions[i] % 2 == 0 ? 0.5 : 0);
                if (!double.IsNaN(lastDotY) && Math.Abs(y - lastDotY) < 0.5)
                {
                    y = lastDotY + 1;
                }

                lastDotY = y;
                AddDots(result, elementId, rightMostHead, y, duration.Dots);
            }
        }

        return result.ToImmutable();
    }

    /// <summary>Chooses the stem direction of a note or chord from its extreme staff positions.</summary>
    /// <param name="lowest">The lowest staff position (0 is the bottom line).</param>
    /// <param name="highest">The highest staff position.</param>
    /// <param name="forced">The direction imposed by the voice, or null.</param>
    /// <returns>Up or down.</returns>
    public static StemDirection ChooseStem(int lowest, int highest, StemDirection? forced = null) =>
        forced is StemDirection.Up or StemDirection.Down
            ? forced.Value
            : BeamGrouper.ChooseStemDirection(highest - 4 >= 4 - lowest ? Math.Max(0, highest - 4) : lowest - 4);

    /// <summary>Places articulations and ornaments of one note or chord.</summary>
    /// <param name="id">The originating score event.</param>
    /// <param name="kinds">The marks, in any order.</param>
    /// <param name="centerX">The horizontal centre of the noteheads.</param>
    /// <param name="staffTop">The top staff-line position.</param>
    /// <param name="lowest">The lowest staff position of the chord.</param>
    /// <param name="highest">The highest staff position of the chord.</param>
    /// <param name="stem">The stem direction; articulations go on the notehead side, opposite the stem.</param>
    /// <param name="hasStem">Whether the note has a stem; whole notes place marks above.</param>
    /// <returns>The mark glyphs.</returns>
    public ImmutableArray<DrawingPrimitive> PlaceArticulations(EventId id, IReadOnlyList<ArticulationKind> kinds,
        double centerX, double staffTop, int lowest, int highest, StemDirection stem, bool hasStem)
    {
        ImmutableArray<DrawingPrimitive>.Builder result = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        ElementId elementId = new(id.Value);
        bool below = hasStem && stem == StemDirection.Up;
        // Behind Bars, Articulation: staccato sits nearest the head, then tenuto, accent and marcato outward.
        // Ornaments and fermatas go above the staff, fermatas highest.
        List<ArticulationKind> near = [.. kinds.Where(k => k <= ArticulationKind.Marcato).OrderBy(Rank)];
        double y = below ? staffTop + 4 - lowest * 0.5 : staffTop + 4 - highest * 0.5;
        int direction = below ? 1 : -1;
        foreach (ArticulationKind kind in near)
        {
            y += direction * 1.0;
            double snapped = y;
            if (snapped >= staffTop && snapped <= staffTop + 4)
            {
                // Inside the staff a mark sits in a space, never on a line.
                snapped = staffTop + Math.Floor(snapped - staffTop) + 0.5;
                if (Math.Abs(snapped - (y - direction * 1.0)) < 0.5)
                {
                    snapped += direction;
                }
            }

            y = snapped;
            string name = $"artic{kind}{(below ? "Below" : "Above")}";
            result.Add(MakeCenteredGlyph(elementId, name, centerX, y));
        }

        double top = Math.Min(staffTop - 1.5, staffTop + 4 - highest * 0.5 - 1.5);
        if (!below && near.Count > 0)
        {
            top = Math.Min(top, y - 1.5);
        }

        foreach (ArticulationKind kind in kinds.Where(k => k > ArticulationKind.Marcato).OrderBy(k => k == ArticulationKind.Fermata ? 1 : 0))
        {
            string name = kind switch
            {
                ArticulationKind.Fermata => "fermataAbove",
                ArticulationKind.Trill => "ornamentTrill",
                ArticulationKind.Mordent => "ornamentMordent",
                _ => "ornamentTurn",
            };
            result.Add(MakeCenteredGlyph(elementId, name, centerX, top));
            top -= 1.6;
        }

        return result.ToImmutable();
    }

    private static int Rank(ArticulationKind kind) => kind switch
    {
        ArticulationKind.Staccato or ArticulationKind.Staccatissimo => 0,
        ArticulationKind.Tenuto => 1,
        ArticulationKind.Accent => 2,
        _ => 3,
    };

    private Glyph MakeCenteredGlyph(ElementId id, string name, double centerX, double y)
    {
        Smufl.SmuflBoundingBox box = _metadata.GetBoundingBox(name);
        double width = box.NorthEast.X - box.SouthWest.X;
        return MakeGlyph(id, name, centerX - width / 2 - box.SouthWest.X, y);
    }

    /// <summary>Places one rest glyph and its optional augmentation dots.</summary>
    /// <param name="id">The originating score event.</param>
    /// <param name="duration">The notated rest duration.</param>
    /// <param name="x">The rest glyph origin.</param>
    /// <param name="staffTop">The top staff-line position.</param>
    /// <param name="verticalOffset">The shift in staff spaces, negative upward, that keeps the rests of different voices apart.</param>
    /// <returns>The rest and optional dot primitives.</returns>
    public ImmutableArray<DrawingPrimitive> PlaceRest(EventId id, Duration duration,
        double x, double staffTop, double verticalOffset = 0)
    {
        ElementId elementId = new(id.Value);
        string name = duration.Value switch
        {
            NoteValue.Whole => "restWhole",
            NoteValue.Half => "restHalf",
            NoteValue.Quarter => "restQuarter",
            NoteValue.Eighth => "rest8th",
            NoteValue.Sixteenth => "rest16th",
            NoteValue.ThirtySecond => "rest32nd",
            NoteValue.SixtyFourth => "rest64th",
            NoteValue.HundredTwentyEighth => "rest128th",
            _ => throw new ArgumentOutOfRangeException(nameof(duration)),
        };
        // Behind Bars, Ground Rules > Rest symbols: whole and half rests sit
        // at different staff-line levels; the remaining rests use the centre.
        double y = staffTop + (duration.Value == NoteValue.Whole ? 1 : 2) + verticalOffset;
        Glyph rest = MakeGlyph(elementId, name, x, y);
        ImmutableArray<DrawingPrimitive>.Builder result = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        result.Add(rest);
        AddDots(result, elementId, rest.Bounds.X + rest.Bounds.Width,
            staffTop + 1.5 + verticalOffset, duration.Dots);
        return result.ToImmutable();
    }

    private void AddDots(ImmutableArray<DrawingPrimitive>.Builder result,
        ElementId id, double right, double y, int count)
    {
        if (count == 0)
        {
            return;
        }

        Smufl.SmuflBoundingBox box = _metadata.GetBoundingBox("augmentationDot");
        double width = box.NorthEast.X - box.SouthWest.X;
        double left = right + _style.MinimumRhythmicGap;
        for (int index = 0; index < count; index++)
        {
            double originX = left - box.SouthWest.X;
            result.Add(MakeGlyph(id, "augmentationDot", originX, y));
            left += width + _style.MinimumAccidentalGap;
        }
    }

    private Glyph MakeGlyph(ElementId id, string name, double x, double y)
    {
        Smufl.SmuflBoundingBox box = _metadata.GetBoundingBox(name);
        DisplayBox bounds = new(x + box.SouthWest.X, y - box.NorthEast.Y,
            box.NorthEast.X - box.SouthWest.X,
            box.NorthEast.Y - box.SouthWest.Y);
        return new Glyph(id, bounds, _metadata.GetGlyphCodepoint(name),
            new DisplayPoint(x, y), 4);
    }

    private static Line HorizontalLine(ElementId id, double startX, double endX,
        double y, double thickness) => new(id,
        new DisplayBox(startX, y - thickness / 2, endX - startX, thickness),
        new DisplayPoint(startX, y), new DisplayPoint(endX, y), thickness);

    private static string AccidentalGlyphName(AccidentalMark mark) => mark switch
    {
        AccidentalMark.DoubleFlat => "accidentalDoubleFlat",
        AccidentalMark.Flat => "accidentalFlat",
        AccidentalMark.Natural => "accidentalNatural",
        AccidentalMark.Sharp => "accidentalSharp",
        AccidentalMark.DoubleSharp => "accidentalDoubleSharp",
        _ => throw new ArgumentOutOfRangeException(nameof(mark)),
    };
}
