using System.Net.Http;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using ScraperTool.Services.UrlResearch.DecisionTree.Formatting;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Services.UrlResearch;

public sealed partial class DeterministicLinkScanner : IDeterministicLinkScanner
{
    private const int MaxCandidateLinks = 30;
    private const int MaxDescriptionLength = 120;

    private readonly IWebContentFetcher? _fetcher;

    private readonly HttpClient _http;

    private readonly IHtmlTagCleaner _htmlTagCleaner;

    private readonly ILogger<DeterministicLinkScanner> _logger;

    public DeterministicLinkScanner(
        HttpClient http,
        ILogger<DeterministicLinkScanner> logger,
        IHtmlTagCleaner htmlTagCleaner,
        IWebContentFetcher? fetcher = null)
    {
        _http = http;
        _logger = logger;
        _htmlTagCleaner = htmlTagCleaner ?? throw new ArgumentNullException(nameof(htmlTagCleaner));
        _fetcher = fetcher;
    }

    public sealed record LinkInfo(string Url, string Description);

    public async Task<ScanResult> ScanAsync(string rootUrl, CancellationToken ct = default)
    {
        _logger.LogDebug("Scanning root domain for links: {Url}", rootUrl);

        string html;

        // Prefer the browser fetcher (Playwright) when available — modern sites are often
        // JS-rendered, and plain HttpClient only sees the initial HTML shell. The browser
        // fetcher gets the full rendered content, including dynamically loaded navigation
        // links that are critical for finding pricing/documentation pages.
        if (_fetcher is not null)
        {
            _logger.LogDebug("Using browser fetcher for rendered content");
            var fetched = await _fetcher.FetchAsAsync(
                rootUrl,
                EContentFormat.Html,
                maxContentLength: null,
                ESanitizeLevel.Strict,
                ct: ct);
            if (fetched is not null && fetched.Success && !string.IsNullOrWhiteSpace(fetched.Content))
            {
                html = fetched.Content;
            }
            else
            {
                _logger.LogDebug("Browser fetcher failed — falling back to plain HTTP");
                try
                {
                    html = await _http.GetStringAsync(rootUrl, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Plain HTTP also failed: {Error}", ex.Message);
                    return new ScanResult(
                        [],
                        $"Failed to fetch website HTML: browser fetcher failed and plain HTTP got {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        else
        {
            // No browser fetcher available — use plain HTTP
            try
            {
                html = await _http.GetStringAsync(rootUrl, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to fetch root domain HTML: {Error}", ex.Message);
                return new ScanResult(
                    [],
                    $"Failed to fetch website HTML: {ex.GetType().Name}: {ex.Message}");
            }
        }

        var matches = AnchorHrefRegex().Matches(html);
        var links = new List<LinkInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in matches)
        {
            var href = m.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(href))
                continue;

            var url = href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                      href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                          ? href
                          : href.StartsWith("//")
                              ? new Uri(rootUrl).Scheme + ":" + href
                              : href.StartsWith('/')
                                  ? new Uri(new Uri(rootUrl), href).ToString()
                                  : null;

            if (url is null || url.TrimEnd('/').Equals(
                    rootUrl.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
                continue;

            if (!seen.Add(url))
                continue;

            var description = CleanText(m.Groups[2].Value);
            if (string.IsNullOrWhiteSpace(description))
                description = url;

            links.Add(new LinkInfo(url, description));
        }

        _logger.LogDebug("Found {Count} raw hrefs on root page", matches.Count);

        if (links.Count == 0)
        {
            if (matches.Count == 0)
                return new ScanResult(
                        [],
                    "Website loaded but HTML contains no links (<a href=...>)",
                    matches.Count);
            return new ScanResult(
                    [],
                $"Found {matches.Count} links but none were absolute/valid URLs",
                matches.Count);
        }

        var batch = links.Take(MaxCandidateLinks).ToList();

        _logger.LogDebug("Found {Count} absolute links on root domain", batch.Count);

        return new ScanResult(batch.AsReadOnly(), null, matches.Count);
    }

    public sealed record ScanResult(
        IReadOnlyList<LinkInfo> Links,
        string? Error,
        int RawHrefCount = 0);

    [GeneratedRegex(
        @"<a[^>]+href\s*=\s*""([^""]*)""[^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorHrefRegex();

    private string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = _htmlTagCleaner.Clean(text);

        return text.Length > MaxDescriptionLength ? text[..MaxDescriptionLength] + "..." : text;
    }
}
