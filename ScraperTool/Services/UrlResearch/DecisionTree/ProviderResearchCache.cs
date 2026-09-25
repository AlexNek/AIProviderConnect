namespace ScraperTool.Services.UrlResearch.DecisionTree;

/// <summary>
/// Batch-scoped cache that eliminates redundant HTTP calls when multiple decision
/// trees research different fields for the same provider, and carries a measured
/// fact forward so a later tree does not re-search for an answer the batch proved.
/// Cleared at the start of each batch by <c>AiUrlFixService</c>.
/// </summary>
public sealed class ProviderResearchCache
{
    private readonly Dictionary<string, ScanCacheEntry> _scanCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PageFetchCacheEntry> _pageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _catalogEndpoints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Removes all cached scan, page-fetch and measured-endpoint entries.
    /// Called at the start of each batch to ensure fresh data.
    /// </summary>
    public void Clear()
    {
        _scanCache.Clear();
        _pageCache.Clear();
        _catalogEndpoints.Clear();
    }

    /// <summary>
    /// Returns the cached scan result for the given provider URL, or null if not cached.
    /// </summary>
    public ScanCacheEntry? GetScan(string providerUrl)
    {
        return _scanCache.TryGetValue(providerUrl, out var entry) ? entry : null;
    }

    /// <summary>
    /// Stores a scan result keyed by the provider URL.
    /// </summary>
    public void SetScan(string providerUrl, ScanCacheEntry entry)
    {
        _scanCache[providerUrl] = entry;
    }

    /// <summary>
    /// Returns the cached page-fetch result for the given candidate URL, or null if not cached.
    /// </summary>
    public PageFetchCacheEntry? GetPageFetch(string url)
    {
        return _pageCache.TryGetValue(url, out var entry) ? entry : null;
    }

    /// <summary>
    /// Stores a page-fetch result keyed by the candidate URL.
    /// </summary>
    public void SetPageFetch(string url, PageFetchCacheEntry entry)
    {
        _pageCache[url] = entry;
    }

    /// <summary>
    /// Records a URL that answered this batch with a real model list, under the provider URL it
    /// belongs to. More than one field depends on that fact: the counting tree measures the
    /// endpoint, and the base-URL tree is the field that has to name it. Without the record the
    /// measurement dies with the tree that made it and the second tree re-searches for an answer
    /// the batch already holds.
    /// </summary>
    public void RecordCatalogEndpoint(string providerUrl, string endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(providerUrl) || string.IsNullOrWhiteSpace(endpointUrl))
            return;

        if (!_catalogEndpoints.TryGetValue(providerUrl, out var endpoints))
        {
            endpoints = [];
            _catalogEndpoints[providerUrl] = endpoints;
        }

        if (!endpoints.Contains(endpointUrl, StringComparer.OrdinalIgnoreCase))
            endpoints.Add(endpointUrl);
    }

    /// <summary>
    /// Returns the endpoints measured as model lists for this provider earlier in the batch.
    /// </summary>
    public IReadOnlyList<string> GetCatalogEndpoints(string providerUrl)
    {
        if (string.IsNullOrWhiteSpace(providerUrl)
            || !_catalogEndpoints.TryGetValue(providerUrl, out var endpoints))
        {
            return [];
        }

        return endpoints;
    }
}
