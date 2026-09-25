using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Fetches known-good sibling field pages (e.g. apiPricingUrl, documentationUrl)
/// and extracts URLs from their content via regex. Unlike <see cref="ScanProviderLinksAction"/>
/// which only finds <c>&lt;a href&gt;</c> links, this action discovers URLs embedded in
/// code examples, curl commands, and documentation text — the typical location of API
/// base URLs on provider pages.
/// <para>
/// A page is read as rendered Markdown first and as the served document when nothing rendered:
/// the call example this scan exists to find is plain text in that document, so a browser that
/// times out must not end the search. Pages that could not be read at all are named in the
/// outcome, because "no URLs extracted" is a statement about the documentation and must not be
/// reported when the documentation was never opened.
/// </para>
/// </summary>
public sealed partial class ScanSiblingContentAction : IDecisionAction
{
    private const int MaxExtractedUrls = 20;

    private readonly IWebContentFetcher _fetcher;
    private readonly HttpClient _http;
    private readonly ProviderResearchCache _cache;

    public string Key => "scanSiblingContent";

    public ScanSiblingContentAction(
        IWebContentFetcher fetcher,
        HttpClient http,
        ProviderResearchCache cache)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseSiblingUrls(context.TemplateParameters, out var siblingUrls))
        {
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["scanResult"] = "no-sibling-urls" },
                DecisionActionStatus.Success);
        }

        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Exclude URLs already in the candidate queue (from homepage link scanning)
        foreach (var item in context.Data.GetAll())
        {
            if (!string.IsNullOrWhiteSpace(item.Content))
                seenUrls.Add(item.Content);
        }

        var evidence = new List<DecisionData>();
        var unreadable = new List<string>();
        var providerHosts = QueryDocumentedModelsEndpointAction.CollectProviderHosts(context.TemplateParameters);

        // An endpoint this batch already measured as a model list is the best candidate the
        // field can be given: a provider-owned URL that answered with data, sitting under the
        // base the field has to name. It is queued before any page is read so the extraction
        // cap can never crowd it out, and a second tree stops re-searching the same
        // documentation for an answer the batch already proved.
        foreach (var measuredUrl in ReadMeasuredEndpoints(context, providerHosts))
        {
            if (seenUrls.Add(measuredUrl))
            {
                evidence.Add(new DecisionData
                {
                    Id = $"sibling-content-{evidence.Count}-{Guid.NewGuid():N}",
                    Source = context.TemplateParameters.GetValueOrDefault("providerUrl") ?? "batch",
                    Type = "CandidateLink",
                    Content = measuredUrl,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ActionId = context.NodeId,
                    Metadata = new Dictionary<string, string>
                    {
                        ["description"] = "Measured as a model list earlier in this batch"
                    }
                });
            }
        }

        foreach (var (fieldKey, url) in siblingUrls)
        {
            if (evidence.Count >= MaxExtractedUrls)
                break;

            var (text, readError) = await ReadSiblingPageAsync(url, cancellationToken);
            if (text is null)
            {
                unreadable.Add($"{url}: {readError}");
                continue;
            }

            foreach (var extractedUrl in OrderProbeCandidatesFirst(text, ExtractUrlsFromContent(text), providerHosts))
            {
                if (evidence.Count >= MaxExtractedUrls)
                    break;

                if (seenUrls.Add(extractedUrl))
                {
                    evidence.Add(new DecisionData
                    {
                        Id = $"sibling-content-{evidence.Count}-{Guid.NewGuid():N}",
                        Source = url,
                        Type = "CandidateLink",
                        Content = extractedUrl,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ActionId = context.NodeId,
                        Metadata = new Dictionary<string, string>
                        {
                            ["description"] = $"Extracted from {fieldKey} page content"
                        }
                    });
                }
            }
        }

        if (unreadable.Count > 0)
            context.State.Properties["unreadableSiblingPages"] = string.Join(", ", unreadable);

        if (evidence.Count == 0)
        {
            // Which of these two it is decides what the reader may conclude: a documentation page
            // that was opened and held no URLs is a fact about the provider, while a page that
            // could not be opened is a gap in this run's evidence. Both keep routing to
            // has-sibling-candidates, so the tree moves on to web-search either way.
            var nothingRead = unreadable.Count == siblingUrls.Count;
            var properties = new Dictionary<string, string>
            {
                ["scanResult"] = nothingRead ? "documentation-unreadable" : "no-urls-extracted"
            };
            if (unreadable.Count > 0)
                properties["unreadablePages"] = unreadable.Count.ToString();

            return new DecisionActionResult(null, properties, DecisionActionStatus.Success);
        }

        // Prioritize API-like URLs (e.g. https://api.example.com/v1) before
        // documentation-like URLs so the decision tree probes actual API
        // endpoints first instead of wasting its budget on documentation pages
        // that will always fail the probe.
        var prioritized = evidence
            .Select((item, index) => (item, index, isApi: IsApiLikeUrl(item.Content)))
            .OrderByDescending(x => x.isApi)
            .ThenBy(x => x.index)
            .Select(x => x.item)
            .ToList();

        context.State.Properties["candidateCount"] = prioritized.Count;
        context.State.Properties["candidateIndex"] = 0;

        var scanProperties = new Dictionary<string, string>
        {
            ["scanResult"] = "success",
            ["extractedUrlCount"] = prioritized.Count.ToString()
        };
        if (unreadable.Count > 0)
            scanProperties["unreadablePages"] = unreadable.Count.ToString();

        return new DecisionActionResult(prioritized, scanProperties, DecisionActionStatus.Success);
    }

    /// <summary>
    /// The URLs another tree in this batch measured as a model list for this provider. They are
    /// re-checked against the provider's own hosts rather than trusted on the cache key alone:
    /// a candidate list feeds a suggestion, and a suggestion on someone else's host is a wrong
    /// answer written to a catalogue.
    /// </summary>
    private IEnumerable<string> ReadMeasuredEndpoints(
        DecisionActionContext context,
        IReadOnlySet<string> providerHosts)
    {
        if (!context.TemplateParameters.TryGetValue("providerUrl", out var providerUrl)
            || string.IsNullOrWhiteSpace(providerUrl))
        {
            return [];
        }

        return _cache.GetCatalogEndpoints(providerUrl)
            .Where(url => QueryDocumentedModelsEndpointAction.IsProbeableEndpoint(url, providerHosts))
            .ToList();
    }

    /// <summary>
    /// Orders a page's URLs by what the field can do with them: an endpoint on the provider's own
    /// host, then the provider's own pages (which state their own base), then someone else's API
    /// host quoted on the page, then a third-party page. Inside each group a URL printed in a code
    /// sample leads. The cap is applied while candidates are collected, so without this ordering
    /// the first twenty addresses on a page — language alternates, navigation, build assets — push
    /// out the one curl example that names the base, and the tree never probes it.
    /// <para>
    /// Nothing is discarded here. Two catalogue entries serve their API from a host none of their
    /// other URLs touch (<c>gemini</c>, <c>github-models</c>), and eight are local addresses, so a
    /// provider-host test may rank a candidate but must never veto one.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> OrderProbeCandidatesFirst(
        string pageText,
        IReadOnlyList<string> urls,
        IReadOnlySet<string> providerHosts)
    {
        var codeText = QueryDocumentedModelsEndpointAction.ExtractCodeSpans(pageText);

        // OrderBy is stable, so document order is kept inside each group.
        return urls
            .OrderBy(url => CandidateRank(url, providerHosts))
            .ThenBy(url => codeText.Contains(url, StringComparison.Ordinal) ? 0 : 1)
            .ToList();
    }

    /// <summary>
    /// What a candidate is worth to the base-URL field, lowest first. The provider's own host
    /// outranks an endpoint-shaped URL on somebody else's: a documentation page that quotes an
    /// upstream API host otherwise gets probed, answers, and is offered as this provider's base —
    /// which is a different service under the provider's name.
    /// </summary>
    private static int CandidateRank(string url, IReadOnlySet<string> providerHosts)
    {
        var isEndpoint = IsApiLikeUrl(url);

        return (isEndpoint, IsOnProviderHost(url, providerHosts)) switch
        {
            (true, true) => 0,
            (false, true) => 1,
            (true, false) => 2,
            _ => 3
        };
    }

    private static bool IsOnProviderHost(string url, IReadOnlySet<string> providerHosts) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && QueryDocumentedModelsEndpointAction.BelongsToHost(uri.Host, providerHosts);

    /// <summary>
    /// Reads one sibling page for URL extraction: rendered Markdown first, then the served
    /// document itself when nothing rendered. Returns the text to scan, or the reason neither
    /// read produced any.
    /// </summary>
    private async Task<(string? Text, string? Error)> ReadSiblingPageAsync(
        string url,
        CancellationToken ct)
    {
        // A page something in this batch already rendered is not rendered again. The tree reads
        // the sibling pages a second time once its first candidate pool is used up, and a browser
        // load per page per pass is exactly what the time budget is spent on.
        var cached = _cache.GetPageFetch(url);
        if (cached is { Success: true } && !string.IsNullOrWhiteSpace(cached.MarkdownContent))
            return (cached.MarkdownContent, null);

        string? renderError = null;

        try
        {
            var content = await _fetcher.FetchAsAsync(url, EContentFormat.Markdown, ct: ct);
            if (content.Success && !string.IsNullOrWhiteSpace(content.Content))
            {
                _cache.SetPageFetch(url, new PageFetchCacheEntry
                {
                    MarkdownContent = content.Content,
                    HtmlContent = null,
                    ErrorMessage = content.ErrorMessage,
                    FinalUrl = content.FinalUrl,
                    Success = true
                });

                return (content.Content, null);
            }

            renderError = string.IsNullOrWhiteSpace(content.ErrorMessage)
                ? "the page rendered no content"
                : content.ErrorMessage;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            renderError = ex.Message;
        }

        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return (null, $"{renderError}; plain read: HTTP {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
                return (null, $"{renderError}; plain read: empty response body");

            return (PrioritizeCodeElements(body), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, $"{renderError}; plain read: {ex.Message}");
        }
    }

    /// <summary>
    /// Moves the text inside <c>&lt;pre&gt;</c> and <c>&lt;code&gt;</c> elements to the front of a
    /// served document. The documented call example lives in a code element, while the first URLs
    /// in raw HTML are navigation, hreflang alternates and build assets — and extraction stops at
    /// <see cref="MaxExtractedUrls"/>, so the order of the text decides what survives that cap.
    /// </summary>
    internal static string PrioritizeCodeElements(string html)
    {
        var code = new StringBuilder();
        foreach (Match match in PreformattedCodeRegex().Matches(html))
            code.AppendLine(match.Groups[1].Value);
        foreach (Match match in InlineCodeElementRegex().Matches(html))
            code.AppendLine(match.Groups[1].Value);

        return code.Length == 0
            ? html
            : string.Concat(code.ToString(), Environment.NewLine, html);
    }

    /// <summary>
    /// Returns true when the URL looks like an API base or endpoint
    /// (e.g. https://api.example.com, https://api.example.com/v1/chat).
    /// These URLs should be probed before documentation-like URLs.
    /// </summary>
    internal static bool IsApiLikeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // API subdomain patterns: api.example.com, api-example.com
        if (uri.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase)
            || uri.Host.StartsWith("api-", StringComparison.OrdinalIgnoreCase))
            return true;

        // Versioned API path: /v1/, /v1beta/, or a base that ends at the version segment
        // (…/zen/go/v1). A gateway base carries a prefix, so neither the host nor a trailing
        // slash identifies it — and a candidate that is not recognised here gets fetched as a
        // web page, which discards a correct base before the probe can test it.
        return VersionedPathRegex().IsMatch(uri.AbsolutePath);
    }

    /// <summary>
    /// Extracts absolute HTTP URLs from markdown/text content using regex.
    /// Finds URLs in code blocks, curl examples, and documentation text.
    /// </summary>
    internal static IReadOnlyList<string> ExtractUrlsFromContent(string content)
    {
        var matches = UrlExtractionRegex().Matches(content);
        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            var url = match.Value;

            // Strip trailing punctuation that's not part of the URL
            url = url.TrimEnd(')', '>', ',', ';', '.', '!', '?', '\'', '"', '`', '*');

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == "http" || uri.Scheme == "https")
                && seen.Add(url))
            {
                urls.Add(url);
            }
        }

        return urls;
    }

    /// <summary>
    /// Parses the siblingUrls template parameter (format: "field1=url1;field2=url2")
    /// into a list of field name → URL pairs.
    /// </summary>
    private static bool TryParseSiblingUrls(
        IReadOnlyDictionary<string, string> templateParameters,
        out IReadOnlyList<KeyValuePair<string, string>> siblingUrls)
    {
        siblingUrls = new List<KeyValuePair<string, string>>();

        if (!templateParameters.TryGetValue("siblingUrls", out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var segment in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex >= segment.Length - 1)
                continue;

            var fieldKey = segment[..equalsIndex].Trim();
            var url = segment[(equalsIndex + 1)..].Trim();

            if (!string.IsNullOrWhiteSpace(url))
            {
                pairs.Add(new KeyValuePair<string, string>(fieldKey, url));
            }
        }

        siblingUrls = pairs;
        return pairs.Count > 0;
    }

    [GeneratedRegex(@"https?://[^\s<>""'`)+\]}>]+")]
    private static partial Regex UrlExtractionRegex();

    [GeneratedRegex(@"/v\d+[a-z]*(?:/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionedPathRegex();

    [GeneratedRegex(@"<pre\b[^>]*>(.*?)</pre>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PreformattedCodeRegex();

    [GeneratedRegex(@"<code\b[^>]*>(.*?)</code>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InlineCodeElementRegex();
}
