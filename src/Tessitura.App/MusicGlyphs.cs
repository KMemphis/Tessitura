using Tessitura.Core;

namespace Tessitura.App;

/// <summary>Provides SMuFL code points used as interface icons with the music font.</summary>
public static class MusicGlyphs
{
    /// <summary>Gets the G clef (SMuFL gClef, U+E050).</summary>
    public const string GClef = "";

    /// <summary>Gets the augmentation dot (SMuFL augmentationDot, U+E1E7).</summary>
    public const string AugmentationDot = "";

    /// <summary>Gets the quarter rest (SMuFL restQuarter, U+E4E5).</summary>
    public const string QuarterRest = "";

    /// <summary>Gets the tie used in text-based notes (SMuFL textTie, U+E1FD).</summary>
    public const string Tie = "";

    /// <summary>Gets the sharp (SMuFL accidentalSharp, U+E262).</summary>
    public const string Sharp = "";

    /// <summary>Gets the flat (SMuFL accidentalFlat, U+E260).</summary>
    public const string Flat = "";

    /// <summary>Gets common time (SMuFL timeSigCommon, U+E08A).</summary>
    public const string CommonTime = "";

    /// <summary>Gets forte (SMuFL dynamicForte, U+E522).</summary>
    public const string Forte = "";

    /// <summary>Gets the accent above (SMuFL articAccentAbove, U+E4A0).</summary>
    public const string Accent = "";

    /// <summary>Gets the crescendo hairpin (SMuFL dynamicCrescendoHairpin, U+E53E).</summary>
    public const string Hairpin = "";

    /// <summary>Gets the metronome-style note glyph for a note value (SMuFL individual notes range).</summary>
    /// <param name="value">The note value.</param>
    /// <returns>The glyph string.</returns>
    public static string ForNoteValue(NoteValue value) => value switch
    {
        NoteValue.Whole => "",
        NoteValue.Half => "",
        NoteValue.Quarter => "",
        NoteValue.Eighth => "",
        NoteValue.Sixteenth => "",
        NoteValue.ThirtySecond => "",
        NoteValue.SixtyFourth => "",
        _ => "",
    };

    /// <summary>Gets the registered duration action for a note value.</summary>
    /// <param name="value">The note value.</param>
    /// <returns>The action identifier, or an empty string when no action exists.</returns>
    public static string ActionFor(NoteValue value) => value switch
    {
        NoteValue.Whole => "score.duration.whole",
        NoteValue.Half => "score.duration.half",
        NoteValue.Quarter => "score.duration.quarter",
        NoteValue.Eighth => "score.duration.eighth",
        NoteValue.Sixteenth => "score.duration.sixteenth",
        _ => string.Empty,
    };
}
