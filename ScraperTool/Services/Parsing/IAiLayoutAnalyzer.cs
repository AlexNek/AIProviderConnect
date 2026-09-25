namespace ScraperTool.Services.Parsing;

/// <summary>
/// Uses AI to analyze a pricing page's HTML structure and determine the correct
/// selectors for extracting model names and prices.
/// </summary>
public interface IAiLayoutAnalyzer
{
    /// <summary>
    /// Analyzes a pricing page HTML snippet and returns selectors for parsing.
    /// </summary>
    /// <param name="htmlSnippet">A representative portion of the page HTML (first few KB).</param>
    /// <param name="providerId">The provider ID for context.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<LayoutAnalysis?> AnalyzeAsync(
        string htmlSnippet,
        string providerId,
        CancellationToken ct = default);
}
