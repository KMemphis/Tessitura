using System.Globalization;
using System.Text.RegularExpressions;
using Tessitura.Core;

namespace Tessitura.Editing;

/// <summary>Says what the user meant to type in a popover.</summary>
public enum TextEntryKind
{
    /// <summary>A dynamic such as <c>mf</c> (<c>Shift+D</c>).</summary>
    Dynamic,
    /// <summary>A tempo such as <c>q=120</c> (<c>Shift+T</c>).</summary>
    Tempo,
    /// <summary>Text or a chord symbol such as <c>Cmaj7</c> (<c>Shift+X</c>).</summary>
    Text,
}

/// <summary>Turns text typed in a popover into the right attachment.</summary>
public static partial class TextEntryParser
{
    /// <summary>Parses an entry.</summary>
    /// <param name="input">The typed text.</param>
    /// <param name="kind">Which popover it came from.</param>
    /// <param name="target">The event the result attaches to.</param>
    /// <param name="attachment">The attachment, or null when the text is not valid for the kind.</param>
    /// <returns>Whether the text was understood.</returns>
    public static bool TryParse(string input, TextEntryKind kind, EventId target, out Attachment? attachment)
    {
        ArgumentNullException.ThrowIfNull(input);
        string text = input.Trim();
        attachment = null;
        if (text.Length == 0)
        {
            return false;
        }

        switch (kind)
        {
            case TextEntryKind.Dynamic:
                if (Enum.TryParse(text, ignoreCase: true, out DynamicLevel level) && Enum.IsDefined(level) && text.All(char.IsLetter))
                {
                    attachment = new DynamicAttachment(target, level);
                }

                return attachment is not null;
            case TextEntryKind.Tempo:
                return TryParseTempo(text, target, out attachment);
            default:
                Match chord = ChordPattern().Match(text);
                if (chord.Success && IsQuality(chord.Groups[3].Value))
                {
                    Step root = Enum.Parse<Step>(chord.Groups[1].Value);
                    Step? bass = chord.Groups[5].Success ? Enum.Parse<Step>(chord.Groups[5].Value) : null;
                    attachment = new ChordSymbolAttachment(target, root, Alter(chord.Groups[2].Value), chord.Groups[3].Value, bass,
                        Alter(chord.Groups[6].Value));
                }
                else
                {
                    attachment = new TextAttachment(target, text);
                }

                return true;
        }
    }

    private static bool TryParseTempo(string text, EventId target, out Attachment? attachment)
    {
        attachment = null;
        Match match = TempoPattern().Match(text);
        if (!match.Success ||
            !double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm) ||
            bpm is < 20 or > 400)
        {
            return false;
        }

        NoteValue value = match.Groups[1].Value.ToLowerInvariant() switch
        {
            "w" => NoteValue.Whole,
            "h" => NoteValue.Half,
            "e" or "♪" => NoteValue.Eighth,
            "s" => NoteValue.Sixteenth,
            _ => NoteValue.Quarter, // q, ♩ or nothing
        };
        attachment = new TempoAttachment(target, new Duration(value, match.Groups[2].Length > 0 ? 1 : 0), bpm);
        return true;
    }

    private static int Alter(string mark) => mark switch { "#" or "♯" => 1, "b" or "♭" => -1, _ => 0 };

    // The quality suffixes players expect; anything else is treated as plain text.
    private static bool IsQuality(string quality) => QualityPattern().IsMatch(quality);

    [GeneratedRegex(@"^([qhwes♩♪]?)(\.?)\s*=\s*(\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex TempoPattern();

    [GeneratedRegex(@"^([A-G])([#b♯♭]?)([^/]*)(/([A-G])([#b♯♭]?))?$")]
    private static partial Regex ChordPattern();

    [GeneratedRegex(@"^(?:m|min|-|maj|M|dim|aug|\+|ø)?(?:6|7|9|11|13)?(?:sus[24]|add\d+|[b#]\d+|\(?[b#]\d+\)?)*$")]
    private static partial Regex QualityPattern();
}
