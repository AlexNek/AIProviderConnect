using HtmlAgilityPack;

using ScraperTool.Models;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Parser that uses AI-determined XPath selectors from <see cref="LayoutAnalysis"/>
/// to extract prices. Only used after AI has analyzed the page structure.
/// </summary>
public sealed class AiGuidedPricingParser : IPricingPageParser
{
    private readonly LayoutAnalysis _layout;

    public AiGuidedPricingParser(LayoutAnalysis layout)
    {
        _layout = layout;
    }

    public bool CanParse(HtmlNode root, ScraperConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(_layout.RowSelector))
            return false;

        var rows = root.SelectNodes(_layout.RowSelector);
        return rows is { Count: > 0 };
    }

    public ParseResult Parse(HtmlNode root, ScraperConfiguration config)
    {
        var rows = root.SelectNodes(_layout.RowSelector);
        if (rows is null || rows.Count == 0)
            return ParseResult.Empty($"AI selector '{_layout.RowSelector}' matched no rows.");

        var results = new List<ProviderPriceResult>();
        var skipped = 0;

        foreach (var row in rows)
        {
            var modelName = ExtractText(row, _layout.ModelNameSelector);
            if (string.IsNullOrWhiteSpace(modelName)
                || !ModelNameValidator.IsLikelyModelName(modelName))
            {
                skipped++;
                continue;
            }

            var inputRaw = ExtractText(row, _layout.InputPriceSelector);
            var outputRaw = ExtractText(row, _layout.OutputPriceSelector);

            var promptPrice = PriceParser.TryParseDollarAmount(inputRaw);
            var completionPrice = PriceParser.TryParseDollarAmount(outputRaw);

            if (promptPrice is null && completionPrice is null)
            {
                skipped++;
                continue;
            }

            results.Add(
                new ProviderPriceResult
                    {
                        ProviderId = config.ProviderId,
                        ModelId = modelName,
                        ModelDisplayName = modelName,
                        PromptPrice = promptPrice,
                        CompletionPrice = completionPrice,
                        PriceUnit = config.PriceUnit,
                        Source = config.ApiPricingUrl,
                        ScrapedAt = DateTime.UtcNow
                    });
        }

        var diagnostic = $"AI-guided ({_layout.LayoutType}, confidence={_layout.Confidence:P0}): " +
                         $"extracted {results.Count} price(s) from {rows.Count} row(s), skipped {skipped}. "
                         +
                         $"Reasoning: {_layout.Reasoning}";

        return new ParseResult(results, diagnostic);
    }

    private static string? ExtractText(HtmlNode container, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return null;

        try
        {
            var node = container.SelectSingleNode(selector);
            if (node is null) return null;
            return HtmlEntity.DeEntitize(node.InnerText).Trim();
        }
        catch (Exception)
        {
            // HTML node selection failure — return null gracefully.
            return null;
        }
    }
}
