using ScraperTool.Models;

namespace ScraperTool.Services.Parsing;

/// <summary>
/// Result of a pricing page parse attempt.
/// </summary>
public sealed class ParseResult
{
    public string Diagnostic { get; }

    public IReadOnlyList<ProviderPriceResult> Prices { get; }

    public bool Success => Prices.Count > 0;

    public ParseResult(IReadOnlyList<ProviderPriceResult> prices, string diagnostic)
    {
        Prices = prices;
        Diagnostic = diagnostic;
    }

    public static ParseResult Empty(string diagnostic) => new([], diagnostic);
}
