using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Engraving;

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
