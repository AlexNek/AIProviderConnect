namespace ScraperTool.Models;

/// <summary>
/// Result of a scrape operation, including diagnostic information about what happened.
/// </summary>
public sealed class ScrapeResult
{
    /// <summary>
    /// Human-readable diagnostic explaining what the scraper found (or why it didn't find prices).
    /// Always populated regardless of success or failure.
    /// </summary>
    public string Diagnostic { get; }

    public bool HasPrices => Prices.Count > 0;

    public IReadOnlyList<ProviderPriceResult> Prices { get; }

    public ScrapeResult(IReadOnlyList<ProviderPriceResult> prices, string diagnostic)
    {
        Prices = prices;
        Diagnostic = diagnostic;
    }

    public static ScrapeResult Empty(string diagnostic) =>
        new(Array.Empty<ProviderPriceResult>(), diagnostic);
}
