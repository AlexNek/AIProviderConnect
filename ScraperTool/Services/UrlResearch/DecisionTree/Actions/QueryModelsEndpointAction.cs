using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using ScraperTool.Services.UrlResearch.DecisionTree.Formatting;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Queries the provider's models endpoint ({baseUrl}{modelsEndpoint}) and parses
/// the JSON response to extract model IDs/names and total count.
/// Handles pagination internally (follows next_page tokens until exhausted or budget hit).
/// Produces ModelList evidence.
/// </summary>
public sealed class QueryModelsEndpointAction : IDecisionAction
{
    private const int MaxPages = 10;
    private const int MaxModelIdsInSummary = 10;
    private const int MaxModelIdLength = 40;

    /// <summary>
    /// Path up to and including a version segment, keeping any prefix in front of it. The optional
    /// letter suffix matters: a real catalogue entry serves <c>/v1beta/</c>, which a digit-only
    /// pattern does not match at all — it fell back to the bare host and measured
    /// <c>…googleapis.com/models</c> as 404, where the suffixed <c>…/v1beta/models</c> answers 403
    /// (auth-gated, so the base is right and only the key is missing).
    /// </summary>
    private const string VersionedBasePattern = @"^(.*?/v\d+[a-z]*)(?:/|$)";

    /// <summary>Leading version segment of an endpoint path, which a resolved base already carries.</summary>
    private const string LeadingVersionPattern = @"^/v\d+[a-z]*(?=/|$)";

    private readonly HttpClient _http;
    private readonly IStringListFormatter _stringListFormatter;

    public string Key => "queryModelsEndpoint";

    public QueryModelsEndpointAction(HttpClient http, IStringListFormatter stringListFormatter)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _stringListFormatter = stringListFormatter ?? throw new ArgumentNullException(nameof(stringListFormatter));
    }

    public async Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        // When evaluating candidates (e.g. in the baseUrl tree), probe the
        // candidate URL set by fetch-next-candidate rather than the template
        // baseUrl. This allows the tree to test each discovered URL.
        string? baseUrl = null;
        if (context.State.Properties.TryGetValue(DecisionTreeStateKeys.LastFetchedUrl, out var fetchedObj)
            && fetchedObj is string fetchedUrl
            && !string.IsNullOrWhiteSpace(fetchedUrl))
        {
            baseUrl = fetchedUrl;
        }
        else if (context.TemplateParameters.TryGetValue("baseUrl", out var templateBaseUrl)
                 && !string.IsNullOrWhiteSpace(templateBaseUrl))
        {
            baseUrl = templateBaseUrl;
        }

        if (baseUrl is null)
        {
            return new DecisionActionResult(
                null, null, DecisionActionStatus.PermanentFailure,
                "No baseUrl to probe — neither candidate URL in state nor template parameter.");
        }

        var modelsEndpoint = context.TemplateParameters.TryGetValue("modelsEndpoint", out var ep)
            ? ep : "/v1/models";

        // When the candidate URL is a documentation page (e.g. /docs/guides/...),
        // probing it as an API base is pointless — return permanent failure immediately
        // so the tree moves to the next candidate instead of wasting retries.
        if (IsDocumentationPageUrl(baseUrl))
        {
            return new DecisionActionResult(
                null,
                new Dictionary<string, string>
                {
                    ["queryResult"] = "not-api-base",
                    ["reason"] = "documentation-page",
                    ["url"] = baseUrl
                },
                DecisionActionStatus.PermanentFailure,
                $"Candidate URL is a documentation page, not an API base: {baseUrl}");
        }

        // The candidate is usually a full endpoint taken from a documented call example
        // (https://api.example.com/v1/chat/completions) or a gateway route under a path prefix
        // (https://gateway.example.com/zen/go/v1/models). Probe the base that candidate implies —
        // its path up to and including the version segment — not the bare host: a gateway answers
        // only under its own prefix, so a host-only probe 404s on a base that is correct.
        var url = ResolveProbeUrl(baseUrl, modelsEndpoint);
        var allModelIds = new List<string>();
        var pageCount = 0;
        string? nextUrl = url;

        // Only the baseUrl field is answered by this probe. Recording a winner for any
        // other field kind (the counting tree reuses this action) leaked a stale
        // "verifiedUrl=..." into run diagnostics and into the suggestion path for fields
        // whose value this action cannot establish.
        var isBaseUrlField = context.TemplateParameters.TryGetValue("fieldKind", out var fieldKindValue)
                             && string.Equals(fieldKindValue, "baseUrl", StringComparison.Ordinal);

        try
        {
            while (!string.IsNullOrWhiteSpace(nextUrl) && pageCount < MaxPages)
            {
                pageCount++;
                var response = await _http.GetAsync(nextUrl, cancellationToken);
                context.State.Properties[DecisionTreeStateKeys.LastHttpStatus] = (int)response.StatusCode;

                // When this probe confirms a working API base — 2xx success, or
                // 401/403 with a JSON body (auth-gated, which the tree treats as
                // a confirmed endpoint via is-auth-gated) — record the BASE URL
                // for the suggestion, not the raw candidate. The winning candidate
                // is often a full endpoint route extracted from a curl example
                // (e.g. .../v1/chat/completions); the baseUrl field must hold the
                // version-prefixed base (.../v1) so the library appends
                // "chat/completions" correctly.
                //
                // 401/403 with JSON confirms a real API that requires a key;
                // 401/403 with HTML is a CDN/bot block, not an API signal.
                if (isBaseUrlField && IsAuthGatedStatus((int)response.StatusCode))
                {
                    var authBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (IsJsonResponse(response, authBody))
                    {
                        context.State.Properties[DecisionTreeStateKeys.IsAuthGated] = true;
                        context.State.Properties[DecisionTreeStateKeys.VerifiedWinnerUrl] =
                            ResolveSuggestedBaseUrl(baseUrl, modelsEndpoint);
                        context.State.Properties[DecisionTreeStateKeys.ModelCount] = 0;
                        context.State.Properties[DecisionTreeStateKeys.ModelIds] = "";
                        return BuildSuccessResult([], url, pageCount, context.NodeId);
                    }

                    // Non-JSON 401/403 (e.g. HTML CDN block) — fall through to
                    // normal error handling, which will reject it as not-an-API.
                }

                if (isBaseUrlField && response.IsSuccessStatusCode)
                {
                    context.State.Properties[DecisionTreeStateKeys.VerifiedWinnerUrl] =
                        ResolveSuggestedBaseUrl(baseUrl, modelsEndpoint);
                }

                // For 401/403 with non-JSON body (e.g. plain text "unauthorized"),
                // still record the base URL so the tree's is-auth-gated predicate
                // can pick it up even though the probe returns PermanentFailure.
                if (isBaseUrlField && IsAuthGatedStatus((int)response.StatusCode)
                    && !context.State.Properties.ContainsKey(DecisionTreeStateKeys.VerifiedWinnerUrl))
                {
                    context.State.Properties[DecisionTreeStateKeys.VerifiedWinnerUrl] =
                        ResolveSuggestedBaseUrl(baseUrl, modelsEndpoint);
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var pageResult = ParseModelsResponse(response, nextUrl, body);

                if (pageResult.Failure is not null)
                    return pageResult.Failure;

                allModelIds.AddRange(pageResult.ModelIds);
                nextUrl = pageResult.NextPageUrl;
            }

            context.State.Properties[DecisionTreeStateKeys.ModelCount] = allModelIds.Count;
            context.State.Properties[DecisionTreeStateKeys.ModelIds] = string.Join(", ", allModelIds.Take(MaxModelIdsInSummary));
            return BuildSuccessResult(allModelIds, url, pageCount, context.NodeId);
        }
        catch (JsonException ex)
        {
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["queryResult"] = "parse-error" },
                DecisionActionStatus.PermanentFailure,
                ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["queryResult"] = "error" },
                DecisionActionStatus.TransientFailure,
                ex.Message);
        }
    }

    /// <summary>
    /// Returns true for HTTP status codes that typically indicate an API requires authentication.
    /// 401 (Unauthorized) and 403 (Forbidden) both signal that the endpoint exists but needs a key.
    /// </summary>
    private static bool IsAuthGatedStatus(int statusCode) =>
        statusCode == 401 || statusCode == 403;

    /// <summary>
    /// Returns true when the response body is JSON (by content-type header or body shape).
    /// A 401/403 with a JSON body confirms a real API endpoint that requires authentication;
    /// a 401/403 with an HTML body is a CDN or bot-protection block, not an API signal.
    /// </summary>
    private static bool IsJsonResponse(HttpResponseMessage response, string body)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return true;

        // Content-Type may be missing or generic (text/plain); check the body shape.
        var trimmed = (body ?? string.Empty).TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    /// <summary>
    /// Detects documentation-like page URLs that can never be API base URLs.
    /// Checks the URL path for segments like /docs, /guide, /api-reference, etc.
    /// </summary>
    internal static bool IsDocumentationPageUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var path = uri.AbsolutePath;

        // Check for common documentation path segments.
        // These indicate a documentation/guide/reference page, not an API endpoint.
        string[] docSegments = ["/docs", "/guide", "/guides", "/api-reference", "/api_reference", "/reference", "/api-ref"];
        foreach (var seg in docSegments)
        {
            if (path.Contains(seg, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the models URL to probe for a candidate: the base the candidate implies, plus the
    /// models path. A documented call example such as
    /// <c>https://gateway.example.com/zen/go/v1/models</c> therefore probes itself,
    /// <c>https://api.example.com/v1/chat/completions</c> probes
    /// <c>https://api.example.com/v1/models</c>, a suffixed version
    /// (<c>…/v1beta/models</c>) keeps its suffix, and a bare host from an OpenAPI "servers" block
    /// probes <c>{host}/v1/models</c>.
    /// </summary>
    internal static string ResolveProbeUrl(string candidateUrl, string modelsEndpoint)
    {
        var probeBase = ResolveSuggestedBaseUrl(candidateUrl, modelsEndpoint);
        var modelsPath = Regex.Replace(modelsEndpoint, LeadingVersionPattern, string.Empty);

        return string.Concat(probeBase.TrimEnd('/'), "/", modelsPath.TrimStart('/'));
    }

    /// <summary>
    /// Resolves the base URL to suggest for the baseUrl field from a winning candidate.
    /// The candidate is often a full endpoint route extracted from a curl example
    /// (e.g. <c>https://api.example.com/v1/chat/completions</c>). This truncates the path
    /// right after the version segment (<c>/v1</c>, <c>/v1beta</c>), preserving any prefix before it
    /// (e.g. <c>/openai/v1</c>), so the result is the version-prefixed base
    /// (<c>https://api.example.com/v1</c>) that the library appends routes to. When the
    /// candidate has no version segment (e.g. a bare host from an OpenAPI "servers"
    /// block), the version prefix is taken from the models endpoint (e.g. "/v1" from
    /// "/v1/models").
    /// </summary>
    internal static string ResolveSuggestedBaseUrl(string candidateUrl, string modelsEndpoint)
    {
        if (!Uri.TryCreate(candidateUrl, UriKind.Absolute, out var uri))
            return candidateUrl.TrimEnd('/');

        var host = $"{uri.Scheme}://{uri.Authority}";

        // Keep the path up to and including the version segment.
        var versionMatch = Regex.Match(uri.AbsolutePath, VersionedBasePattern, RegexOptions.IgnoreCase);
        if (versionMatch.Success)
            return host + versionMatch.Groups[1].Value;

        // No version segment in the candidate — fall back to the models endpoint's
        // version prefix (e.g. "/v1" from "/v1/models").
        var endpointMatch = Regex.Match(modelsEndpoint, VersionedBasePattern, RegexOptions.IgnoreCase);
        if (endpointMatch.Success)
            return host + endpointMatch.Groups[1].Value;

        return host;
    }

    /// <summary>
    /// Result of parsing a single page from a models endpoint.
    /// Either <see cref="Failure"/> is set (response was invalid) or
    /// <see cref="ModelIds"/> contains the extracted IDs.
    /// </summary>
    private sealed record ModelsPageResult(
        List<string> ModelIds,
        string? NextPageUrl,
        DecisionActionResult? Failure)
    {
        public static ModelsPageResult Fail(DecisionActionResult failure) =>
            new([], null, failure);

        public static ModelsPageResult Ok(List<string> ids, string? nextPageUrl) =>
            new(ids, nextPageUrl, null);
    }

    /// <summary>
    /// Validates and parses a single models API response page.
    /// Checks HTTP status, content type, JSON error field, and data array presence.
    /// Returns a failure for any invalid response; returns extracted model IDs on success.
    /// </summary>
    private static ModelsPageResult ParseModelsResponse(
        HttpResponseMessage response, string url, string body)
    {
        var httpFail = CheckHttpStatus(response, url);
        if (httpFail is not null)
            return ModelsPageResult.Fail(httpFail);

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            return ModelsPageResult.Fail(BuildHtmlFailure(body, contentType, url));

        // A gateway can answer a wrong models path with HTTP 200 and a plain-text error
        // body ("Not Found"); the raw JsonException message ("'N' is an invalid start of
        // a value") told the reader nothing about which URL said what.
        JsonDocument? parsed;
        try
        {
            parsed = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return ModelsPageResult.Fail(BuildNotJsonFailure(body, contentType, url));
        }

        using var doc = parsed;
        var root = doc.RootElement;

        var apiFail = CheckApiError(root, url);
        if (apiFail is not null)
            return ModelsPageResult.Fail(apiFail);

        var noDataFail = CheckDataArray(root, url);
        if (noDataFail is not null)
            return ModelsPageResult.Fail(noDataFail);

        var modelIds = ExtractModelIds(root, out var nextPageUrl, url);
        return ModelsPageResult.Ok(modelIds, nextPageUrl);
    }

    private static DecisionActionResult? CheckHttpStatus(HttpResponseMessage response, string url)
    {
        if (response.IsSuccessStatusCode)
            return null;

        var statusCode = (int)response.StatusCode;

        // Only genuinely transient conditions deserve a retry: 429 (rate limited)
        // and 5xx (server-side) may succeed on a later attempt. Every other error
        // is a 4xx client error (400/401/403/404/405/410 ...) which is permanent
        // for a given URL — retrying the same probe only burns the node budget
        // (this previously caused an endless retry loop on a 404). 401 remains a
        // permanent failure but is still surfaced via lastHttpStatus so the
        // is-auth-gated predicate can treat an auth-gated API base as a win.
        var isTransient = statusCode == 429 || statusCode >= 500;

        return new DecisionActionResult(
            null,
            new Dictionary<string, string>
            {
                ["queryResult"] = "http-error",
                ["statusCode"] = statusCode.ToString(),
                ["url"] = url
            },
            isTransient
                ? DecisionActionStatus.TransientFailure
                : DecisionActionStatus.PermanentFailure,
            $"HTTP {statusCode} from {url}");
    }

    /// <summary>
    /// Describes a successful HTTP call whose body is not a JSON model list — typically a
    /// soft 404 page or plain-text error the CDN returns for an unknown API path.
    /// </summary>
    private static DecisionActionResult BuildNotJsonFailure(string body, string contentType, string url)
    {
        var head = (body ?? string.Empty).Trim();
        if (head.Length > 60)
            head = head[..60] + "…";

        return new DecisionActionResult(
            null,
            new Dictionary<string, string>
            {
                ["queryResult"] = "not-json",
                ["contentType"] = contentType,
                ["url"] = url
            },
            DecisionActionStatus.PermanentFailure,
            $"Response from {url} is not a JSON model list "
            + $"({(string.IsNullOrWhiteSpace(contentType) ? "no content type" : contentType)}): \"{head}\"");
    }

    private static DecisionActionResult BuildHtmlFailure(string body, string contentType, string url)
    {
        var title = ExtractHtmlTitle(body);
        return new DecisionActionResult(
            null,
            new Dictionary<string, string>
            {
                ["queryResult"] = "not-api",
                ["contentType"] = contentType,
                ["url"] = url,
                ["pageTitle"] = title
            },
            DecisionActionStatus.PermanentFailure,
            $"Response is HTML ({contentType}), not JSON — baseUrl is not an API endpoint. Page title: '{title}'.");
    }

    private static DecisionActionResult? CheckApiError(JsonElement root, string url)
    {
        if (!root.TryGetProperty("error", out var errorElement))
            return null;

        var errorMessage = errorElement.ValueKind == JsonValueKind.Object
            ? (errorElement.TryGetProperty("message", out var msg) ? msg.GetString() : null)
            : errorElement.ValueKind == JsonValueKind.String ? errorElement.GetString() : null;

        return new DecisionActionResult(
            null,
            new Dictionary<string, string>
            {
                ["queryResult"] = "api-error",
                ["error"] = errorMessage ?? "unknown",
                ["url"] = url
            },
            DecisionActionStatus.PermanentFailure,
            $"API returned error: {errorMessage ?? "unknown"}.");
    }

    private static DecisionActionResult? CheckDataArray(JsonElement root, string url)
    {
        if (root.TryGetProperty("data", out var dataElement)
            && dataElement.ValueKind == JsonValueKind.Array)
            return null;

        return new DecisionActionResult(
            null,
            new Dictionary<string, string>
            {
                ["queryResult"] = "unexpected-response",
                ["url"] = url
            },
            DecisionActionStatus.PermanentFailure,
            "Response JSON has no 'data' array and no 'error' — not a models endpoint.");
    }

    private static List<string> ExtractModelIds(JsonElement root, out string? nextPageUrl, string url)
    {
        root.TryGetProperty("data", out var dataElement);
        var modelIds = new List<string>();
        foreach (var item in dataElement.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (!string.IsNullOrWhiteSpace(id))
                modelIds.Add(id);
        }

        nextPageUrl = null;
        if (root.TryGetProperty("has_more", out var hasMore)
            && hasMore.GetBoolean()
            && root.TryGetProperty("next_page", out var nextPage)
            && nextPage.ValueKind == JsonValueKind.String)
        {
            var token = nextPage.GetString();
            if (!string.IsNullOrWhiteSpace(token))
            {
                var separator = url.Contains('?') ? '&' : '?';
                nextPageUrl = $"{url}{separator}after={token}";
            }
        }

        return modelIds;
    }

    private DecisionActionResult BuildSuccessResult(
        List<string> modelIds, string url, int pageCount, string nodeId)
    {
        var modelListData = new DecisionData
        {
            Id = $"models-{Guid.NewGuid():N}",
            Source = url,
            Type = "ModelList",
            Content = $"Found {modelIds.Count} models",
            CreatedAt = DateTimeOffset.UtcNow,
            ActionId = nodeId,
            Metadata = new Dictionary<string, string>
            {
                ["url"] = url,
                ["modelCount"] = modelIds.Count.ToString(),
                ["pages"] = pageCount.ToString(),
                ["sampleIds"] = string.Join(", ", modelIds.Take(10))
            }
        };

        return new DecisionActionResult(
            new[] { modelListData },
            new Dictionary<string, string>
            {
                ["queryResult"] = "success",
                ["modelCount"] = modelIds.Count.ToString(),
                ["pages"] = pageCount.ToString()
            },
            DecisionActionStatus.Success);
    }

    private static string ExtractHtmlTitle(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "(empty)";

        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            @"<title[^>]*>(.*?)</title>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.Singleline);

        return match.Success
            ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim()
            : "(no title)";
    }
}
