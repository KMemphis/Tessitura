using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>Stores one copied event relative to the start of the fragment.</summary>
/// <param name="Offset">The onset from the fragment start, in whole-note units.</param>
/// <param name="Duration">The notated duration.</param>
/// <param name="Notes">The written notes; empty for a rest.</param>
/// <param name="Stem">The stem direction preference.</param>
public sealed record ClipboardEvent(
    Fraction Offset, Duration Duration, ImmutableArray<Note> Notes, StemDirection Stem);

/// <summary>Stores the copied events of one voice on one staff.</summary>
/// <param name="StaffOffset">The staff distance from the topmost copied staff.</param>
/// <param name="VoiceNumber">The voice the events were copied from.</param>
/// <param name="Events">The events ordered by offset.</param>
public sealed record ClipboardVoice(int StaffOffset, int VoiceNumber, ImmutableArray<ClipboardEvent> Events);

/// <summary>Holds a copied range independently of any score, so it can be pasted anywhere.</summary>
/// <param name="Length">The span from the first copied onset to the end of the last copied event.</param>
/// <param name="Voices">The copied voices.</param>
public sealed record ClipboardFragment(Fraction Length, ImmutableArray<ClipboardVoice> Voices)
{
    /// <summary>Copies the events of a selection.</summary>
    /// <param name="score">The score the selection refers to.</param>
    /// <param name="selection">The selected events and notes.</param>
    /// <returns>The fragment, or null when the selection contains no events.</returns>
    public static ClipboardFragment? Copy(Score score, Selection selection)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Items.IsDefaultOrEmpty)
        {
            return null;
        }

        // A whole-event selection wins over a single-note selection of the same chord.
        Dictionary<EventId, int?> wanted = [];
        foreach (SelectionItem item in selection.Items)
        {
            wanted[item.EventId] = wanted.TryGetValue(item.EventId, out int? existing) && existing != item.NoteIndex
                ? null
                : item.NoteIndex;
        }

        List<(int Staff, int Voice, Fraction Onset, MusicEvent Event, int? NoteIndex)> found = [];
        Fraction measureStart = Fraction.Zero;
        for (int measureIndex = 0; measureIndex < score.Measures.Length; measureIndex++)
        {
            foreach (KeyValuePair<StaffMeasureKey, StaffMeasure> entry in score.Content)
            {
                if (entry.Key.MeasureIndex != measureIndex)
                {
                    continue;
                }

                foreach (Voice voice in entry.Value.Voices)
                {
                    foreach (MusicEvent musicEvent in voice.Events)
                    {
                        if (wanted.TryGetValue(musicEvent.Id, out int? noteIndex))
                        {
                            found.Add((entry.Key.StaffIndex, voice.Number, measureStart + musicEvent.Onset,
                                musicEvent, noteIndex));
                        }
                    }
                }
            }

            measureStart += score.Measures[measureIndex].TimeSignature.Length;
        }

        if (found.Count == 0)
        {
            return null;
        }

        int firstStaff = int.MaxValue;
        Fraction start = found[0].Onset;
        Fraction end = found[0].Onset;
        foreach ((int staff, _, Fraction onset, MusicEvent musicEvent, _) in found)
        {
            firstStaff = Math.Min(firstStaff, staff);
            if (onset < start)
            {
                start = onset;
            }

            Fraction eventEnd = onset + musicEvent.Duration.Length;
            if (eventEnd > end)
            {
                end = eventEnd;
            }
        }

        SortedDictionary<(int, int), List<ClipboardEvent>> voices = [];
        foreach ((int staff, int voiceNumber, Fraction onset, MusicEvent musicEvent, int? noteIndex) in found)
        {
            ImmutableArray<Note> notes = [];
            if (musicEvent is Chord chord)
            {
                notes = noteIndex is int index && index < chord.Notes.Length && chord.Notes.Length > 1
                    ? [chord.Notes[index]]
                    : chord.Notes;
            }

            (int, int) key = (staff - firstStaff, voiceNumber);
            if (!voices.TryGetValue(key, out List<ClipboardEvent>? list))
            {
                list = [];
                voices.Add(key, list);
            }

            StemDirection stem = musicEvent is Chord stemmed ? stemmed.Stem : StemDirection.Auto;
            list.Add(new ClipboardEvent(onset - start, musicEvent.Duration, notes, stem));
        }

        ImmutableArray<ClipboardVoice>.Builder result = ImmutableArray.CreateBuilder<ClipboardVoice>();
        foreach (((int staffOffset, int voiceNumber), List<ClipboardEvent> events) in voices)
        {
            events.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            result.Add(new ClipboardVoice(staffOffset, voiceNumber, [.. events]));
        }

        return new ClipboardFragment(end - start, result.ToImmutable());
    }
}
