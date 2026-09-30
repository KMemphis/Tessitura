using System.Collections.Immutable;

namespace Tessitura.Core;

/// <summary>Identifies a music event across immutable score snapshots.</summary>
/// <param name="Value">The stable identifier.</param>
public readonly record struct EventId(Guid Value);

/// <summary>Contains the score's basic textual credits.</summary>
/// <param name="Title">The score title.</param>
/// <param name="Composer">The composer's name.</param>
public sealed record ScoreMetadata(string Title, string Composer);

/// <summary>Describes one instrument and its staves.</summary>
/// <param name="Name">The instrument name.</param>
/// <param name="Staves">The instrument's staves.</param>
public sealed record Instrument(string Name, ImmutableArray<Staff> Staves);

/// <summary>Describes one staff.</summary>
/// <param name="Name">The staff name.</param>
public sealed record Staff(string Name);

/// <summary>Represents a conventional time signature.</summary>
public readonly record struct TimeSignature
{
    /// <summary>Creates a time signature.</summary>
    /// <param name="numerator">The number of denominator units per measure.</param>
    /// <param name="denominator">The note-value denominator.</param>
    public TimeSignature(int numerator, int denominator)
    {
        if (numerator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator));
        }

        if (denominator <= 0 || (denominator & (denominator - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator));
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>Gets the number of denominator units per measure.</summary>
    public int Numerator { get; }

    /// <summary>Gets the note-value denominator.</summary>
    public int Denominator { get; }

    /// <summary>Gets the measure length in whole-note units.</summary>
    public Fraction Length => new(Numerator, Denominator);
}

/// <summary>Describes one measure on the score's global timeline.</summary>
/// <param name="Number">The displayed measure number.</param>
/// <param name="TimeSignature">The measure's meter.</param>
public sealed record Measure(int Number, TimeSignature TimeSignature);

/// <summary>Locates one staff's content in one global measure.</summary>
/// <param name="StaffIndex">The zero-based staff index.</param>
/// <param name="MeasureIndex">The zero-based measure index.</param>
public readonly record struct StaffMeasureKey(int StaffIndex, int MeasureIndex);

/// <summary>Contains the voices on one staff in one measure.</summary>
/// <param name="Voices">The staff's voices.</param>
public sealed record StaffMeasure(ImmutableArray<Voice> Voices);

/// <summary>Contains one ordered voice's events.</summary>
/// <param name="Number">The voice number, from one to four.</param>
/// <param name="Events">The ordered music events.</param>
public sealed record Voice(int Number, ImmutableArray<MusicEvent> Events);

/// <summary>Identifies a sounding or silent event at an exact musical onset.</summary>
/// <param name="Id">The stable event identifier.</param>
/// <param name="Onset">The onset in whole-note units from the measure start.</param>
/// <param name="Duration">The notated duration.</param>
public abstract record MusicEvent(EventId Id, Fraction Onset, Duration Duration);

/// <summary>Specifies how the stem direction is chosen.</summary>
public enum StemDirection
{
    /// <summary>Choose the direction automatically.</summary>
    Auto,
    /// <summary>Draw an upward stem.</summary>
    Up,
    /// <summary>Draw a downward stem.</summary>
    Down,
}

/// <summary>Contains simultaneous notes at one onset.</summary>
/// <param name="Id">The stable event identifier.</param>
/// <param name="Onset">The exact musical onset.</param>
/// <param name="Duration">The notated duration.</param>
/// <param name="Notes">The written notes.</param>
/// <param name="Stem">The stem direction preference.</param>
public sealed record Chord(
    EventId Id,
    Fraction Onset,
    Duration Duration,
    ImmutableArray<Note> Notes,
    StemDirection Stem) : MusicEvent(Id, Onset, Duration);

/// <summary>Contains one silent musical event.</summary>
/// <param name="Id">The stable event identifier.</param>
/// <param name="Onset">The exact musical onset.</param>
/// <param name="Duration">The notated duration.</param>
public sealed record Rest(EventId Id, Fraction Onset, Duration Duration) : MusicEvent(Id, Onset, Duration);

/// <summary>Contains one written note.</summary>
/// <param name="Pitch">The written pitch.</param>
/// <param name="TiedToNext">Whether this note is tied to its following matching note.</param>
public sealed record Note(Pitch Pitch, bool TiedToNext = false);

/// <summary>Contains immutable score content and its global measure timeline.</summary>
/// <param name="Metadata">The textual credits.</param>
/// <param name="Instruments">The instruments and their staves.</param>
/// <param name="Measures">The global measures.</param>
/// <param name="Content">The content of each staff and measure.</param>
public sealed record Score(
    ScoreMetadata Metadata,
    ImmutableArray<Instrument> Instruments,
    ImmutableArray<Measure> Measures,
    ImmutableDictionary<StaffMeasureKey, StaffMeasure> Content);

/// <summary>Checks basic score invariants.</summary>
public static class ScoreValidator
{
    /// <summary>Checks that every voice fills its measure without gaps or overlaps.</summary>
    /// <param name="measure">The global measure definition.</param>
    /// <param name="content">The staff's content for that measure.</param>
    /// <returns>Whether all voices cover the measure exactly.</returns>
    public static bool IsMeasureValid(Measure measure, StaffMeasure content)
    {
        if (content.Voices.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (Voice voice in content.Voices)
        {
            if (voice.Number is < 1 or > 4 || voice.Events.IsDefaultOrEmpty)
            {
                return false;
            }

            Fraction expectedOnset = Fraction.Zero;
            foreach (MusicEvent musicEvent in voice.Events)
            {
                if (musicEvent.Onset != expectedOnset)
                {
                    return false;
                }

                expectedOnset += musicEvent.Duration.Length;
                if (expectedOnset > measure.TimeSignature.Length)
                {
                    return false;
                }
            }

            if (expectedOnset != measure.TimeSignature.Length)
            {
                return false;
            }
        }

        return true;
    }
}
