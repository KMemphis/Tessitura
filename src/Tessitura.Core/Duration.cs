using System.Numerics;

namespace Tessitura.Core;

/// <summary>Names a note value by its denominator relative to a whole note.</summary>
public enum NoteValue
{
    /// <summary>Whole note.</summary>
    Whole = 1,
    /// <summary>Half note.</summary>
    Half = 2,
    /// <summary>Quarter note.</summary>
    Quarter = 4,
    /// <summary>Eighth note.</summary>
    Eighth = 8,
    /// <summary>Sixteenth note.</summary>
    Sixteenth = 16,
    /// <summary>Thirty-second note.</summary>
    ThirtySecond = 32,
    /// <summary>Sixty-fourth note.</summary>
    SixtyFourth = 64,
    /// <summary>Hundred-twenty-eighth note.</summary>
    HundredTwentyEighth = 128,
}

/// <summary>Represents a note value and its augmentation dots.</summary>
public readonly record struct Duration
{
    /// <summary>Creates a duration with zero or more augmentation dots.</summary>
    /// <param name="value">The undotted note value.</param>
    /// <param name="dots">The number of augmentation dots.</param>
    public Duration(NoteValue value, int dots)
    {
        if (value is not (NoteValue.Whole or NoteValue.Half or NoteValue.Quarter or
            NoteValue.Eighth or NoteValue.Sixteenth or NoteValue.ThirtySecond or
            NoteValue.SixtyFourth or NoteValue.HundredTwentyEighth))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        int maximumDots = 62 - BitOperations.Log2((uint)value);
        if (dots < 0 || dots > maximumDots)
        {
            throw new ArgumentOutOfRangeException(nameof(dots));
        }

        Value = value;
        Dots = dots;
    }

    /// <summary>Gets the undotted note value.</summary>
    public NoteValue Value { get; }

    /// <summary>Gets the number of augmentation dots.</summary>
    public int Dots { get; }

    /// <summary>Gets the exact length in whole-note units.</summary>
    public Fraction Length
    {
        get
        {
            Fraction addition = new(1, (int)Value);
            Fraction length = addition;
            for (int dot = 0; dot < Dots; dot++)
            {
                addition /= new Fraction(2, 1);
                length += addition;
            }

            return length;
        }
    }
}
