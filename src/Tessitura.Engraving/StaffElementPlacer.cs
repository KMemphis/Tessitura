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
    /// <returns>The notehead, stem, optional accidental and dots, and ledger lines.</returns>
    public ImmutableArray<DrawingPrimitive> PlaceNote(EventId id, Pitch pitch,
        Duration duration, AccidentalMark accidental, double x, double staffTop)
    {
        ElementId elementId = new(id.Value);
        int staffPosition = (pitch.Octave - 4) * 7 + (int)pitch.Step - (int)Step.E;
        double y = staffTop + 4 - staffPosition * 0.5;
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
            StemDirection direction = BeamGrouper.ChooseStemDirection(staffPosition - 4);
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

    /// <summary>Places one rest glyph and its optional augmentation dots.</summary>
    /// <param name="id">The originating score event.</param>
    /// <param name="duration">The notated rest duration.</param>
    /// <param name="x">The rest glyph origin.</param>
    /// <param name="staffTop">The top staff-line position.</param>
    /// <returns>The rest and optional dot primitives.</returns>
    public ImmutableArray<DrawingPrimitive> PlaceRest(EventId id, Duration duration,
        double x, double staffTop)
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
        double y = staffTop + (duration.Value == NoteValue.Whole ? 1 : 2);
        Glyph rest = MakeGlyph(elementId, name, x, y);
        ImmutableArray<DrawingPrimitive>.Builder result = ImmutableArray.CreateBuilder<DrawingPrimitive>();
        result.Add(rest);
        AddDots(result, elementId, rest.Bounds.X + rest.Bounds.Width,
            staffTop + 1.5, duration.Dots);
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
