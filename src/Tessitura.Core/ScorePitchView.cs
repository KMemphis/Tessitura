using System.Collections.Immutable;

namespace Tessitura.Core;

/// <summary>Resolves the pitch and key-signature context for a written or concert-pitch view.</summary>
public static class ScorePitchView
{
    /// <summary>Creates an immutable full-score view in written or concert pitch.</summary>
    /// <param name="score">The source score whose notes are stored in written pitch.</param>
    /// <param name="mode">Whether to keep written pitches or convert every note to concert pitch.</param>
    /// <returns>The source snapshot for written pitch, or a derived concert-pitch snapshot.</returns>
    public static Score Project(Score score, PitchDisplayMode mode)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (mode == PitchDisplayMode.Written || score.Instruments.IsEmpty)
        {
            return score;
        }

        ImmutableArray<int>.Builder indices = ImmutableArray.CreateBuilder<int>(score.Instruments.Length);
        for (int instrumentIndex = 0; instrumentIndex < score.Instruments.Length; instrumentIndex++)
        {
            indices.Add(instrumentIndex);
        }

        Score projected = ScorePartProjector.Project(score,
            new ScorePartView("Concert pitch", indices.MoveToImmutable()), PitchDisplayMode.Concert);
        return projected with { Parts = score.Parts };
    }

    /// <summary>Gets the instrument transposition for a flattened zero-based staff index.</summary>
    /// <param name="score">The score containing the staff.</param>
    /// <param name="staffIndex">The flattened staff index across all instruments.</param>
    /// <returns>The interval from that instrument's written pitch to its concert pitch.</returns>
    public static Interval GetInstrumentTransposition(Score score, int staffIndex)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (staffIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(staffIndex));
        }

        int remaining = staffIndex;
        foreach (Instrument instrument in score.Instruments)
        {
            if (remaining < instrument.Staves.Length)
            {
                return instrument.Transposition;
            }

            remaining -= instrument.Staves.Length;
        }

        throw new ArgumentOutOfRangeException(nameof(staffIndex));
    }

    /// <summary>Gets the key signature shown for one staff in the selected pitch view.</summary>
    /// <param name="score">The score whose measures hold concert key signatures.</param>
    /// <param name="staffIndex">The flattened zero-based staff index.</param>
    /// <param name="measureIndex">The zero-based measure index.</param>
    /// <param name="mode">Whether to show written or concert pitch.</param>
    /// <returns>The key signature for the selected staff and view.</returns>
    public static KeySignature GetKeySignature(Score score, int staffIndex, int measureIndex,
        PitchDisplayMode mode)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (measureIndex < 0 || measureIndex >= score.Measures.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(measureIndex));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        KeySignature concertKey = score.Measures[measureIndex].KeySignature;
        return mode switch
        {
            PitchDisplayMode.Concert => concertKey,
            PitchDisplayMode.Written => concertKey.Transpose(
                GetInstrumentTransposition(score, staffIndex).Inverse()),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }
}
