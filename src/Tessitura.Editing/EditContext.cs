namespace Tessitura.Editing;

/// <summary>Identifies the staff, measure and voice targeted by an edit command.</summary>
public readonly record struct EditContext
{
    /// <summary>Creates a context for an exact score location.</summary>
    public EditContext(int staffIndex, int measureIndex, int voiceNumber)
    {
        if (staffIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(staffIndex));
        }

        if (measureIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(measureIndex));
        }

        if (voiceNumber is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(voiceNumber));
        }

        StaffIndex = staffIndex;
        MeasureIndex = measureIndex;
        VoiceNumber = voiceNumber;
    }

    /// <summary>Gets the zero-based staff index.</summary>
    public int StaffIndex { get; }

    /// <summary>Gets the zero-based measure index.</summary>
    public int MeasureIndex { get; }

    /// <summary>Gets the voice number, from one to four.</summary>
    public int VoiceNumber { get; }
}
