using System.Globalization;
using System.Text;

namespace Tessitura.Playback.Audio;

/// <summary>Chooses a General MIDI program from an instrument name, in Spanish or English.</summary>
public static class GeneralMidiPrograms
{
    private static readonly (string Word, int Program)[] Table =
    [
        ("contrabajo", 43), ("double bass", 43), ("violonchelo", 42), ("cello", 42), ("violin", 40), ("viola", 41),
        ("piano", 0), ("clavecin", 6), ("harpsichord", 6), ("organo", 19), ("organ", 19), ("guitarra", 24), ("guitar", 24),
        ("arpa", 46), ("harp", 46), ("flauta", 73), ("flute", 73), ("piccolo", 72), ("oboe", 68), ("corno ingles", 69),
        ("clarinete", 71), ("clarinet", 71), ("fagot", 70), ("bassoon", 70), ("saxofon", 65), ("saxophone", 65),
        ("trompeta", 56), ("trumpet", 56), ("trompa", 60), ("horn", 60), ("trombon", 57), ("trombone", 57), ("tuba", 58),
        ("marimba", 12), ("xilofono", 13), ("vibrafono", 11), ("timbal", 47),
        ("coro", 52), ("choir", 52), ("soprano", 52), ("alto", 52), ("tenor", 52), ("bajo", 52), ("bass", 52), ("voz", 52),
    ];

    /// <summary>Finds the program for an instrument name.</summary>
    /// <param name="name">The instrument name.</param>
    /// <returns>A program from 0 to 127; 0 (acoustic piano) when the name is not recognized.</returns>
    public static int FromName(string name)
    {
        string text = Normalize(name);
        foreach ((string word, int program) in Table)
        {
            if (text.Contains(word, StringComparison.Ordinal))
            {
                return program;
            }
        }

        return 0;
    }

    private static string Normalize(string text)
    {
        StringBuilder builder = new();
        foreach (char c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
