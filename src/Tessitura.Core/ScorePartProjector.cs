using System.Collections.Immutable;

namespace Tessitura.Core;

/// <summary>Builds an immutable, linked score snapshot for one part definition.</summary>
public static class ScorePartProjector
{
    /// <summary>Projects the current source-score snapshot onto the instruments selected by a part.</summary>
    /// <param name="score">The latest source score snapshot.</param>
    /// <param name="part">The linked part to project.</param>
    /// <returns>A score with selected instruments and content reindexed from zero.</returns>
    /// <remarks>
    /// The projection retains event identifiers and the selected source snapshot's measures. Re-projecting after
    /// each score command therefore exposes edits without copying or synchronizing a second music model.
    /// </remarks>
    public static Score Project(Score score, ScorePartView part)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(part);

        int sourceStaffCount = CountStaves(score);
        int[] targetStaffBySource = new int[sourceStaffCount];
        Array.Fill(targetStaffBySource, -1);
        ImmutableArray<Instrument>.Builder instruments = ImmutableArray.CreateBuilder<Instrument>();
        int targetStaffCount = 0;
        foreach (int instrumentIndex in part.InstrumentIndices)
        {
            if (instrumentIndex >= score.Instruments.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(part),
                    $"Part '{part.Name}' refers to missing instrument {instrumentIndex}.");
            }

            Instrument instrument = score.Instruments[instrumentIndex];
            instruments.Add(instrument);
            int sourceStaffIndex = GetFirstStaffIndex(score.Instruments, instrumentIndex);
            for (int localStaff = 0; localStaff < instrument.Staves.Length; localStaff++)
            {
                targetStaffBySource[sourceStaffIndex + localStaff] = targetStaffCount++;
            }
        }

        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content =
            ImmutableDictionary.CreateBuilder<StaffMeasureKey, StaffMeasure>();
        HashSet<EventId> includedEvents = [];
        foreach ((StaffMeasureKey key, StaffMeasure staffMeasure) in score.Content)
        {
            if (key.StaffIndex < 0 || key.StaffIndex >= targetStaffBySource.Length)
            {
                continue;
            }

            int targetStaffIndex = targetStaffBySource[key.StaffIndex];
            if (targetStaffIndex < 0)
            {
                continue;
            }

            content.Add(new StaffMeasureKey(targetStaffIndex, key.MeasureIndex), staffMeasure);
            foreach (Voice voice in staffMeasure.Voices)
            {
                foreach (MusicEvent musicEvent in voice.Events)
                {
                    AddEventAndChildren(includedEvents, musicEvent);
                }
            }
        }

        ImmutableArray<Attachment> attachments = score.AttachmentList.IsEmpty
            ? default
            : [.. FilterAttachments(score.AttachmentList, includedEvents)];
        ImmutableArray<Spanner> spanners = score.SpannerList.IsEmpty
            ? default
            : [.. FilterSpanners(score.SpannerList, includedEvents)];
        return new Score(score.Metadata, instruments.ToImmutable(), score.Measures, content.ToImmutable(),
            attachments, spanners);
    }

    private static IEnumerable<Attachment> FilterAttachments(ImmutableArray<Attachment> attachments,
        HashSet<EventId> includedEvents)
    {
        foreach (Attachment attachment in attachments)
        {
            if (includedEvents.Contains(attachment.Target))
            {
                yield return attachment;
            }
        }
    }

    private static IEnumerable<Spanner> FilterSpanners(ImmutableArray<Spanner> spanners,
        HashSet<EventId> includedEvents)
    {
        foreach (Spanner spanner in spanners)
        {
            if (includedEvents.Contains(spanner.Start) && includedEvents.Contains(spanner.End))
            {
                yield return spanner;
            }
        }
    }

    private static void AddEventAndChildren(HashSet<EventId> includedEvents, MusicEvent musicEvent)
    {
        includedEvents.Add(musicEvent.Id);
        if (musicEvent is not TupletGroup group)
        {
            return;
        }

        foreach (MusicEvent child in group.Children)
        {
            AddEventAndChildren(includedEvents, child);
        }
    }

    private static int CountStaves(Score score)
    {
        int count = 0;
        foreach (Instrument instrument in score.Instruments)
        {
            count = checked(count + instrument.Staves.Length);
        }

        return count;
    }

    private static int GetFirstStaffIndex(ImmutableArray<Instrument> instruments, int instrumentIndex)
    {
        int firstStaffIndex = 0;
        for (int index = 0; index < instrumentIndex; index++)
        {
            firstStaffIndex = checked(firstStaffIndex + instruments[index].Staves.Length);
        }

        return firstStaffIndex;
    }
}
