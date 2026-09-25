using HtmlAgilityPack;

using ScraperTool.Models;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Parses pricing data from div-based layouts commonly used by modern pricing pages.
/// Handles patterns like:
///   - div[role="listitem"] containers with child divs for model name and prices
///   - Divs with CSS classes containing "price", "input", "output", "model" etc.
/// </summary>
public sealed class DivLayoutPricingParser : IPricingPageParser
{
    // Selectors for input/prompt price within a row
    private static readonly string[] InputPriceSelectors =
        [
            ".//*[contains(@class,'input-table') or contains(@class,'input-price') or contains(@id,'input')]",
            ".//*[contains(@class,'prompt-price')]",
        ];

    // Selectors for model name within a row
    private static readonly string[] ModelNameSelectors =
        [
            ".//*[contains(@class,'cmsfilter-field')]",
            ".//*[contains(@class,'model-name') or contains(@class,'model_name')]",
            ".//*[contains(@class,'heading-card')]//*[contains(@class,'headline')]",
            ".//*[contains(@class,'heading-card')]//div[contains(@class,'sort-txt')]",
            ".//a[contains(@class,'price-column') or contains(@class,'newprice-column')]//div[contains(@class,'headline')]",
            ".//a[contains(@href,'/models/')]//div[last()]",
        ];

    // Selectors for output/completion price within a row
    private static readonly string[] OutputPriceSelectors =
        [
            ".//*[contains(@class,'output-table') or contains(@class,'output-price') or contains(@id,'output')]",
            ".//*[contains(@class,'completion-price')]",
        ];

    // Common XPath selectors for div-based pricing rows (tried in order)
    private static readonly string[] RowSelectors =
        [
            ".//*[@role='listitem']",
            ".//*[contains(@class,'price-row') or contains(@class,'pricing-row')]",
            ".//*[contains(@class,'total-table-price')]",
            ".//*[contains(@class,'model-row') or contains(@class,'model-item')]",
        ];

    public bool CanParse(HtmlNode root, ScraperConfiguration config)
    {
        foreach (var selector in RowSelectors)
        {
            var nodes = root.SelectNodes(selector);
            if (nodes is { Count: >= 3 })
                return true;
        }

        return false;
    }

    public ParseResult Parse(HtmlNode root, ScraperConfiguration config)
    {
        // Find which row selector works
        HtmlNodeCollection? rows = null;
        string usedSelector = string.Empty;

        foreach (var selector in RowSelectors)
        {
            rows = root.SelectNodes(selector);
            if (rows is { Count: >= 3 })
            {
                usedSelector = selector;
                break;
            }
        }

        if (rows is null || rows.Count == 0)
            return ParseResult.Empty("No div-based pricing rows found.");

        var results = new List<ProviderPriceResult>();
        var skipped = 0;

        foreach (var row in rows)
        {
            var modelName = ExtractFirst(row, ModelNameSelectors);
            if (string.IsNullOrWhiteSpace(modelName)
                || !ModelNameValidator.IsLikelyModelName(modelName))
            {
                skipped++;
                continue;
            }

            var inputRaw = ExtractFirst(row, InputPriceSelectors);
            var outputRaw = ExtractFirst(row, OutputPriceSelectors);

            var promptPrice = PriceParser.TryParsePrice(inputRaw);
            var completionPrice = PriceParser.TryParsePrice(outputRaw);

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

        var diagnostic =
            $"Div layout ({usedSelector}): extracted {results.Count} price(s) from {rows.Count} item(s), skipped {skipped}.";
        return new ParseResult(results, diagnostic);
    }

    private static string? ExtractFirst(HtmlNode container, string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var node = container.SelectSingleNode(selector);
            if (node is not null)
            {
                var text = HtmlEntity.DeEntitize(node.InnerText).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }

        return null;
    }
}
