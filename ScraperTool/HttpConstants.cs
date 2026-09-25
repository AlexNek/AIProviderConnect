namespace ScraperTool;

/// <summary>
/// Constants for HTTP headers and user agent strings.
/// Eliminates magic strings throughout the application.
/// </summary>
public static class HttpConstants
{
    /// <summary>
    /// Accept header value for HTML content.
    /// </summary>
    public const string AcceptHtml = "text/html,application/xhtml+xml";

    /// <summary>
    /// Named HTTP client for AI provider API calls (no auto-redirect).
    /// </summary>
    public const string AiApiHttpClientName = "AiApiClient";

    /// <summary>
    /// Named HTTP client configuration for scraper operations.
    /// </summary>
    public const string ScraperHttpClientName = "ScraperTool";

    /// <summary>
    /// User-Agent header value for all HTTP requests.
    /// </summary>
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36";
}
