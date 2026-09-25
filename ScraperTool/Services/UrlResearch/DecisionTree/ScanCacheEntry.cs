namespace ScraperTool.Services.UrlResearch.DecisionTree;

/// <summary>
/// Cached result of a provider website link scan.
/// Stores the raw discovered links before field-specific sorting.
/// </summary>
public sealed class ScanCacheEntry
{
    public IReadOnlyList<(string Url, string Description)> Links { get; init; } = [];

    public string? Error { get; init; }
}
