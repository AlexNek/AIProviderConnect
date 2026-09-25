using System.Text.RegularExpressions;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Validates whether extracted text looks like a real AI model name
/// vs. a table header, section label, JSON path, or descriptive sentence.
/// </summary>
public static partial class ModelNameValidator
{
    /// <summary>
    /// Returns true if the text looks like a legitimate model identifier/name.
    /// </summary>
    public static bool IsLikelyModelName(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
            return false;

        // Too long to be a model name — likely a sentence/description
        if (text.Length > 80)
            return false;

        // Known junk patterns: table headers, column labels, pricing tier names, schema keywords
        if (KnownNonModelPattern().IsMatch(text))
            return false;

        // All uppercase multi-word text is usually a header (e.g. "MODEL VERSION")
        var upper = text.ToUpperInvariant();
        if (upper == text && text.Contains(' ') && !text.Contains('/'))
            return false;

        // Contains HTML entities (e.g. "&gt;") — likely raw HTML artifact
        if (text.Contains("&gt;") || text.Contains("&lt;") || text.Contains("&amp;"))
            return false;

        // JSON/API path artifacts (e.g. "data[].created", "response.choices")
        if (text.Contains("[]") || text.Contains("()") || JsonPathPattern().IsMatch(text))
            return false;

        // Programming/technical tokens that aren't model names
        if (text.Contains('{') || text.Contains('}'))
            return false;

        return true;
    }

    [GeneratedRegex(@"^[a-z_]+(\[\])?(\.[a-z_]+)+", RegexOptions.IgnoreCase)]
    private static partial Regex JsonPathPattern();

    [GeneratedRegex(
        @"^(input\s*price|output\s*price|model(\s+version)?|models?\s+[>&<]|pay\s+as\s+you\s+go|price|pricing|per\s+(token|1[km]|million|request)|free\s+tier|enterprise|on[\s-]?demand|reserved|batch|tier|plan|category|type|endpoint|including\s+thinking|data\[|response\.|request\.|created|updated|status|description|parameters?|properties|items?|required|optional|default|returns?|string|integer|boolean|number|object|array|null)",
        RegexOptions.IgnoreCase)]
    private static partial Regex KnownNonModelPattern();
}
