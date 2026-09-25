using System.Text.RegularExpressions;

using HtmlAgilityPack;

using ScraperTool.Models;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Parses pricing data from plain-text content (e.g. Playwright-rendered text).
/// Looks for lines matching patterns like: "ModelName $X.XX $Y.YY"
/// </summary>
public sealed partial class TextContentPricingParser : IPricingPageParser
{
    /// <summary>
    /// This parser works on plain text, not HTML structure.
    /// Use <see cref="ParseText"/> directly for text content.
    /// </summary>
    public bool CanParse(HtmlNode root, ScraperConfiguration config) => false;

    public ParseResult Parse(HtmlNode root, ScraperConfiguration config) =>
        ParseResult.Empty("TextContentPricingParser requires plain text input, not HTML nodes.");

    /// <summary>
    /// Parses pricing from plain text content (main entry point for this parser).
    /// </summary>
    public ParseResult ParseText(string textContent, ScraperConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(textContent))
            return ParseResult.Empty("Empty text content.");

        var results = new List<ProviderPriceResult>();
        var lines = textContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var priceMatches = PricePattern().Matches(trimmed);
            if (priceMatches.Count < 1)
                continue;

            var firstDollarIdx = trimmed.IndexOf('$');
            if (firstDollarIdx <= 0)
                continue;

            var modelPart = trimmed[..firstDollarIdx].Trim();
            modelPart = ContextSizeTrailingPattern().Replace(modelPart, "").Trim();

            if (string.IsNullOrWhiteSpace(modelPart) || modelPart.Length < 3)
                continue;

            if (!ModelNameValidator.IsLikelyModelName(modelPart))
                continue;

            decimal? promptPrice = null;
            decimal? completionPrice = null;

            if (priceMatches.Count >= 2)
            {
                promptPrice = ParseMatchedPrice(priceMatches[0]);
                completionPrice = ParseMatchedPrice(priceMatches[1]);
            }
            else if (priceMatches.Count == 1)
            {
                promptPrice = ParseMatchedPrice(priceMatches[0]);
            }

            if (promptPrice is null && completionPrice is null)
                continue;

            results.Add(
                new ProviderPriceResult
                    {
                        ProviderId = config.ProviderId,
                        ModelId = modelPart,
                        ModelDisplayName = modelPart,
                        PromptPrice = promptPrice,
                        CompletionPrice = completionPrice,
                        PriceUnit = config.PriceUnit,
                        Source = "Playwright (text extraction)",
                        ScrapedAt = DateTime.UtcNow
                    });
        }

        var diagnostic = results.Count > 0
                             ? $"Text parser: extracted {results.Count} price(s) from {lines.Length} line(s)."
                             : $"Text parser: no prices found in {lines.Length} line(s).";

        return new ParseResult(results, diagnostic);
    }

    [GeneratedRegex(@"\s+\d+[KkMm]?\s*$")]
    private static partial Regex ContextSizeTrailingPattern();

    private static decimal? ParseMatchedPrice(Match match)
    {
        var raw = match.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var cleaned = raw.Replace(",", ".");
        if (decimal.TryParse(
                cleaned,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
            return value;

        return null;
    }

    [GeneratedRegex(@"\$\s*(\d+[.,]?\d*)")]
    private static partial Regex PricePattern();
}
