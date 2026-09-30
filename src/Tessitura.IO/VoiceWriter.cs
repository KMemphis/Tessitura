using System.Collections.Immutable;
using Tessitura.Core;
using Tessitura.Editing;

namespace Tessitura.IO;

/// <summary>Places the raw events of one voice on a score's measure grid and lets the rhythmic engine clean them up.</summary>
internal static class VoiceWriter
{
    /// <summary>Writes the events of a voice, one list per measure, and normalizes the voice.</summary>
    /// <param name="score">A score in which every staff measure already exists.</param>
    /// <param name="staff">The zero-based staff.</param>
    /// <param name="voice">The voice number, from one to four.</param>
    /// <param name="perMeasure">The events of each measure, ordered by onset.</param>
    /// <param name="failure">Why the voice could not be laid out, when the result is null.</param>
    /// <returns>The new score, or null when the events do not fit the grid.</returns>
    public static Score? Write(Score score, int staff, int voice, IReadOnlyList<List<MusicEvent>> perMeasure, out string? failure)
    {
        failure = null;
        ImmutableDictionary<StaffMeasureKey, StaffMeasure>.Builder content = score.Content.ToBuilder();
        for (int m = 0; m < perMeasure.Count; m++)
        {
            StaffMeasureKey key = new(staff, m);
            StaffMeasure staffMeasure = content[key];
            Voice replaced = new(voice, [.. perMeasure[m]]);
            int index = -1;
            for (int i = 0; i < staffMeasure.Voices.Length; i++)
            {
                if (staffMeasure.Voices[i].Number == voice)
                {
                    index = i;
                }
            }

            content[key] = staffMeasure with
            {
                Voices = index >= 0 ? staffMeasure.Voices.SetItem(index, replaced) : staffMeasure.Voices.Add(replaced),
            };
        }

        Score candidate = score with { Content = content.ToImmutable() };
        try
        {
            return new NormalizeVoiceCommand().Apply(candidate, new EditContext(staff, 0, voice));
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException
            or ArgumentOutOfRangeException or ArgumentException or OverflowException)
        {
            failure = exception.Message;
            return null;
        }
    }
}
