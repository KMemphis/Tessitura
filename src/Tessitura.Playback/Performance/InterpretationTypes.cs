using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Playback.Performance;

/// <summary>Names a dynamic level, from very soft to very loud.</summary>
public enum Dynamic
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

/// <summary>Names an articulation that shapes how long and how hard a note sounds.</summary>
public enum Articulation
{
    /// <summary>Short and detached.</summary>
    Staccato,
    /// <summary>Very short and detached.</summary>
    Staccatissimo,
    /// <summary>Held for its full value.</summary>
    Tenuto,
    /// <summary>Emphasized.</summary>
    Accent,
    /// <summary>Strongly emphasized and slightly shortened.</summary>
    Marcato,
}

/// <summary>Sets the dynamic level from the anchored event onward, on every staff of its instrument.</summary>
/// <param name="Event">The event that carries the dynamic.</param>
/// <param name="Level">The new level.</param>
public sealed record DynamicMark(EventId Event, Dynamic Level);

/// <summary>Adds an articulation to the notes of one event.</summary>
/// <param name="Event">The articulated event.</param>
/// <param name="Kind">The articulation.</param>
public sealed record ArticulationMark(EventId Event, Articulation Kind);

/// <summary>Sets the tempo from a position onward.</summary>
/// <param name="Position">The absolute position in whole-note units.</param>
/// <param name="QuarterNotesPerMinute">The tempo in quarter notes per minute.</param>
public sealed record TempoMark(Fraction Position, double QuarterNotesPerMinute);

/// <summary>Carries the performance markings that the score model does not store yet (dynamics, articulations, tempo).</summary>
/// <param name="Dynamics">The dynamic marks.</param>
/// <param name="Articulations">The articulation marks.</param>
/// <param name="Tempos">The tempo marks.</param>
public sealed record PerformanceHints(
    ImmutableArray<DynamicMark> Dynamics,
    ImmutableArray<ArticulationMark> Articulations,
    ImmutableArray<TempoMark> Tempos)
{
    /// <summary>Gets hints with no markings.</summary>
    public static PerformanceHints Empty { get; } = new([], [], []);

    /// <summary>Reads the dynamics and articulations attached to a score's events.</summary>
    /// <param name="score">The score.</param>
    /// <returns>Hints for the interpreter; tempo marks come with F4.5.</returns>
    public static PerformanceHints FromScore(Score score)
    {
        ArgumentNullException.ThrowIfNull(score);
        ImmutableArray<DynamicMark>.Builder dynamics = ImmutableArray.CreateBuilder<DynamicMark>();
        ImmutableArray<ArticulationMark>.Builder articulations = ImmutableArray.CreateBuilder<ArticulationMark>();
        ImmutableArray<TempoMark>.Builder tempos = ImmutableArray.CreateBuilder<TempoMark>();
        Dictionary<EventId, Fraction>? positions = null;
        foreach (Attachment attachment in score.AttachmentList)
        {
            switch (attachment)
            {
                case TempoAttachment tempo:
                    positions ??= EventPositions(score);
                    if (positions.TryGetValue(tempo.Target, out Fraction at))
                    {
                        // The beat unit may not be a quarter: convert to quarter notes per minute.
                        Fraction quarters = tempo.Beat.Length * new Fraction(4, 1);
                        tempos.Add(new TempoMark(at, tempo.Bpm * (double)quarters.Num / quarters.Den));
                    }

                    break;
                case DynamicAttachment dynamic:
                    dynamics.Add(new DynamicMark(dynamic.Target, (Dynamic)(int)dynamic.Level));
                    break;
                case ArticulationAttachment articulation when articulation.Kind <= ArticulationKind.Marcato:
                    articulations.Add(new ArticulationMark(articulation.Target, (Articulation)(int)articulation.Kind));
                    break;
            }
        }

        return new PerformanceHints(dynamics.ToImmutable(), articulations.ToImmutable(), tempos.ToImmutable());
    }

    private static Dictionary<EventId, Fraction> EventPositions(Score score)
    {
        Dictionary<EventId, Fraction> positions = [];
        Fraction measureStart = Fraction.Zero;
        for (int measure = 0; measure < score.Measures.Length; measure++)
        {
            foreach (KeyValuePair<StaffMeasureKey, StaffMeasure> entry in score.Content)
            {
                if (entry.Key.MeasureIndex != measure)
                {
                    continue;
                }

                foreach (Voice voice in entry.Value.Voices)
                {
                    foreach (MusicEvent musicEvent in voice.Events)
                    {
                        positions.TryAdd(musicEvent.Id, measureStart + musicEvent.Onset);
                    }
                }
            }

            measureStart += score.Measures[measure].TimeSignature.Length;
        }

        return positions;
    }
}

/// <summary>Sets how dynamics and articulations are translated into MIDI velocity and sounding length.</summary>
public sealed record InterpretationSettings
{
    /// <summary>Gets the tempo used before the first tempo mark, in quarter notes per minute.</summary>
    public double DefaultTempo { get; init; } = 120;

    /// <summary>Gets the dynamic in force before the first dynamic mark.</summary>
    public Dynamic DefaultDynamic { get; init; } = Dynamic.Mf;

    /// <summary>Gets the share of the notated length that an unmarked note sounds.</summary>
    /// <remarks>A value below tenuto's keeps repeated notes distinct; it is Tessitura's adjustable choice.</remarks>
    public Fraction NormalGate { get; init; } = new(9, 10);

    /// <summary>Gets the share for staccato; the project definition fixes it at 50 %.</summary>
    public Fraction StaccatoGate { get; init; } = new(1, 2);

    /// <summary>Gets the share for staccatissimo; Tessitura's adjustable choice.</summary>
    public Fraction StaccatissimoGate { get; init; } = new(1, 4);

    /// <summary>Gets the share for tenuto; the project definition fixes it at 100 %.</summary>
    public Fraction TenutoGate { get; init; } = Fraction.One;

    /// <summary>Gets the share for marcato; Tessitura's adjustable choice.</summary>
    public Fraction MarcatoGate { get; init; } = new(17, 20);

    /// <summary>Gets the velocity added by an accent; Tessitura's adjustable choice.</summary>
    public int AccentBoost { get; init; } = 16;

    /// <summary>Gets the velocity added by a marcato; Tessitura's adjustable choice.</summary>
    public int MarcatoBoost { get; init; } = 24;

    /// <summary>Gets the MIDI velocity of a dynamic level.</summary>
    /// <param name="dynamic">The level.</param>
    /// <returns>A velocity from 1 to 127.</returns>
    public int VelocityOf(Dynamic dynamic) => dynamic switch
    {
        Dynamic.Ppp => 16,
        Dynamic.Pp => 33,
        Dynamic.P => 49,
        Dynamic.Mp => 64,
        Dynamic.Mf => 80,
        Dynamic.F => 96,
        Dynamic.Ff => 112,
        Dynamic.Fff => 126,
        _ => throw new ArgumentOutOfRangeException(nameof(dynamic)),
    };
}

/// <summary>One note as it should sound.</summary>
/// <param name="Instrument">The zero-based instrument index.</param>
/// <param name="Staff">The zero-based staff index in the score.</param>
/// <param name="Voice">The voice number.</param>
/// <param name="Midi">The MIDI note number, taken from the written pitch until transposition arrives (F4.12).</param>
/// <param name="Start">The absolute start in whole-note units.</param>
/// <param name="NotatedLength">The written length, with ties merged.</param>
/// <param name="SoundingLength">The length that actually sounds after the articulation gate.</param>
/// <param name="Velocity">The MIDI velocity from 1 to 127.</param>
public sealed record PerformedNote(
    int Instrument, int Staff, int Voice, int Midi, Fraction Start, Fraction NotatedLength,
    Fraction SoundingLength, int Velocity);

/// <summary>The score as a sorted list of notes to play plus its tempo map.</summary>
/// <param name="Notes">The notes ordered by start, then instrument and pitch.</param>
/// <param name="Tempo">The tempo map that converts positions to seconds.</param>
public sealed record Interpretation(ImmutableArray<PerformedNote> Notes, TempoMap Tempo);
