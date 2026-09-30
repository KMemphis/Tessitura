using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Engraving;

/// <summary>Identifies an event participating in a rhythmic column.</summary>
/// <param name="EventId">The stable score event identifier.</param>
/// <param name="StaffIndex">The zero-based staff index.</param>
/// <param name="VoiceNumber">The voice number on that staff.</param>
public readonly record struct SegmentEvent(EventId EventId, int StaffIndex, int VoiceNumber);

/// <summary>Groups simultaneous events from all staves at one exact onset.</summary>
/// <param name="Onset">The exact onset within the measure.</param>
/// <param name="Events">The events ordered by staff and voice.</param>
public sealed record RhythmicSegment(Fraction Onset, ImmutableArray<SegmentEvent> Events);

/// <summary>Builds aligned rhythmic columns from an immutable score snapshot.</summary>
public sealed class RhythmicSegmentBuilder
{
    /// <summary>Builds the ordered columns for one global measure.</summary>
    /// <param name="score">The source score snapshot.</param>
    /// <param name="measureIndex">The zero-based global measure index.</param>
    /// <returns>The columns ordered by exact musical onset.</returns>
    public ImmutableArray<RhythmicSegment> Build(Score score, int measureIndex)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (measureIndex < 0 || measureIndex >= score.Measures.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(measureIndex));
        }

        SortedDictionary<Fraction, List<SegmentEvent>> byOnset = new();
        int staffIndex = 0;
        foreach (Instrument instrument in score.Instruments)
        {
            foreach (Staff _ in instrument.Staves)
            {
                if (score.Content.TryGetValue(new StaffMeasureKey(staffIndex, measureIndex),
                    out StaffMeasure? content))
                {
                    foreach (Voice voice in content.Voices)
                    {
                        foreach (MusicEvent musicEvent in voice.Events)
                        {
                            if (!byOnset.TryGetValue(musicEvent.Onset, out List<SegmentEvent>? events))
                            {
                                events = new List<SegmentEvent>();
                                byOnset.Add(musicEvent.Onset, events);
                            }

                            events.Add(new SegmentEvent(musicEvent.Id, staffIndex, voice.Number));
                        }
                    }
                }

                staffIndex++;
            }
        }

        ImmutableArray<RhythmicSegment>.Builder segments =
            ImmutableArray.CreateBuilder<RhythmicSegment>(byOnset.Count);
        foreach (KeyValuePair<Fraction, List<SegmentEvent>> entry in byOnset)
        {
            entry.Value.Sort(static (left, right) =>
            {
                int staffComparison = left.StaffIndex.CompareTo(right.StaffIndex);
                return staffComparison != 0 ? staffComparison :
                    left.VoiceNumber.CompareTo(right.VoiceNumber);
            });
            ImmutableArray<SegmentEvent>.Builder events =
                ImmutableArray.CreateBuilder<SegmentEvent>(entry.Value.Count);
            foreach (SegmentEvent item in entry.Value)
            {
                events.Add(item);
            }

            segments.Add(new RhythmicSegment(entry.Key, events.MoveToImmutable()));
        }

        return segments.MoveToImmutable();
    }
}
