using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Counts models from a models endpoint the provider documents on its own pages.
/// <para>
/// The stored <c>baseUrl</c> is frequently not where the catalog lives: a gateway can keep
/// its public model list on the site host (<c>https://provider.example/zen/go/v1/models</c>)
/// while the chat base points somewhere else, and a wrong or keyless base answers with an
/// error body instead of JSON. Providers that do expose a catalog state its URL in the
/// integration documentation, so this action reads that documentation, takes the URLs it
/// literally prints there, and asks each one. The reply is a JSON array, so the count needs
/// no LLM and no excerpting — it is the same authoritative source the model list comes from.
/// </para>
/// <para>
/// Produces ModelList evidence and stores <c>modelCount</c> for the comparison step.
/// Finding no documented catalog is reported as a completed step, not a failed action: the
/// tree routes on <c>hasDocumentedModelCount</c>, so an ordinary "this provider publishes no
/// model list" answer does not mark the whole research run unsuccessful.
/// </para>
/// <para>
/// A page this run could not open is named in the outcome instead of being dropped from the
/// count, so "nothing is documented" is only ever concluded from pages that were actually
/// read — a failed read is a gap in this run's evidence, not a fact about the provider.
/// </para>
/// </summary>
public sealed partial class QueryDocumentedModelsEndpointAction : IDecisionAction
{
    private const int MaxDocumentationPages = 2;
    private const int MaxEndpointProbes = 5;
    private const int MaxEndpointIdsInSummary = 10;

    /// <summary>
    /// Template parameters that always hold a provider-owned URL, and so can be used to
    /// recognise which hosts the provider controls.
    /// </summary>
    private static readonly string[] ProviderUrlParameterKeys =
    [
        "providerUrl", "baseUrl", "currentValue", "modelsPageUrl", "redirectTargetUrl"
    ];

    /// <summary>
    /// Path extensions that are a file to download rather than an endpoint to query.
    /// </summary>
    private static readonly HashSet<string> StaticAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "svg", "ico", "webp", "avif",
        "css", "js", "mjs", "woff", "woff2", "ttf",
        "zip", "tar", "gz", "dmg", "exe",
        "pdf", "mp4", "webm", "mov"
    };

    private readonly HttpClient _http;
    private readonly IWebContentFetcher _fetcher;
    private readonly ProviderResearchCache _cache;

    public string Key => "queryDocumentedModelsEndpoint";

    public QueryDocumentedModelsEndpointAction(
        HttpClient http,
        IWebContentFetcher fetcher,
        ProviderResearchCache cache)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        // Runs once per tree execution: every probe already failed, so a second pass over
        // the same documentation would only repeat it and burn the node budget.
        if (context.State.Properties.ContainsKey("documentedModelsAttempted"))
        {
            return Report(context, "already-attempted", "Documented models endpoints were already probed in this run.");
        }

        context.State.Properties["documentedModelsAttempted"] = true;

        if (!TryGetDocumentationUrls(context.TemplateParameters, out var documentationUrls)
            && context.TemplateParameters.TryGetValue("providerUrl", out var providerUrl)
            && !string.IsNullOrWhiteSpace(providerUrl))
        {
            documentationUrls = [providerUrl];
        }

        if (documentationUrls.Count == 0)
        {
            return Report(context, "no-documentation", "No documentation page to read a models endpoint from.");
        }

        // Probe as each page is read rather than collecting from every page first: the
        // integration page usually states the catalog, and a browser fetch of the sibling
        // page afterwards would cost ~10s for nothing.
        var providerHosts = CollectProviderHosts(context.TemplateParameters);
        var probed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        var unreadable = new List<string>();
        var pagesRead = 0;

        foreach (var pageUrl in documentationUrls)
        {
            if (pagesRead >= MaxDocumentationPages || probed.Count >= MaxEndpointProbes)
                break;

            var (text, readError) = await ReadPageForEndpointScanAsync(pageUrl, cancellationToken);
            if (text is null)
            {
                unreadable.Add($"{pageUrl} ({readError})");
                continue;
            }

            pagesRead++;

            foreach (var endpoint in ExtractCandidateEndpoints(text, providerHosts))
            {
                if (probed.Count >= MaxEndpointProbes || !probed.Add(endpoint))
                    continue;

                var (count, modelIds, error) = await ProbeEndpointAsync(endpoint, cancellationToken);
                if (count > 0)
                    return CreateSuccessResult(context, count, modelIds, endpoint, failures);

                failures.Add($"{endpoint}: {error}");
            }
        }

        if (probed.Count == 0)
            return ReportNothingCounted(context, pagesRead, unreadable);

        var probedProperties = WithUnreadablePages(new Dictionary<string, string>
        {
            ["probedCount"] = probed.Count.ToString()
        }, unreadable, pagesRead);

        return Report(
            context,
            "no-documented-endpoint-responded",
            $"Probed {probed.Count} documented URL(s), none returned a model list — {string.Join(" | ", failures.Take(MaxEndpointProbes))}"
            + DescribeUnreadable(unreadable),
            probedProperties);
    }

    /// <summary>
    /// Explains why no count came out of the documentation. A page that could not be read at
    /// all is kept separate from a page that was read and stated no endpoint, because the two
    /// say different things about the provider and lead the tree to different work.
    /// </summary>
    private static DecisionActionResult ReportNothingCounted(
        DecisionActionContext context,
        int pagesRead,
        IReadOnlyList<string> unreadable)
    {
        var properties = WithUnreadablePages(new Dictionary<string, string>(), unreadable, pagesRead);

        if (pagesRead == 0)
        {
            return Report(
                context,
                "documentation-unreadable",
                $"Could not read any documentation page — {string.Join(" | ", unreadable)}",
                properties);
        }

        if (unreadable.Count > 0)
        {
            return Report(
                context,
                "documentation-partial",
                $"No URL stated on the {pagesRead} documentation page(s) read addresses"
                + " an endpoint on this provider's own hosts; that covers only those pages,"
                + $" and {unreadable.Count} page(s) could not be read — {string.Join(" | ", unreadable)}",
                properties);
        }

        return Report(
            context,
            "no-documented-endpoint",
            $"No URL stated on the {pagesRead} documentation page(s) read addresses"
            + " an endpoint on this provider's own hosts.",
            properties);
    }

    private static Dictionary<string, string> WithUnreadablePages(
        Dictionary<string, string> properties,
        IReadOnlyList<string> unreadable,
        int pagesRead)
    {
        properties["documentationPagesRead"] = pagesRead.ToString();
        if (unreadable.Count > 0)
            properties["documentationPagesUnreadable"] = string.Join(" | ", unreadable);

        return properties;
    }

    private static string DescribeUnreadable(IReadOnlyList<string> unreadable)
        => unreadable.Count == 0
            ? string.Empty
            : $" — note: {unreadable.Count} further documentation page(s) could not be read"
              + $" ({string.Join(" | ", unreadable)})";

    /// <summary>
    /// Reports what the probe found when no count came out of it, as a completed step —
    /// see the type summary for why this is not surfaced as an action failure.
    /// </summary>
    private static DecisionActionResult Report(
        DecisionActionContext context,
        string queryResult,
        string message,
        Dictionary<string, string>? extraProperties = null)
    {
        context.State.Properties["modelCountMethod"] = "not-documented";

        var properties = extraProperties is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(extraProperties);
        properties["queryResult"] = queryResult;

        return new DecisionActionResult(
            null,
            properties,
            DecisionActionStatus.Success,
            message);
    }

    /// <summary>
    /// Reads one documentation page as the text to scan for stated URLs.
    /// <para>
    /// The rendered Markdown is preferred: a browser read drops the page chrome (language
    /// alternates, navigation, canonical self-links) that an HTML source is full of, so what
    /// remains is what the page states as content. When that read yields no content at all the
    /// page is fetched plainly instead — a documentation site that renders on the server prints
    /// its endpoints in the response body, and such a page must not be written off as unread.
    /// </para>
    /// </summary>
    private async Task<(string? Text, string? Error)> ReadPageForEndpointScanAsync(
        string url,
        CancellationToken ct)
    {
        var (markdown, markdownError) = await ReadRenderedMarkdownAsync(url, ct);
        if (markdown is not null)
            return (markdown, null);

        var (body, bodyError) = await ReadPlainAsync(url, ct);
        if (body is not null)
            return (body, null);

        return (null, $"{markdownError}; {bodyError}");
    }

    /// <summary>
    /// Reads the page as Markdown, reusing the batch cache so a page another tree already
    /// fetched is not loaded twice.
    /// </summary>
    private async Task<(string? Text, string? Error)> ReadRenderedMarkdownAsync(
        string url,
        CancellationToken ct)
    {
        var cached = _cache.GetPageFetch(url);
        if (cached is { Success: true } && !string.IsNullOrWhiteSpace(cached.MarkdownContent))
            return (cached.MarkdownContent, null);

        try
        {
            var content = await _fetcher.FetchAsAsync(url, EContentFormat.Markdown, ct: ct);
            if (!content.Success || string.IsNullOrWhiteSpace(content.Content))
                return (null, content.ErrorMessage ?? "no content rendered");

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Plain HTTP GET of a page, used as link source text when nothing rendered. The text stays
    /// local to this action and is not written into the shared page cache: cache consumers treat
    /// <c>MarkdownContent</c> as rendered Markdown, and raw HTML in that slot reads as an empty
    /// page to them.
    /// </summary>
    private async Task<(string? Text, string? Error)> ReadPlainAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return (null, $"plain read: HTTP {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(body)
                ? (null, "plain read: empty response body")
                : (body, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, $"plain read: {ex.Message}");
        }
    }

    /// <summary>
    /// Picks the URLs a documentation page could be stating an API endpoint with, ordered so
    /// that the ones printed inside code samples come first — an integration guide writes a
    /// catalog URL in a <c>curl</c> example far more often than in prose. Which of them is
    /// actually the model list is decided by the reply, not by the shape of the path.
    /// </summary>
    internal static IReadOnlyList<string> ExtractCandidateEndpoints(
        string markdown,
        IReadOnlySet<string> providerHosts)
    {
        var codeText = ExtractCodeSpans(markdown);
        var endpoints = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // OrderBy is stable, so document order is kept within each of the two groups.
        foreach (var candidate in ScanSiblingContentAction.ExtractUrlsFromContent(markdown)
                     .OrderBy(url => codeText.Contains(url, StringComparison.Ordinal) ? 0 : 1))
        {
            if (IsProbeableEndpoint(candidate, providerHosts) && seen.Add(candidate))
                endpoints.Add(candidate);
        }

        return endpoints;
    }

    /// <summary>
    /// Whether a documented URL is worth asking for a model list. Judged structurally: it must
    /// be an absolute http(s) URL on a host the provider owns, and not a static download. The
    /// host test keeps a third-party catalog that the page happens to link out from being
    /// counted as this provider's; nothing here predicts what the endpoint returns.
    /// </summary>
    internal static bool IsProbeableEndpoint(string url, IReadOnlySet<string> providerHosts)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (!BelongsToHost(uri.Host, providerHosts))
            return false;

        // Extensions that are never a queryable endpoint. JSON and YAML stay in the running on
        // purpose: a hosted catalog file is a legitimate answer, and its body decides.
        var file = uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..];
        var dot = file.LastIndexOf('.');
        if (dot >= 0 && StaticAssetExtensions.Contains(file[(dot + 1)..]))
            return false;

        return true;
    }

    /// <summary>
    /// Collects the hosts the provider owns from the URL-shaped template parameters. Free-text
    /// parameters may quote unrelated pages, so only the parameters that always hold a
    /// provider URL are read.
    /// </summary>
    internal static IReadOnlySet<string> CollectProviderHosts(
        IReadOnlyDictionary<string, string> templateParameters)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in ProviderUrlParameterKeys)
        {
            if (templateParameters.TryGetValue(key, out var value))
                AddHost(hosts, value);
        }

        if (templateParameters.TryGetValue("siblingUrls", out var siblings)
            && !string.IsNullOrWhiteSpace(siblings))
        {
            foreach (var segment in siblings.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var equalsIndex = segment.IndexOf('=');
                if (equalsIndex >= 0 && equalsIndex < segment.Length - 1)
                    AddHost(hosts, segment[(equalsIndex + 1)..]);
            }
        }

        return hosts;
    }

    private static void AddHost(HashSet<string> hosts, string? value)
    {
        if (Uri.TryCreate((value ?? string.Empty).Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(uri.Host))
        {
            hosts.Add(uri.Host);
        }
    }

    /// <summary>
    /// True when the two hosts are the same or one is a subdomain of the other: a catalog on
    /// <c>api.provider.example</c> belongs to a site at <c>provider.example</c>, and a page
    /// linked as <c>www.provider.example</c> is the same host as <c>provider.example</c>.
    /// </summary>
    internal static bool BelongsToHost(string host, IReadOnlySet<string> providerHosts)
    {
        if (providerHosts.Count == 0)
            return false;

        foreach (var known in providerHosts)
        {
            if (string.Equals(host, known, StringComparison.OrdinalIgnoreCase))
                return true;

            if (host.EndsWith("." + known, StringComparison.OrdinalIgnoreCase)
                || known.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Concatenates the code samples of a page: fenced blocks and inline backtick spans for
    /// Markdown, <c>pre</c> and <c>code</c> element content for HTML. Endpoints are stated
    /// literally there, so a URL found in this text is a stronger candidate than one mentioned
    /// in prose — and on a page read as HTML, the code elements are the only part that carries
    /// the page's own content rather than its navigation and language alternates.
    /// </summary>
    internal static string ExtractCodeSpans(string source)
    {
        var builder = new StringBuilder();
        var inFence = false;

        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal)
                || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                builder.Append(line).Append('\n');
                continue;
            }

            AppendInlineCode(builder, line);
        }

        AppendHtmlCodeElements(builder, source);

        return builder.ToString();
    }

    private static void AppendHtmlCodeElements(StringBuilder builder, string source)
    {
        foreach (var regex in new[] { PreformattedCodeRegex(), InlineCodeElementRegex() })
        {
            foreach (Match match in regex.Matches(source))
            {
                builder.Append(
                        WebUtility.HtmlDecode(TagRegex().Replace(match.Groups[1].Value, string.Empty)))
                    .Append('\n');
            }
        }
    }

    private static void AppendInlineCode(StringBuilder builder, string line)
    {
        var start = 0;
        while (true)
        {
            var open = line.IndexOf('`', start);
            if (open < 0)
                return;

            var close = line.IndexOf('`', open + 1);
            if (close < 0)
                return;

            builder.Append(line, open + 1, close - open - 1).Append('\n');
            start = close + 1;
        }
    }

    /// <summary>
    /// Requests one documented URL and counts the model entries it returns.
    /// </summary>
    private async Task<(int Count, List<string> ModelIds, string Error)> ProbeEndpointAsync(
        string url,
        CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
                return (0, [], $"HTTP {status}");

            var body = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return (0, [], "response was an HTML page, not a model list");

            return ParseModelList(body);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (0, [], ex.Message);
        }
    }

    /// <summary>
    /// Reads an OpenAI-style <c>data</c> array or a <c>models</c> array and counts the
    /// entries that carry an identifier. A body that is valid JSON but holds neither is
    /// reported as such, which is what separates "this is not the catalog" from "the
    /// request failed".
    /// </summary>
    internal static (int Count, List<string> ModelIds, string Error) ParseModelList(string body)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(body);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            var head = body.Trim();
            head = head.Length > 60 ? head[..60] + "…" : head;

            return (0, [], string.IsNullOrWhiteSpace(head)
                ? "empty body"
                : $"body is not JSON: \"{head}\"");
        }

        JsonElement entries = default;
        var found = false;
        foreach (var property in new[] { "data", "models" })
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(property, out var array)
                && array.ValueKind == JsonValueKind.Array)
            {
                entries = array;
                found = true;
                break;
            }
        }

        if (!found)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                entries = root;
            }
            else
            {
                return (0, [], "JSON has no 'data' or 'models' array");
            }
        }

        var ids = new List<string>();
        foreach (var item in entries.EnumerateArray())
        {
            var id = item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object => ReadIdentifier(item),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id);
        }

        return ids.Count > 0
            ? (ids.Count, ids, string.Empty)
            : (0, [], "model array held no identifiable entries");
    }

    private static string? ReadIdentifier(JsonElement item)
    {
        foreach (var property in new[] { "id", "name", "slug", "model" })
        {
            if (item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }

        return null;
    }

    private DecisionActionResult CreateSuccessResult(
        DecisionActionContext context,
        int count,
        List<string> modelIds,
        string endpoint,
        IReadOnlyList<string> failures)
    {
        context.State.Properties["modelCount"] = count;
        context.State.Properties["modelCountMethod"] = "documented-endpoint";
        context.State.Properties["modelCountSourceUrl"] = endpoint;

        // This URL is also the strongest evidence the batch can have about where the provider
        // serves its API, and the field that must name it is the base URL — a different tree,
        // which otherwise re-searches the same documentation and may not find it.
        if (context.TemplateParameters.TryGetValue("providerUrl", out var providerUrl))
            _cache.RecordCatalogEndpoint(providerUrl, endpoint);

        var evidence = new DecisionData
        {
            Id = $"models-documented-{Guid.NewGuid():N}",
            Source = endpoint,
            Type = "ModelList",
            Content = $"Found {count} models at the documented endpoint {endpoint}",
            CreatedAt = DateTimeOffset.UtcNow,
            ActionId = context.NodeId,
            Metadata = new Dictionary<string, string>
            {
                ["url"] = endpoint,
                ["modelCount"] = count.ToString(),
                ["sampleIds"] = string.Join(", ", modelIds.Take(MaxEndpointIdsInSummary))
            }
        };

        var properties = new Dictionary<string, string>
        {
            ["queryResult"] = "success",
            ["modelCount"] = count.ToString(),
            ["url"] = endpoint
        };

        if (failures.Count > 0)
            properties["skipped"] = string.Join(" | ", failures.Take(MaxEndpointProbes));

        return new DecisionActionResult(
            new[] { evidence },
            properties,
            DecisionActionStatus.Success);
    }

    /// <summary>
    /// Reads the sibling field URLs the documentation lives behind, in the order the
    /// research service considers them most authoritative.
    /// </summary>
    private static bool TryGetDocumentationUrls(
        IReadOnlyDictionary<string, string> templateParameters,
        out List<string> urls)
    {
        urls = new List<string>();

        if (!templateParameters.TryGetValue("siblingUrls", out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        foreach (var segment in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex >= segment.Length - 1)
                continue;

            var url = segment[(equalsIndex + 1)..].Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && !urls.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                urls.Add(url);
            }
        }

        return urls.Count > 0;
    }

    [GeneratedRegex(@"<pre\b[^>]*>(.*?)</pre>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PreformattedCodeRegex();

    [GeneratedRegex(@"<code\b[^>]*>(.*?)</code>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InlineCodeElementRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();
}
