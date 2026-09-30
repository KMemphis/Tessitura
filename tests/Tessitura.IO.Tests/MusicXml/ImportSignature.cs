using Tessitura.Core;

namespace Tessitura.IO.Tests.MusicXml;

/// <summary>Compares the pitched notes of a MusicXML file with those of the score imported from it.</summary>
public static class ImportSignature
{
    /// <summary>Builds the note events of a score in the same key format as <see cref="MusicXmlSignature"/>.</summary>
    /// <param name="score">The imported score.</param>
    /// <returns>Pitched notes, one event per note.</returns>
    public static List<string> Notes(Score score)
    {
        List<string> keys = [];
        int staffBase = 0;
        for (int part = 0; part < score.Instruments.Length; part++)
        {
            for (int staff = staffBase; staff < staffBase + score.Instruments[part].Staves.Length; staff++)
            {
                for (int measure = 0; measure < score.Measures.Length; measure++)
                {
                    foreach (Voice voice in score.Content[new StaffMeasureKey(staff, measure)].Voices)
                    {
                        foreach (MusicEvent e in voice.Events)
                        {
                            if (e is not Chord chord)
                            {
                                continue;
                            }

                            foreach (Note note in chord.Notes)
                            {
                                string pitch = $"{note.Pitch.Step}{(note.Pitch.Alter == 0 ? "" : $"[{note.Pitch.Alter}]")}{note.Pitch.Octave}";
                                keys.Add($"{part}|{measure}|{e.Onset.Num}/{e.Onset.Den}|{e.Duration.Length.Num}/{e.Duration.Length.Den}|note|{pitch}|{(note.TiedToNext ? "start" : "")}");
                            }
                        }
                    }
                }
            }

            staffBase += score.Instruments[part].Staves.Length;
        }

        return keys;
    }

    /// <summary>Extracts the pitched notes of the original file, keeping only the "tie starts" the model stores.</summary>
    /// <param name="path">The MusicXML file.</param>
    /// <returns>Pitched notes, one event per note.</returns>
    public static List<string> Notes(string path)
    {
        List<string> keys = [];
        foreach (MusicXmlEvent e in MusicXmlSignature.FromFile(path).Events.Where(e => e.Kind == "note"))
        {
            string tie = e.Tie is "start" or "both" ? "start" : "";
            keys.Add((e with { Tie = tie }).Key);
        }

        return keys;
    }

    /// <summary>Compares the notes of a file with the notes of a score.</summary>
    /// <param name="path">The original file.</param>
    /// <param name="score">The imported score.</param>
    /// <returns>Matched, missing and extra note counts.</returns>
    public static (int Matched, int Missing, int Extra) Compare(string path, Score score)
    {
        Dictionary<string, int> remaining = [];
        List<string> expected = Notes(path);
        foreach (string key in expected)
        {
            remaining[key] = remaining.GetValueOrDefault(key) + 1;
        }

        int matched = 0;
        int extra = 0;
        foreach (string key in Notes(score))
        {
            if (remaining.TryGetValue(key, out int count) && count > 0)
            {
                remaining[key] = count - 1;
                matched++;
            }
            else
            {
                extra++;
            }
        }

        return (matched, expected.Count - matched, extra);
    }
}
