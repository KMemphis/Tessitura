namespace Tessitura.Core;

/// <summary>Names the natural letter of a written pitch.</summary>
public enum Step
{
    /// <summary>C.</summary>
    C,
    /// <summary>D.</summary>
    D,
    /// <summary>E.</summary>
    E,
    /// <summary>F.</summary>
    F,
    /// <summary>G.</summary>
    G,
    /// <summary>A.</summary>
    A,
    /// <summary>B.</summary>
    B,
}

/// <summary>Represents a signed interval in diatonic steps and semitones.</summary>
public readonly record struct Interval
{
    /// <summary>Creates a signed interval.</summary>
    /// <param name="diatonicSteps">The signed number of letter-name steps.</param>
    /// <param name="semitones">The signed number of chromatic semitones.</param>
    public Interval(int diatonicSteps, int semitones)
    {
        DiatonicSteps = diatonicSteps;
        Semitones = semitones;
    }

    /// <summary>Gets the signed number of letter-name steps.</summary>
    public int DiatonicSteps { get; }

    /// <summary>Gets the signed number of chromatic semitones.</summary>
    public int Semitones { get; }

    /// <summary>Returns the interval that reverses this transposition.</summary>
    public Interval Inverse() => new(checked(-DiatonicSteps), checked(-Semitones));
}

/// <summary>Represents a written pitch, preserving its spelling.</summary>
public readonly record struct Pitch
{
    /// <summary>Creates a written pitch.</summary>
    /// <param name="step">The natural letter name.</param>
    /// <param name="alter">The chromatic alteration in semitones.</param>
    /// <param name="octave">The scientific pitch notation octave.</param>
    public Pitch(Step step, int alter, int octave)
    {
        if ((int)step is < 0 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        Step = step;
        Alter = alter;
        Octave = octave;
    }

    /// <summary>Gets the natural letter name.</summary>
    public Step Step { get; }

    /// <summary>Gets the chromatic alteration in semitones.</summary>
    public int Alter { get; }

    /// <summary>Gets the scientific pitch notation octave.</summary>
    public int Octave { get; }

    /// <summary>Gets the derived MIDI note number, with C4 equal to 60.</summary>
    public int MidiNumber => checked((int)(12L * (Octave + 1L) + NaturalSemitones(Step) + Alter));

    /// <summary>Transposes while retaining the resulting letter name and alteration.</summary>
    /// <param name="interval">The signed diatonic and chromatic interval.</param>
    /// <returns>The transposed written pitch.</returns>
    public Pitch Transpose(Interval interval)
    {
        long diatonicIndex = (long)Octave * 7 + (int)Step + interval.DiatonicSteps;
        long targetOctave = Math.DivRem(diatonicIndex, 7, out long targetStep);
        if (targetStep < 0)
        {
            targetOctave--;
            targetStep += 7;
        }

        Step step = (Step)targetStep;
        long targetMidi = (long)MidiNumber + interval.Semitones;
        long naturalMidi = 12 * (targetOctave + 1) + NaturalSemitones(step);
        int alter = checked((int)(targetMidi - naturalMidi));
        return new Pitch(step, alter, checked((int)targetOctave));
    }

    private static int NaturalSemitones(Step step) => step switch
    {
        Step.C => 0,
        Step.D => 2,
        Step.E => 4,
        Step.F => 5,
        Step.G => 7,
        Step.A => 9,
        Step.B => 11,
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };
}
