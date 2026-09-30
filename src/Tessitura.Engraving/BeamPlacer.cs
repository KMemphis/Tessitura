using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Engraving.DisplayLists;
using DisplayLine = Tessitura.Engraving.DisplayLists.Line;

namespace Tessitura.Engraving;

/// <summary>Describes a beamable note using its SMuFL stem anchor and flag count.</summary>
/// <param name="Id">The originating score event.</param>
/// <param name="StemStart">The notehead's stem anchor in staff spaces.</param>
/// <param name="BeamCount">One for an eighth, two for a sixteenth, and so on.</param>
public readonly record struct BeamNote(EventId Id, DisplayPoint StemStart, int BeamCount);

/// <summary>Places stems and primary, secondary, and partial beams in staff spaces.</summary>
public sealed class BeamPlacer
{
    private readonly Style _style;

    /// <summary>Creates a beam placer with the selected engraving style.</summary>
    /// <param name="style">The source of SMuFL beam dimensions.</param>
    public BeamPlacer(Style style)
    {
        _style = style ?? throw new ArgumentNullException(nameof(style));
    }

    /// <summary>Places one beam group with a shared stem direction.</summary>
    /// <param name="notes">At least two notes in ascending horizontal order.</param>
    /// <param name="direction">Up or down stem direction.</param>
    /// <returns>Stem lines followed by beam lines.</returns>
    public ImmutableArray<DrawingPrimitive> Place(ReadOnlySpan<BeamNote> notes,
        StemDirection direction)
    {
        if (notes.Length < 2 || direction is not (StemDirection.Up or StemDirection.Down))
        {
            throw new ArgumentOutOfRangeException(nameof(notes),
                "A beam group needs at least two notes and an explicit stem direction.");
        }

        int maximumLevel = 1;
        for (int index = 0; index < notes.Length; index++)
        {
            if (notes[index].BeamCount is < 1 or > 4 ||
                index > 0 && notes[index].StemStart.X <= notes[index - 1].StemStart.X)
            {
                throw new ArgumentOutOfRangeException(nameof(notes),
                    "Beam notes need one to four levels and strictly increasing X positions.");
            }

            maximumLevel = Math.Max(maximumLevel, notes[index].BeamCount);
        }

        double firstX = notes[0].StemStart.X;
        double span = notes[^1].StemStart.X - firstX;
        // Behind Bars, Beams > Slant: use a restrained beam angle; the 0.5-space
        // ceiling is Tessitura's style baseline, not a value quoted from the book.
        double rise = Math.Clamp(notes[^1].StemStart.Y - notes[0].StemStart.Y, -0.5, 0.5);
        double intercept = direction == StemDirection.Up ? double.PositiveInfinity :
            double.NegativeInfinity;
        for (int index = 0; index < notes.Length; index++)
        {
            double fraction = (notes[index].StemStart.X - firstX) / span;
            double limit = notes[index].StemStart.Y +
                (direction == StemDirection.Up ? -_style.StemLength : _style.StemLength) -
                fraction * rise;
            intercept = direction == StemDirection.Up ? Math.Min(intercept, limit) :
                Math.Max(intercept, limit);
        }

        ImmutableArray<DrawingPrimitive>.Builder result =
            ImmutableArray.CreateBuilder<DrawingPrimitive>();
        for (int index = 0; index < notes.Length; index++)
        {
            BeamNote note = notes[index];
            double fraction = (note.StemStart.X - firstX) / span;
            DisplayPoint end = new(note.StemStart.X, intercept + fraction * rise);
            result.Add(MakeLine(new ElementId(note.Id.Value), note.StemStart, end,
                _style.StemThickness));
        }

        ElementId beamId = new(notes[0].Id.Value);
        // SMuFL engravingDefaults.beamThickness and beamSpacing control the
        // beam strokes and the clear gap between adjacent levels.
        AddBeam(result, beamId, firstX, notes[^1].StemStart.X, 0,
            firstX, span, intercept, rise, direction);
        for (int level = 2; level <= maximumLevel; level++)
        {
            int start = -1;
            for (int index = 0; index <= notes.Length; index++)
            {
                bool participates = index < notes.Length && notes[index].BeamCount >= level;
                if (participates && start < 0)
                {
                    start = index;
                }
                else if (!participates && start >= 0)
                {
                    int end = index - 1;
                    double left = notes[start].StemStart.X;
                    double right = notes[end].StemStart.X;
                    if (start == end)
                    {
                        // Behind Bars, Beams > Partial beams: a lone extra level
                        // receives a short hook directed toward its neighbour.
                        if (start == notes.Length - 1)
                        {
                            left -= Math.Min(1.5, (left - notes[start - 1].StemStart.X) / 2);
                        }
                        else
                        {
                            right += Math.Min(1.5, (notes[start + 1].StemStart.X - right) / 2);
                        }
                    }

                    AddBeam(result, beamId, left, right, level - 1,
                        firstX, span, intercept, rise, direction);
                    start = -1;
                }
            }
        }

        return result.ToImmutable();
    }

    private void AddBeam(ImmutableArray<DrawingPrimitive>.Builder result, ElementId id,
        double left, double right, int level, double firstX, double span,
        double intercept, double rise, StemDirection direction)
    {
        double offset = level * (_style.BeamThickness + _style.BeamSpacing) *
            (direction == StemDirection.Up ? 1 : -1);
        DisplayPoint start = new(left, intercept + (left - firstX) / span * rise + offset);
        DisplayPoint end = new(right, intercept + (right - firstX) / span * rise + offset);
        result.Add(MakeLine(id, start, end, _style.BeamThickness));
    }

    private static DisplayLine MakeLine(ElementId id, DisplayPoint start,
        DisplayPoint end, double thickness)
    {
        double half = thickness / 2;
        DisplayBox bounds = new(Math.Min(start.X, end.X) - half,
            Math.Min(start.Y, end.Y) - half,
            Math.Abs(end.X - start.X) + thickness,
            Math.Abs(end.Y - start.Y) + thickness);
        return new DisplayLine(id, bounds, start, end, thickness);
    }
}
