using System.Text.RegularExpressions;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Shared utility for parsing price values from raw text extracted from HTML cells.
/// </summary>
public static partial class PriceParser
{
    /// <summary>
    /// Extracts the first dollar amount from text that may contain extra content.
    /// E.g. "$0.11 (9.09M / $1)*" → 0.11
    /// E.g. "$ 2.6" → 2.6
    /// </summary>
    public static decimal? TryParseDollarAmount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var match = DollarAmountPattern().Match(raw);
        if (!match.Success)
            return null;

        var numStr = match.Groups[1].Value.Replace(",", ".");
        if (decimal.TryParse(
                numStr,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
            return value;

        return null;
    }

    /// <summary>
    /// Attempts to parse a decimal price from raw cell text.
    /// Strips non-numeric characters (currency symbols, whitespace) and normalizes separators.
    /// Best for cells that contain only a number (e.g. "3.00", "0.15").
    /// </summary>
    public static decimal? TryParsePrice(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var cleaned = Regex.Replace(raw, @"[^\d.,]", "");
        if (string.IsNullOrWhiteSpace(cleaned))
            return null;

        if (decimal.TryParse(
                cleaned.Replace(",", "."),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
            return value;

        return null;
    }

    [GeneratedRegex(@"\$\s*(\d+[.,]?\d*)")]
    private static partial Regex DollarAmountPattern();
}
