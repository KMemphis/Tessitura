using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Tessitura.App;

/// <summary>Fuzzy-searches registered actions by name and identifier.</summary>
public static class CommandSearch
{
    /// <summary>Filters and ranks actions for a query.</summary>
    /// <param name="actions">All registered actions.</param>
    /// <param name="query">Space-separated tokens; each must match as a subsequence of the name or id.</param>
    /// <returns>Matching actions, best first; every action, ordered by name, for an empty query.</returns>
    public static ImmutableArray<RegisteredAction> Search(
        ImmutableArray<RegisteredAction> actions, string query)
    {
        string[] tokens = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<(RegisteredAction Action, int Score, string Name)> matches = [];
        foreach (RegisteredAction action in actions)
        {
            string name = Normalize(action.Name);
            string id = Normalize(action.Id);
            int total = 0;
            bool matched = true;
            foreach (string token in tokens)
            {
                int score = Math.Max(Score(name, token), Score(id, token) - 5);
                if (score < 0)
                {
                    matched = false;
                    break;
                }

                total += score;
            }

            if (matched)
            {
                matches.Add((action, total, name));
            }
        }

        matches.Sort((a, b) => a.Score != b.Score
            ? b.Score.CompareTo(a.Score)
            : string.CompareOrdinal(a.Name, b.Name));
        ImmutableArray<RegisteredAction>.Builder result =
            ImmutableArray.CreateBuilder<RegisteredAction>(matches.Count);
        foreach ((RegisteredAction action, _, _) in matches)
        {
            result.Add(action);
        }

        return result.MoveToImmutable();
    }

    // Greedy subsequence score: rewards consecutive characters and word starts;
    // negative when the token is not a subsequence of the text.
    private static int Score(string text, string token)
    {
        int position = 0;
        int score = 0;
        int previous = -2;
        foreach (char c in token)
        {
            int found = text.IndexOf(c, position);
            if (found < 0)
            {
                return -1;
            }

            score += 1;
            if (found == previous + 1)
            {
                score += 4;
            }

            if (found == 0 || !char.IsLetterOrDigit(text[found - 1]))
            {
                score += 3;
            }

            previous = found;
            position = found + 1;
        }

        int contiguous = text.IndexOf(token, StringComparison.Ordinal);
        return contiguous >= 0 ? score + 10 - Math.Min(contiguous, 10) : score;
    }

    private static string Normalize(string text)
    {
        string decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
