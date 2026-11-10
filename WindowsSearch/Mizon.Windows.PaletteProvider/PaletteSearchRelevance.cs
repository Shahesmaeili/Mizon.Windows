using System.Globalization;
using System.Text;

namespace Mizon.Windows.PaletteProvider;

internal static class PaletteSearchRelevance
{
    internal static PaletteResultData Apply(PaletteResultData data, string? query)
    {
        var normalizedQuery = Normalize(query?.Trim().TrimStart('@'));
        if (normalizedQuery.Length == 0)
            return data with { SearchQuery = null, MatchedTitle = null, MatchedSubtitle = null, RelevanceScore = null };

        var title = data.DisplayTitle;
        var score = 0d;
        if (data.Kind == PaletteResultKind.Music)
        {
            Consider(data.PersianTitle);
            Consider(data.Title);
        }
        else
        {
            Consider(data.NickName);
            Consider(data.FullName);
            Consider(data.Username, username: true);
        }
        var subtitle = string.Join(" · ", new[] { data.DisplayTitle, data.SecondaryTitle }
            .Where(value => !string.IsNullOrWhiteSpace(value) && !string.Equals(value, title, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        return data with { SearchQuery = query, MatchedTitle = title, MatchedSubtitle = subtitle, RelevanceScore = score };

        void Consider(string? value, bool username = false)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var candidateScore = Match(Normalize(value), normalizedQuery);
            if (candidateScore <= score) return;
            score = candidateScore;
            title = username ? "@" + value.Trim().TrimStart('@') : value.Trim();
        }
    }

    // A bounded relevance hint, not a priority override. Windows combines this
    // with its own title matching, history and cross-provider ranking signals.
    private static double Match(string candidate, string query)
    {
        if (candidate == query) return 1;
        var coverage = (double)query.Length / candidate.Length;
        var best = 0d;
        for (var from = 0; from < candidate.Length;)
        {
            var index = candidate.IndexOf(query, from, StringComparison.Ordinal);
            if (index < 0) break;
            if (index == 0) return .82 + .12 * coverage;
            var startsWord = !char.IsLetterOrDigit(candidate[index - 1]);
            var end = index + query.Length;
            var endsWord = end == candidate.Length || !char.IsLetterOrDigit(candidate[end]);
            var score = startsWord && endsWord ? .78 + .10 * coverage
                : startsWord ? .65 + .10 * coverage : .45 + .15 * coverage;
            best = Math.Max(best, score);
            from = index + 1;
        }
        return best;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark || character == '\u0640') continue;
            var letter = character switch { '\u064A' or '\u0649' => '\u06CC', '\u0643' => '\u06A9', _ => char.ToUpperInvariant(character) };
            if (char.IsWhiteSpace(letter) || letter == '\u200C')
            {
                if (result.Length > 0 && result[^1] != ' ') result.Append(' ');
            }
            else result.Append(letter);
        }
        return result.ToString().TrimEnd();
    }
}
