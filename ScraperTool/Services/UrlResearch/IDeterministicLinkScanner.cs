namespace ScraperTool.Services.UrlResearch;

/// <summary>
/// Scans a website's root page for candidate links using deterministic regex-based extraction.
/// </summary>
public interface IDeterministicLinkScanner
{
    /// <summary>
    /// Scans the given root URL for absolute links and returns a bounded set of candidates.
    /// </summary>
    /// <param name="rootUrl">The root URL to scan.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A scan result containing discovered links and optional error information.</returns>
    Task<DeterministicLinkScanner.ScanResult> ScanAsync(string rootUrl, CancellationToken ct = default);
}
