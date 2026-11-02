using System.Text;
using System.Text.RegularExpressions;

namespace CodexControlDeck.Core;

public static partial class ActionAliasNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Replace('ё', 'е');
        normalized = PunctuationPattern().Replace(normalized, " ");
        return WhitespacePattern().Replace(normalized, " ").Trim();
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex PunctuationPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
