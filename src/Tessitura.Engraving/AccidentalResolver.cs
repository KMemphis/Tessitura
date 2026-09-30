using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Engraving;

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

/// <summary>Describes one written pitch in engraving order.</summary>
/// <param name="MeasureIndex">The zero-based measure index.</param>
/// <param name="Pitch">The pitch as written, including its alteration.</param>
/// <param name="TiedFromPrevious">Whether this note continues a tie from an earlier note.</param>
/// <param name="KeySignature">The key signature effective at this note.</param>
public readonly record struct AccidentalInput(int MeasureIndex, Pitch Pitch,
    bool TiedFromPrevious, KeySignature KeySignature);

/// <summary>Names the accidental glyph required before a note.</summary>
public enum AccidentalMark
{
    /// <summary>No accidental is needed.</summary>
    None,
    /// <summary>A double-flat is needed.</summary>
    DoubleFlat,
    /// <summary>A flat is needed.</summary>
    Flat,
    /// <summary>A natural is needed.</summary>
    Natural,
    /// <summary>A sharp is needed.</summary>
    Sharp,
    /// <summary>A double-sharp is needed.</summary>
    DoubleSharp,
}

/// <summary>Resolves visible accidentals from written pitches and measure context.</summary>
public sealed class AccidentalResolver
{
    /// <summary>Resolves notes in score order without changing the score model.</summary>
    /// <param name="notes">Written pitches ordered by measure and onset.</param>
    /// <returns>One visible accidental decision per input note.</returns>
    public ImmutableArray<AccidentalMark> Resolve(ReadOnlySpan<AccidentalInput> notes)
    {
        ImmutableArray<AccidentalMark>.Builder result = ImmutableArray.CreateBuilder<AccidentalMark>(notes.Length);
        Dictionary<(Step Step, int Octave), int> measureState = new();
        int currentMeasure = -1;
        KeySignature currentKey = default;

        foreach (AccidentalInput note in notes)
        {
            if (note.MeasureIndex < 0 || note.MeasureIndex < currentMeasure)
            {
                throw new ArgumentOutOfRangeException(nameof(notes), "Notes must be ordered by measure.");
            }

            if (note.Pitch.Alter is < -2 or > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(notes), "Only standard accidentals are supported.");
            }

            // Behind Bars, Accidentals and Key Signatures > Using accidentals:
            // the measure and key-signature boundary resets the accidental context.
            if (note.MeasureIndex != currentMeasure || note.KeySignature != currentKey)
            {
                measureState.Clear();
                currentMeasure = note.MeasureIndex;
                currentKey = note.KeySignature;
            }

            (Step Step, int Octave) position = (note.Pitch.Step, note.Pitch.Octave);
            int currentAlter = measureState.TryGetValue(position, out int previous)
                ? previous : currentKey.GetAlter(note.Pitch.Step);

            // Behind Bars, Chords – Dotted notes – Ties > Ties:
            // a continuation keeps its sounding alteration without a repeated glyph.
            AccidentalMark mark = !note.TiedFromPrevious && note.Pitch.Alter != currentAlter
                ? MarkFor(note.Pitch.Alter) : AccidentalMark.None;
            measureState[position] = note.Pitch.Alter;
            result.Add(mark);
        }

        return result.MoveToImmutable();
    }

    private static AccidentalMark MarkFor(int alter) => alter switch
    {
        -2 => AccidentalMark.DoubleFlat,
        -1 => AccidentalMark.Flat,
        0 => AccidentalMark.Natural,
        1 => AccidentalMark.Sharp,
        2 => AccidentalMark.DoubleSharp,
        _ => throw new ArgumentOutOfRangeException(nameof(alter)),
    };
}
