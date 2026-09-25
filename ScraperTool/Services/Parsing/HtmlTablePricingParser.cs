using HtmlAgilityPack;

using ScraperTool.Models;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Parses pricing data from traditional HTML tables (&lt;tr&gt;/&lt;td&gt; layout).
/// Uses two strategies:
/// 1. Fixed cell indexes (when configured via ScraperConfiguration)
/// 2. Auto-detection: finds model name in the first text-heavy cell, prices in cells containing "$"
/// </summary>
public sealed class HtmlTablePricingParser : IPricingPageParser
{
    public bool CanParse(HtmlNode root, ScraperConfiguration config)
    {
        var rows = root.SelectNodes(".//tr");
        return rows is { Count: > 0 };
    }

    public ParseResult Parse(HtmlNode root, ScraperConfiguration config)
    {
        var allRows = root.SelectNodes(".//tr");
        if (allRows is null || allRows.Count == 0)
            return ParseResult.Empty("No <tr> elements found.");

        var rows = allRows.Skip(config.RowOffset).ToList();
        if (rows.Count == 0)
            return ParseResult.Empty(
                $"Found {allRows.Count} <tr> element(s) but all skipped by RowOffset={config.RowOffset}.");

        // Try auto-detect first; fall back to fixed-index if it yields nothing
        var autoResult = ParseWithAutoDetection(rows, config);
        if (autoResult.Success)
            return autoResult;

        var fixedResult = ParseWithFixedIndexes(rows, config);
        return fixedResult;
    }

    private static string CollapseWhitespace(string s) =>
        string.IsNullOrWhiteSpace(s)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ");

    /// <summary>
    /// Extracts the first dollar price from a cell that may contain extra text like:
    /// "$0.11 (9.09M / $1)*" → 0.11
    /// </summary>
    private static decimal? ExtractDollarPrice(string cellText)
    {
        // Find "$" followed by digits
        var idx = cellText.IndexOf('$');
        if (idx < 0) return null;

        // Extract the number immediately after "$" (with optional whitespace)
        var afterDollar = cellText[(idx + 1)..].TrimStart();
        var numStr = string.Empty;

        foreach (var ch in afterDollar)
        {
            if (char.IsDigit(ch) || ch == '.' || ch == ',')
                numStr += ch;
            else
                break;
        }

        if (string.IsNullOrWhiteSpace(numStr))
            return null;

        if (decimal.TryParse(
                numStr.Replace(",", "."),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
            return value;

        return null;
    }

    /// <summary>
    /// Auto-detects model names and prices by scanning cell content:
    /// - Model name: first cell that contains text without "$" and passes model name validation
    /// - Prices: cells containing "$" — first is prompt, second is completion
    /// </summary>
    private static ParseResult ParseWithAutoDetection(
        List<HtmlNode> rows,
        ScraperConfiguration config)
    {
        var results = new List<ProviderPriceResult>();
        var skipped = 0;
        var currentSection = string.Empty;

        foreach (var row in rows)
        {
            var cells = row.SelectNodes(".//td | .//th")?.ToList();
            if (cells is null || cells.Count < 2)
            {
                skipped++;
                continue;
            }

            // Find all cells containing dollar amounts
            var priceCells = new List<(int index, string text)>();
            string? modelName = null;
            int modelCellIndex = -1;

            for (int i = 0; i < cells.Count; i++)
            {
                var cellText = HtmlEntity.DeEntitize(cells[i].InnerText).Trim();
                if (string.IsNullOrWhiteSpace(cellText))
                    continue;

                if (cellText.Contains('$'))
                {
                    priceCells.Add((i, cellText));
                }
                else if (modelName is null && cellText.Length >= 3)
                {
                    // First substantial non-price cell is the model name candidate
                    // Collapse whitespace (multiline cell content)
                    var cleaned = CollapseWhitespace(cellText);
                    modelCellIndex = i;
                    modelName = cleaned;
                }
            }

            if (modelName is null || priceCells.Count == 0)
            {
                // Could be a section header row
                if (modelName is not null && !ModelNameValidator.IsLikelyModelName(modelName))
                    currentSection = modelName;
                skipped++;
                continue;
            }

            if (!ModelNameValidator.IsLikelyModelName(modelName))
            {
                currentSection = modelName;
                skipped++;
                continue;
            }

            // Extract prices from dollar-containing cells
            decimal? promptPrice = null;
            decimal? completionPrice = null;

            if (priceCells.Count >= 2)
            {
                promptPrice = ExtractDollarPrice(priceCells[0].text);
                completionPrice = ExtractDollarPrice(priceCells[1].text);
            }
            else if (priceCells.Count == 1)
            {
                promptPrice = ExtractDollarPrice(priceCells[0].text);
            }

            if (promptPrice is null && completionPrice is null)
            {
                skipped++;
                continue;
            }

            var displayName = currentSection.Length > 0
                                  ? $"{modelName} ({currentSection})"
                                  : modelName;

            results.Add(
                new ProviderPriceResult
                    {
                        ProviderId = config.ProviderId,
                        ModelId = displayName,
                        ModelDisplayName = displayName,
                        PromptPrice = promptPrice,
                        CompletionPrice = completionPrice,
                        PriceUnit = config.PriceUnit,
                        Source = config.ApiPricingUrl,
                        ScrapedAt = DateTime.UtcNow
                    });
        }

        var diagnostic =
            $"Auto-detect: extracted {results.Count} price(s) from {rows.Count} row(s), skipped {skipped}.";
        return new ParseResult(results, diagnostic);
    }

    /// <summary>
    /// Original fixed-index approach using ModelCellIndex, PromptCellIndex, CompletionCellIndex.
    /// Used as fallback when auto-detection fails.
    /// </summary>
    private static ParseResult ParseWithFixedIndexes(
        List<HtmlNode> rows,
        ScraperConfiguration config)
    {
        var results = new List<ProviderPriceResult>();
        var skippedNoModel = 0;
        var skippedNoPrice = 0;
        var skippedTooFewCells = 0;
        var currentSection = string.Empty;

        foreach (var row in rows)
        {
            var cells = row.SelectNodes(".//td | .//th")?.ToList();
            var minCells = Math.Max(
                               config.ModelCellIndex,
                               Math.Max(config.PromptCellIndex, config.CompletionCellIndex)) + 1;

            if (cells is null || cells.Count < minCells)
            {
                skippedTooFewCells++;
                continue;
            }

            var modelName = CollapseWhitespace(
                HtmlEntity.DeEntitize(cells[config.ModelCellIndex].InnerText).Trim());
            var promptRaw = cells[config.PromptCellIndex].InnerText.Trim();
            var completionRaw = cells[config.CompletionCellIndex].InnerText.Trim();

            if (string.IsNullOrWhiteSpace(modelName))
            {
                skippedNoModel++;
                continue;
            }

            if (!ModelNameValidator.IsLikelyModelName(modelName))
            {
                currentSection = modelName;
                skippedNoModel++;
                continue;
            }

            var promptPrice = PriceParser.TryParsePrice(promptRaw);
            var completionPrice = PriceParser.TryParsePrice(completionRaw);

            if (promptPrice is null && completionPrice is null)
            {
                skippedNoPrice++;
                continue;
            }

            var displayName = currentSection.Length > 0
                                  ? $"{modelName} ({currentSection})"
                                  : modelName;

            results.Add(
                new ProviderPriceResult
                    {
                        ProviderId = config.ProviderId,
                        ModelId = displayName,
                        ModelDisplayName = displayName,
                        PromptPrice = promptPrice,
                        CompletionPrice = completionPrice,
                        PriceUnit = config.PriceUnit,
                        Source = config.ApiPricingUrl,
                        ScrapedAt = DateTime.UtcNow
                    });
        }

        var diagnostic =
            $"Fixed-index: extracted {results.Count} price(s) from {rows.Count} row(s).";
        if (skippedTooFewCells > 0 || skippedNoModel > 0 || skippedNoPrice > 0)
            diagnostic +=
                $" Skipped: {skippedTooFewCells} too-few-cells, {skippedNoModel} empty-model, {skippedNoPrice} no-parseable-price.";

        return new ParseResult(results, diagnostic);
    }
}
