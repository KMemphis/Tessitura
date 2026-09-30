using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>Identifies a selected event or one note within a chord.</summary>
public readonly record struct SelectionItem(EventId EventId, int? NoteIndex = null);

/// <summary>Identifies a position on the musical selection plane.</summary>
public readonly record struct MusicalSelectionPoint
{
    /// <summary>Creates a point from a staff and exact score position.</summary>
    /// <param name="staffIndex">The zero-based staff index.</param>
    /// <param name="position">The absolute score position in whole-note units.</param>
    public MusicalSelectionPoint(int staffIndex, Fraction position)
    {
        if (staffIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(staffIndex));
        }

        StaffIndex = staffIndex;
        Position = position;
    }

    /// <summary>Gets the zero-based staff index.</summary>
    public int StaffIndex { get; }

    /// <summary>Gets the absolute position in whole-note units.</summary>
    public Fraction Position { get; }
}

/// <summary>Stores the fixed musical anchor and the moving endpoint of a range selection.</summary>
/// <param name="Anchor">The position where the range began.</param>
/// <param name="Target">The current position at the other end of the range.</param>
public sealed record SelectionRange(MusicalSelectionPoint Anchor, MusicalSelectionPoint Target);

/// <summary>Stores the musical selection associated with a score snapshot.</summary>
public sealed record Selection
{
    /// <summary>Creates a selection from its event and note targets.</summary>
    public Selection(ImmutableArray<SelectionItem> items, SelectionRange? range = null)
    {
        if (items.IsDefault)
        {
            throw new ArgumentException("Selection items must be initialized.", nameof(items));
        }

        Items = items;
        Range = range;
    }

    /// <summary>Gets the selected musical targets.</summary>
    public ImmutableArray<SelectionItem> Items { get; }

    /// <summary>Gets the musical range, when this is a range selection.</summary>
    public SelectionRange? Range { get; }

    /// <summary>Gets an empty selection.</summary>
    public static Selection Empty => new([]);
}
