namespace ScraperTool.Services.UrlResearch.DecisionTree;

/// <summary>
/// Cached result of a candidate page fetch (markdown + HTML content).
/// Stores the raw HTTP response so that region selection can run per-tree
/// without re-fetching the page.
/// </summary>
public sealed class PageFetchCacheEntry
{
    public string? MarkdownContent { get; init; }

    public string? HtmlContent { get; init; }

    public string? ErrorMessage { get; init; }

    public string? FinalUrl { get; init; }

    public bool Success { get; init; }
}
