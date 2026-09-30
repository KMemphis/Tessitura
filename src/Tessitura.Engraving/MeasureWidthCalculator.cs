using Tessitura.Core;
using Tessitura.Smufl;

namespace Tessitura.Engraving;

/// <summary>Measures collision-safe and preferred widths for one global score measure.</summary>
public sealed class MeasureWidthCalculator
{
    private readonly SmuflMetadata _metadata;
    private readonly HorizontalSpacer _spacer = new();

    /// <summary>Creates a calculator using the selected SMuFL font metrics.</summary>
    /// <param name="metadata">The selected font's glyph bounding boxes.</param>
    public MeasureWidthCalculator(SmuflMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _metadata = metadata;
    }

    /// <summary>Calculates the aligned width of a measure across every staff and voice.</summary>
    /// <param name="score">The immutable score snapshot.</param>
    /// <param name="measureIndex">The zero-based measure index.</param>
    /// <param name="style">The engraving style used for collision clearances.</param>
    /// <param name="cancellationToken">Cancels the calculation before it returns.</param>
    /// <returns>The minimum, ideal, and elastic measure width in staff spaces.</returns>
    public SystemBreakMeasure Calculate(Score score, int measureIndex, Style style,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(style);
        cancellationToken.ThrowIfCancellationRequested();
        if (measureIndex < 0 || measureIndex >= score.Measures.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(measureIndex));
        }

        if (!double.IsFinite(style.MinimumAccidentalGap) || style.MinimumAccidentalGap < 0 ||
            !double.IsFinite(style.MinimumRhythmicGap) || style.MinimumRhythmicGap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(style),
                "Engraving gaps must be finite and nonnegative.");
        }

        List<MeasureColumn> columns = [];
        int staffIndex = 0;
        foreach (Instrument instrument in score.Instruments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (Staff _ in instrument.Staves)
            {
                if (score.Content.TryGetValue(new StaffMeasureKey(staffIndex, measureIndex),
                    out StaffMeasure? staffMeasure))
                {
                    foreach (Voice voice in staffMeasure.Voices)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        foreach (MusicEvent musicEvent in voice.Events)
                        {
                            AddEvent(columns, musicEvent, style);
                        }
                    }
                }

                staffIndex++;
            }
        }

        if (columns.Count == 0)
        {
            return new SystemBreakMeasure(4, 4, 1);
        }

        Fraction shortest = columns[0].Duration;
        for (int index = 1; index < columns.Count; index++)
        {
            if (columns[index].Duration < shortest)
            {
                shortest = columns[index].Duration;
            }
        }

        double minimumWidth = columns[0].LeftExtent + columns[^1].RightExtent;
        double idealWidth = minimumWidth;
        for (int index = 1; index < columns.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MeasureColumn previous = columns[index - 1];
            MeasureColumn current = columns[index];
            double collisionWidth = previous.RightExtent + current.LeftExtent +
                style.MinimumAccidentalGap;
            double rhythmicWidth = _spacer.IdealWidth(previous.Duration, shortest, 2);
            minimumWidth += collisionWidth;
            idealWidth += Math.Max(collisionWidth, rhythmicWidth);
        }

        double elasticity = Math.Max(1, idealWidth - minimumWidth);
        return new SystemBreakMeasure(minimumWidth, idealWidth, elasticity);
    }

    private void AddEvent(List<MeasureColumn> columns, MusicEvent musicEvent, Style style)
    {
        if (musicEvent is TupletGroup)
        {
            // Members space like ordinary events at their sounding onsets and lengths.
            foreach ((MusicEvent leaf, Fraction onset, Fraction length) in new[] { musicEvent }.Flatten())
            {
                AddEvent(columns, leaf.WithOnset(onset), style);
            }

            return;
        }

        if (musicEvent.Onset < Fraction.Zero || musicEvent.Length <= Fraction.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(musicEvent),
                "Event onsets must be nonnegative and durations must be positive.");
        }

        double leftExtent;
        double rightExtent;
        if (musicEvent is Chord chord)
        {
            // Glyph widths use the selected font's SMuFL glyphBBoxes.
            string glyphName = musicEvent.Duration.Value switch
            {
                NoteValue.Whole => "noteheadWhole",
                NoteValue.Half => "noteheadHalf",
                _ => "noteheadBlack",
            };
            SmuflBoundingBox notehead = _metadata.GetBoundingBox(glyphName);
            leftExtent = LeftExtent(notehead);
            rightExtent = RightExtent(notehead);
            for (int noteIndex = 0; noteIndex < chord.Notes.Length; noteIndex++)
            {
                int alter = chord.Notes[noteIndex].Pitch.Alter;
                if (alter != 0)
                {
                    string accidentalName = AccidentalGlyph(alter);
                    SmuflBoundingBox accidental = _metadata.GetBoundingBox(accidentalName);
                    // Behind Bars, Accidentals > Placing: retain a visible gap before the notehead.
                    leftExtent += style.MinimumAccidentalGap +
                        accidental.NorthEast.X - accidental.SouthWest.X;
                }
            }

            if (musicEvent.Duration.Dots > 0)
            {
                // Behind Bars, Dots > Placing dots: allow a style-controlled gap before each dot.
                SmuflBoundingBox dot = _metadata.GetBoundingBox("augmentationDot");
                rightExtent += musicEvent.Duration.Dots *
                    (dot.NorthEast.X - dot.SouthWest.X + style.MinimumRhythmicGap / 2);
            }
        }
        else
        {
            string glyphName = RestGlyph(musicEvent.Duration.Value);
            SmuflBoundingBox rest = _metadata.GetBoundingBox(glyphName);
            leftExtent = LeftExtent(rest);
            rightExtent = RightExtent(rest);
            if (musicEvent.Duration.Dots > 0)
            {
                SmuflBoundingBox dot = _metadata.GetBoundingBox("augmentationDot");
                rightExtent += musicEvent.Duration.Dots *
                    (dot.NorthEast.X - dot.SouthWest.X + style.MinimumRhythmicGap / 2);
            }
        }

        AddColumn(columns, musicEvent, leftExtent, rightExtent);
    }

    private static void AddColumn(List<MeasureColumn> columns, MusicEvent musicEvent,
        double leftExtent, double rightExtent)
    {
        int columnIndex = 0;
        while (columnIndex < columns.Count && columns[columnIndex].Onset < musicEvent.Onset)
        {
            columnIndex++;
        }

        if (columnIndex < columns.Count && columns[columnIndex].Onset == musicEvent.Onset)
        {
            MeasureColumn existing = columns[columnIndex];
            columns[columnIndex] = existing with
            {
                Duration = musicEvent.Length < existing.Duration
                    ? musicEvent.Length
                    : existing.Duration,
                LeftExtent = Math.Max(leftExtent, existing.LeftExtent),
                RightExtent = Math.Max(rightExtent, existing.RightExtent),
            };
            return;
        }

        columns.Insert(columnIndex, new MeasureColumn(musicEvent.Onset,
            musicEvent.Length, leftExtent, rightExtent));
    }

    private static double LeftExtent(SmuflBoundingBox box) => Math.Max(0, -box.SouthWest.X);

    private static double RightExtent(SmuflBoundingBox box) => Math.Max(0, box.NorthEast.X);

    private static string RestGlyph(NoteValue value) => value switch
    {
        NoteValue.Whole => "restWhole",
        NoteValue.Half => "restHalf",
        NoteValue.Quarter => "restQuarter",
        NoteValue.Eighth => "rest8th",
        NoteValue.Sixteenth => "rest16th",
        NoteValue.ThirtySecond => "rest32nd",
        NoteValue.SixtyFourth => "rest64th",
        NoteValue.HundredTwentyEighth => "rest128th",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string AccidentalGlyph(int alter) => alter switch
    {
        1 => "accidentalSharp",
        -1 => "accidentalFlat",
        2 => "accidentalDoubleSharp",
        -2 => "accidentalDoubleFlat",
        > 2 => "accidentalTripleSharp",
        < -2 => "accidentalTripleFlat",
        _ => "accidentalNatural",
    };

    private readonly record struct MeasureColumn(
        Fraction Onset, Fraction Duration, double LeftExtent, double RightExtent);
}
