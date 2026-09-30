using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>Identifies a selected event or one note within a chord.</summary>
public readonly record struct SelectionItem(EventId EventId, int? NoteIndex = null);

/// <summary>Stores the musical selection associated with a score snapshot.</summary>
public sealed record Selection
{
    /// <summary>Creates a selection from its event and note targets.</summary>
    public Selection(ImmutableArray<SelectionItem> items)
    {
        if (items.IsDefault)
        {
            throw new ArgumentException("Selection items must be initialized.", nameof(items));
        }

        Items = items;
    }

    /// <summary>Gets the selected musical targets.</summary>
    public ImmutableArray<SelectionItem> Items { get; }

    /// <summary>Gets an empty selection.</summary>
    public static Selection Empty => new([]);
}
