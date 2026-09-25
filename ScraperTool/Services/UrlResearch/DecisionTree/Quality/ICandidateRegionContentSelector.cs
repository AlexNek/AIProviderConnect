namespace ScraperTool.Services.UrlResearch.DecisionTree.Quality;

/// <summary>
/// Selects the best content for LLM consumption by analyzing HTML candidate regions
/// and falling back to full Markdown when regions are low quality.
/// </summary>
public interface ICandidateRegionContentSelector
{
    /// <summary>
    /// Analyzes HTML content for meaningful candidate regions and returns the best
    /// available content for LLM classification. Falls back to the full Markdown
    /// when regions are missing or dominated by UI chrome.
    /// </summary>
    /// <param name="markdownContent">The full Markdown content (used as fallback).</param>
    /// <param name="htmlContent">The raw HTML content to analyze, or null/empty if unavailable.</param>
    /// <param name="sourceUri">The source URI for the HTML analyzer.</param>
    /// <returns>The selected content and the total number of candidate regions found.</returns>
    CandidateRegionContentResult Select(string markdownContent, string? htmlContent, Uri? sourceUri);
}
