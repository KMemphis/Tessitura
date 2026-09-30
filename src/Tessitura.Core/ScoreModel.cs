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
/// <param name="InitialClef">The clef used when the staff is first drawn.</param>
public sealed record Staff(string Name, Clef InitialClef = Clef.Treble);

/// <summary>Identifies a conventional pitched staff clef.</summary>
public enum Clef
{
    /// <summary>G clef on the second line from the bottom.</summary>
    Treble,
    /// <summary>F clef on the second line from the top.</summary>
    Bass,
    /// <summary>C clef on the middle line.</summary>
    Alto,
    /// <summary>C clef on the fourth line from the bottom.</summary>
    Tenor,
}

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

/// <summary>Represents a conventional key signature from seven flats to seven sharps.</summary>
public readonly record struct KeySignature
{
    /// <summary>Creates a key signature from its signed circle-of-fifths count.</summary>
    /// <param name="fifths">Negative for flats, positive for sharps.</param>
    public KeySignature(int fifths)
    {
        if (fifths is < -7 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(fifths));
        }

        Fifths = fifths;
    }

    /// <summary>Gets the signed number of flats or sharps.</summary>
    public int Fifths { get; }

    /// <summary>Gets the alteration supplied by this signature for a written step.</summary>
    /// <param name="step">The written letter name.</param>
    /// <returns>Negative one, zero, or positive one.</returns>
    public int GetAlter(Step step)
    {
        // Behind Bars, Accidentals and Key Signatures > Key Signatures:
        // sharps follow F C G D A E B; flats reverse that order.
        int sharpOrder = step switch
        {
            Step.F => 0,
            Step.C => 1,
            Step.G => 2,
            Step.D => 3,
            Step.A => 4,
            Step.E => 5,
            Step.B => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };
        if (Fifths > 0 && sharpOrder < Fifths)
        {
            return 1;
        }

        if (Fifths < 0 && 6 - sharpOrder < -Fifths)
        {
            return -1;
        }

        return 0;
    }
}

/// <summary>Describes one measure on the score's global timeline.</summary>
/// <param name="Number">The displayed measure number.</param>
/// <param name="TimeSignature">The measure's meter.</param>
/// <param name="KeySignature">The key signature in effect in this measure.</param>
public sealed record Measure(
    int Number,
    TimeSignature TimeSignature,
    KeySignature KeySignature = default);

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
public abstract record MusicEvent(EventId Id, Fraction Onset, Duration Duration)
{
    /// <summary>Gets the time the event occupies in the measure, in whole-note units.</summary>
    public virtual Fraction Length => Duration.Length;
}

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

/// <summary>
/// Contains events squeezed into the time of fewer (or more) of the same value, such as a triplet:
/// <c>Actual</c> members sound in the time of <c>Normal</c> members of the base value.
/// </summary>
/// <param name="Id">The stable event identifier.</param>
/// <param name="Onset">The exact musical onset of the group.</param>
/// <param name="Duration">The base note value of one group unit (an eighth for eighth-note triplets).</param>
/// <param name="Actual">How many units are played, such as 3.</param>
/// <param name="Normal">How many units the time equals, such as 2.</param>
/// <param name="Children">The members; their onsets count from the group start in sounding time, and their durations are notated values.</param>
public sealed record TupletGroup(
    EventId Id,
    Fraction Onset,
    Duration Duration,
    int Actual,
    int Normal,
    ImmutableArray<MusicEvent> Children) : MusicEvent(Id, Onset, Duration)
{
    /// <summary>Gets the time the whole group occupies: <c>Normal</c> units of the base value.</summary>
    public override Fraction Length => Duration.Length * new Fraction(Normal, 1);

    /// <summary>Gets the ratio that turns a member's notated length into its sounding length.</summary>
    public Fraction Ratio => new(Normal, Actual);

    /// <summary>Checks that the members exactly fill the group with no gap or overlap.</summary>
    /// <returns>Whether the ratio is valid and the members tile the group, also in nested groups.</returns>
    public bool IsConsistent()
    {
        if (Actual < 2 || Normal < 1 || Actual == Normal || Children.IsDefaultOrEmpty)
        {
            return false;
        }

        Fraction expected = Fraction.Zero;
        foreach (MusicEvent child in Children)
        {
            if (child.Onset != expected)
            {
                return false;
            }

            if (child is TupletGroup inner && !inner.IsConsistent())
            {
                return false;
            }

            expected += child.Length * Ratio;
        }

        return expected == Length;
    }
}

/// <summary>Contains one written note.</summary>
/// <param name="Pitch">The written pitch.</param>
/// <param name="TiedToNext">Whether this note is tied to its following matching note.</param>
public sealed record Note(Pitch Pitch, bool TiedToNext = false);

/// <summary>Contains immutable score content and its global measure timeline.</summary>
/// <param name="Metadata">The textual credits.</param>
/// <param name="Instruments">The instruments and their staves.</param>
/// <param name="Measures">The global measures.</param>
/// <param name="Content">The content of each staff and measure.</param>
/// <param name="Attachments">Dynamics, articulations and other marks anchored to events; default means none.</param>
/// <param name="Spanners">Slurs and lines that run from one event to another; default means none.</param>
public sealed record Score(
    ScoreMetadata Metadata,
    ImmutableArray<Instrument> Instruments,
    ImmutableArray<Measure> Measures,
    ImmutableDictionary<StaffMeasureKey, StaffMeasure> Content,
    ImmutableArray<Attachment> Attachments = default,
    ImmutableArray<Spanner> Spanners = default)
{
    /// <summary>Gets the spanners, empty when none were given.</summary>
    public ImmutableArray<Spanner> SpannerList => Spanners.IsDefault ? ImmutableArray<Spanner>.Empty : Spanners;

    /// <summary>Gets the attachments, empty when none were given.</summary>
    public ImmutableArray<Attachment> AttachmentList => Attachments.IsDefault ? ImmutableArray<Attachment>.Empty : Attachments;
}

/// <summary>Names the kinds of line that run from one event to another.</summary>
public enum SpannerKind
{
    /// <summary>A slur or phrase mark.</summary>
    Slur,
    /// <summary>A crescendo hairpin.</summary>
    Crescendo,
    /// <summary>A diminuendo hairpin.</summary>
    Diminuendo,
    /// <summary>An 8va line: sounds an octave higher than written.</summary>
    OctaveUp,
    /// <summary>An 8vb line: sounds an octave lower than written.</summary>
    OctaveDown,
    /// <summary>A pedal line.</summary>
    Pedal,
}

/// <summary>A mark drawn from one event to another, anchored by their stable identifiers so that it survives edits.</summary>
/// <param name="Start">The event where the mark begins.</param>
/// <param name="End">The event where the mark ends, in the same staff.</param>
/// <param name="Kind">The kind of mark.</param>
public sealed record Spanner(EventId Start, EventId End, SpannerKind Kind);

/// <summary>Names a dynamic level, from very soft to very loud.</summary>
public enum DynamicLevel
{
    /// <summary>Pianississimo.</summary>
    Ppp,
    /// <summary>Pianissimo.</summary>
    Pp,
    /// <summary>Piano.</summary>
    P,
    /// <summary>Mezzo piano.</summary>
    Mp,
    /// <summary>Mezzo forte.</summary>
    Mf,
    /// <summary>Forte.</summary>
    F,
    /// <summary>Fortissimo.</summary>
    Ff,
    /// <summary>Fortississimo.</summary>
    Fff,
}

/// <summary>Names an articulation or ornament attached to a note or chord.</summary>
public enum ArticulationKind
{
    /// <summary>Short and detached.</summary>
    Staccato,
    /// <summary>Very short and detached.</summary>
    Staccatissimo,
    /// <summary>Held for its full value.</summary>
    Tenuto,
    /// <summary>Emphasized.</summary>
    Accent,
    /// <summary>Strongly emphasized.</summary>
    Marcato,
    /// <summary>Held longer than written.</summary>
    Fermata,
    /// <summary>Rapid alternation with the note above.</summary>
    Trill,
    /// <summary>Quick alternation with the note above and back.</summary>
    Mordent,
    /// <summary>Upper neighbour, note, lower neighbour, note.</summary>
    Turn,
}

/// <summary>A mark anchored to one event by its stable identifier.</summary>
/// <param name="Target">The event that carries the mark.</param>
public abstract record Attachment(EventId Target);

/// <summary>An articulation or ornament on an event.</summary>
/// <param name="Target">The articulated event.</param>
/// <param name="Kind">The articulation.</param>
public sealed record ArticulationAttachment(EventId Target, ArticulationKind Kind) : Attachment(Target);

/// <summary>A tempo that starts at an event: <c>Beat</c> = <c>Bpm</c> per minute.</summary>
/// <param name="Target">The event where the tempo begins.</param>
/// <param name="Beat">The beat unit, a quarter note for "q=120".</param>
/// <param name="Bpm">The number of beat units per minute.</param>
public sealed record TempoAttachment(EventId Target, Duration Beat, double Bpm) : Attachment(Target);

/// <summary>Free text above or below the staff, such as an expression mark.</summary>
/// <param name="Target">The event the text is attached to.</param>
/// <param name="Text">The text.</param>
public sealed record TextAttachment(EventId Target, string Text) : Attachment(Target);

/// <summary>A chord symbol such as Cmaj7 or F#m7/A written above the staff.</summary>
/// <param name="Target">The event the symbol is attached to.</param>
/// <param name="Root">The root letter.</param>
/// <param name="RootAlter">The root alteration in semitones.</param>
/// <param name="Quality">The suffix, such as "maj7", "m", "7" or "sus4"; empty for a major triad.</param>
/// <param name="Bass">The bass letter of a slash chord, if any.</param>
/// <param name="BassAlter">The bass alteration.</param>
public sealed record ChordSymbolAttachment(EventId Target, Step Root, int RootAlter, string Quality, Step? Bass = null, int BassAlter = 0)
    : Attachment(Target)
{
    /// <summary>Gets the symbol as written: root, alteration, quality and optional bass.</summary>
    public string Display => $"{Root}{Accidental(RootAlter)}{Quality}{(Bass is Step bass ? $"/{bass}{Accidental(BassAlter)}" : "")}";

    private static string Accidental(int alter) => alter switch { > 0 => "♯", < 0 => "♭", _ => "" };
}

/// <summary>A dynamic level that starts at an event.</summary>
/// <param name="Target">The event where the level begins.</param>
/// <param name="Level">The level.</param>
public sealed record DynamicAttachment(EventId Target, DynamicLevel Level) : Attachment(Target);

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

                if (musicEvent is TupletGroup group && !group.IsConsistent())
                {
                    return false;
                }

                expectedOnset += musicEvent.Length;
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
