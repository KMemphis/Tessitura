using System.Collections.Immutable;
using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>
/// Pastes a copied fragment with its first onset at an absolute score position.
/// The context's staff and voice are the destination: the fragment's topmost staff lands on
/// that staff, and a single-voice fragment lands in that voice (several voices keep their numbers).
/// Pasted events replace whatever they overlap, and rests fill any gap left behind.
/// </summary>
/// <param name="Fragment">The copied fragment.</param>
/// <param name="Position">The absolute onset, in whole-note units, where the fragment starts.</param>
public sealed record PasteCommand(ClipboardFragment Fragment, Fraction Position) : IScoreCommand
{
    /// <inheritdoc />
    public string Description => "Paste";

    /// <inheritdoc />
    public Score Apply(Score score, EditContext context)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(Fragment);
        if (Fragment.Voices.IsDefaultOrEmpty)
        {
            return score;
        }

        int staffCount = 0;
        foreach (Instrument instrument in score.Instruments)
        {
            staffCount += instrument.Staves.Length;
        }

        foreach (ClipboardVoice clipped in Fragment.Voices)
        {
            if (context.StaffIndex + clipped.StaffOffset >= staffCount)
            {
                throw new ArgumentOutOfRangeException(nameof(context), "The paste needs more staves than the score has.");
            }
        }

        Fraction end = Position + Fragment.Length;
        while (TotalLength(score) < end)
        {
            score = new AppendMeasureCommand().Apply(score, context);
        }

        if (Position < Fraction.Zero || Position >= TotalLength(score))
        {
            throw new ArgumentOutOfRangeException(nameof(Position));
        }

        List<(int Staff, int Voice)> targets = [];
        foreach (ClipboardVoice clipped in Fragment.Voices)
        {
            int staff = context.StaffIndex + clipped.StaffOffset;
            int voice = Fragment.Voices.Length == 1 ? context.VoiceNumber : clipped.VoiceNumber;
            score = WriteRaw(score, staff, voice, clipped);
            targets.Add((staff, voice));
        }

        foreach ((int staff, int voice) in targets)
        {
            score = RhythmicScoreNormalizer.NormalizeVoice(score, new EditContext(staff, 0, voice));
        }

        return score;
    }

    private Score WriteRaw(Score score, int staff, int voiceNumber, ClipboardVoice clipped)
    {
        Fraction end = Position + Fragment.Length;
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content = score.Content.ToBuilder();
        Fraction measureStart = Fraction.Zero;
        for (int measureIndex = 0; measureIndex < score.Measures.Length; measureIndex++)
        {
            Fraction measureEnd = measureStart + score.Measures[measureIndex].TimeSignature.Length;
            StaffMeasureKey key = new(staff, measureIndex);
            content.TryGetValue(key, out StaffMeasure? staffMeasure);
            int voiceIndex = -1;
            List<MusicEvent> events = [];
            if (staffMeasure is not null)
            {
                for (int index = 0; index < staffMeasure.Voices.Length; index++)
                {
                    if (staffMeasure.Voices[index].Number == voiceNumber)
                    {
                        voiceIndex = index;
                    }
                }
            }

            if (voiceIndex >= 0)
            {
                foreach (MusicEvent existing in staffMeasure!.Voices[voiceIndex].Events)
                {
                    Fraction onset = measureStart + existing.Onset;
                    Fraction existingEnd = onset + existing.Duration.Length;
                    bool overlaps = onset < end && existingEnd > Position;
                    if (!overlaps)
                    {
                        events.Add(existing);
                    }
                    else if (existing is Chord && onset < Position)
                    {
                        throw new InvalidOperationException(
                            "The paste position falls inside a note; paste at the start of an event.");
                    }

                    // Overlapped rests and events starting inside the pasted span are replaced;
                    // rhythmic normalization fills the remaining gap with rests.
                }
            }

            foreach (ClipboardEvent clippedEvent in clipped.Events)
            {
                Fraction onset = Position + clippedEvent.Offset;
                if (onset < measureStart || onset >= measureEnd)
                {
                    continue;
                }

                EventId id = new(Guid.NewGuid());
                events.Add(clippedEvent.Notes.IsDefaultOrEmpty
                    ? new Rest(id, onset - measureStart, clippedEvent.Duration)
                    : new Chord(id, onset - measureStart, clippedEvent.Duration,
                        clippedEvent.Notes, clippedEvent.Stem));
            }

            events.Sort((a, b) => a.Onset.CompareTo(b.Onset));
            if (events.Count > 0 || voiceIndex >= 0)
            {
                Voice voice = new(voiceNumber, [.. events]);
                content[key] = staffMeasure is null
                    ? new StaffMeasure([voice])
                    : staffMeasure with
                    {
                        Voices = voiceIndex >= 0
                            ? staffMeasure.Voices.SetItem(voiceIndex, voice)
                            : staffMeasure.Voices.Add(voice),
                    };
            }

            measureStart = measureEnd;
        }

        return score with { Content = content.ToImmutable() };
    }

    private static Fraction TotalLength(Score score)
    {
        Fraction total = Fraction.Zero;
        foreach (Measure measure in score.Measures)
        {
            total += measure.TimeSignature.Length;
        }

        return total;
    }
}
