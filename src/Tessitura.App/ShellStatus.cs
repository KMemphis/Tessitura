using System.Text;
using Tessitura.Core;
using Tessitura.Editing;

namespace Tessitura.App;

/// <summary>Formats the editor state shown in the status bar and inspector.</summary>
public static class ShellStatus
{
    /// <summary>Describes the active input mode.</summary>
    /// <param name="mode">The current input mode.</param>
    /// <returns>The localized mode label.</returns>
    public static string DescribeMode(ScoreInputMode mode) => mode switch
    {
        ScoreInputMode.NoteEntry => "Entrada de notas",
        _ => "Selección",
    };

    /// <summary>Describes a notated duration, including its dots.</summary>
    /// <param name="duration">The duration to describe.</param>
    /// <returns>The localized duration name.</returns>
    public static string DescribeDuration(Duration duration)
    {
        string name = DescribeNoteValue(duration.Value);
        return duration.Dots switch
        {
            0 => name,
            1 => name + " con puntillo",
            2 => name + " con doble puntillo",
            _ => $"{name} con {duration.Dots} puntillos",
        };
    }

    /// <summary>Describes an absolute score position as a measure number and beat.</summary>
    /// <param name="score">The score whose measure timeline is used.</param>
    /// <param name="position">The absolute position in whole-note units.</param>
    /// <returns>The measure and beat label.</returns>
    public static string DescribePosition(Score score, Fraction position)
    {
        ArgumentNullException.ThrowIfNull(score);
        (int number, string beat) = LocatePosition(score, position);
        return $"Compás {number} · Tiempo {beat}";
    }

    /// <summary>Describes the selected elements for the status bar.</summary>
    /// <param name="score">The score that contains the selection.</param>
    /// <param name="selection">The current selection.</param>
    /// <returns>A short localized description.</returns>
    public static string DescribeSelection(Score score, Selection selection)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(selection);
        int count = selection.Items.Length;
        if (count == 0)
        {
            return "Sin selección";
        }

        if (selection.Range is not null)
        {
            return count == 1 ? "Rango · 1 elemento" : $"Rango · {count} elementos";
        }

        if (count > 1)
        {
            return $"{count} elementos";
        }

        SelectionItem item = selection.Items[0];
        if (!TryFindEvent(score, item.EventId, out MusicEvent? musicEvent, out Fraction absolute))
        {
            return "1 elemento";
        }

        (int number, string beat) = LocatePosition(score, absolute);
        string where = $"compás {number}, tiempo {beat}";
        if (musicEvent is Rest)
        {
            return $"Silencio de {DescribeDuration(musicEvent.Duration).ToLowerInvariant()} · {where}";
        }

        Chord chord = (Chord)musicEvent;
        StringBuilder pitches = new();
        if (item.NoteIndex is int noteIndex && noteIndex >= 0 && noteIndex < chord.Notes.Length)
        {
            pitches.Append(DescribePitch(chord.Notes[noteIndex].Pitch));
        }
        else
        {
            foreach (Note note in chord.Notes)
            {
                if (pitches.Length > 0)
                {
                    pitches.Append(", ");
                }

                pitches.Append(DescribePitch(note.Pitch));
            }
        }

        return $"{DescribeDuration(chord.Duration)}, {pitches} · {where}";
    }

    /// <summary>Describes a written pitch with Spanish solfège names.</summary>
    /// <param name="pitch">The written pitch.</param>
    /// <returns>The pitch label, for example «Fa♯ 5».</returns>
    public static string DescribePitch(Pitch pitch)
    {
        string step = pitch.Step switch
        {
            Step.C => "Do",
            Step.D => "Re",
            Step.E => "Mi",
            Step.F => "Fa",
            Step.G => "Sol",
            Step.A => "La",
            _ => "Si",
        };
        string alter = pitch.Alter switch
        {
            2 => "𝄪",
            1 => "♯",
            -1 => "♭",
            -2 => "𝄫",
            _ => "",
        };
        return $"{step}{alter} {pitch.Octave}";
    }

    private static string DescribeNoteValue(NoteValue value) => value switch
    {
        NoteValue.Whole => "Redonda",
        NoteValue.Half => "Blanca",
        NoteValue.Quarter => "Negra",
        NoteValue.Eighth => "Corchea",
        NoteValue.Sixteenth => "Semicorchea",
        NoteValue.ThirtySecond => "Fusa",
        NoteValue.SixtyFourth => "Semifusa",
        _ => "Garrapatea",
    };

    private static (int MeasureNumber, string Beat) LocatePosition(Score score, Fraction position)
    {
        if (score.Measures.IsDefaultOrEmpty)
        {
            return (1, "1");
        }

        Fraction measureStart = Fraction.Zero;
        for (int index = 0; index < score.Measures.Length; index++)
        {
            Measure measure = score.Measures[index];
            Fraction measureEnd = measureStart + measure.TimeSignature.Length;
            if (position < measureEnd || index == score.Measures.Length - 1)
            {
                Fraction local = position - measureStart;
                if (local < Fraction.Zero)
                {
                    local = Fraction.Zero;
                }

                // Beats are counted in the time signature's denominator unit, so 6/8 counts eighths.
                Fraction beat = local * new Fraction(measure.TimeSignature.Denominator, 1) + Fraction.One;
                return (measure.Number, FormatMixed(beat));
            }

            measureStart = measureEnd;
        }

        return (score.Measures[^1].Number, "1");
    }

    private static string FormatMixed(Fraction value)
    {
        long whole = value.Num / value.Den;
        long remainder = value.Num % value.Den;
        return remainder == 0 ? whole.ToString() : $"{whole} + {remainder}/{value.Den}";
    }

    private static bool TryFindEvent(
        Score score,
        EventId eventId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MusicEvent? found,
        out Fraction absolute)
    {
        foreach ((StaffMeasureKey key, StaffMeasure staffMeasure) in score.Content)
        {
            foreach (Voice voice in staffMeasure.Voices)
            {
                foreach (MusicEvent musicEvent in voice.Events)
                {
                    if (musicEvent.Id != eventId)
                    {
                        continue;
                    }

                    Fraction measureStart = Fraction.Zero;
                    for (int index = 0; index < key.MeasureIndex && index < score.Measures.Length; index++)
                    {
                        measureStart += score.Measures[index].TimeSignature.Length;
                    }

                    found = musicEvent;
                    absolute = measureStart + musicEvent.Onset;
                    return true;
                }
            }
        }

        found = null;
        absolute = Fraction.Zero;
        return false;
    }
}
