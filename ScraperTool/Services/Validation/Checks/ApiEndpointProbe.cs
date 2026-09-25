using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

using ScraperTool.Models;

using WebTools.NET.Abstractions;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Default <see cref="IApiEndpointProbe"/>: uses a named <see cref="HttpClient"/>
/// from <see cref="IHttpClientFactory"/> for the JSON probe and
/// <see cref="IUrlReachabilityChecker"/> for the models-endpoint fallback.
/// </summary>
public sealed class ApiEndpointProbe : IApiEndpointProbe
{
    /// <summary>
    /// How much of a probe response body is read to tell data from an error page.
    /// </summary>
    private const int MaxApiProbeBodyPrefixLength = 200;

    /// <summary>
    /// How long an API probe waits before the address counts as not having replied at all.
    /// </summary>
    private static readonly TimeSpan ApiProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Plain-text phrases that real APIs return (with HTTP 200) when no authentication is
    /// supplied.  Recognising these prevents false "not an API" verdicts for endpoints that
    /// answer with a plain-text auth gate instead of a structured 401/403 JSON error.
    /// </summary>
    private static readonly string[] AuthGatePhrases =
    [
        "api key",
        "api-key",
        "apikey",
        "access token",
        "bearer token",
        "authentication required",
        "authorization header",
    ];

    private readonly HttpClient _httpClient;
    private readonly IUrlReachabilityChecker _urlReachabilityChecker;

    public ApiEndpointProbe(
        IHttpClientFactory httpClientFactory,
        IUrlReachabilityChecker urlReachabilityChecker)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClient = httpClientFactory.CreateClient(HttpConstants.ScraperHttpClientName);
        _urlReachabilityChecker = urlReachabilityChecker ?? throw new ArgumentNullException(nameof(urlReachabilityChecker));
    }

    /// <inheritdoc />
    public async Task<(EApiProbeVerdict Verdict, string Reason)> ProbeIsApiEndpointAsync(
        string url,
        CancellationToken ct)
    {
        try
        {
            using var request = CreateJsonRequest(url);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ApiProbeTimeout);

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
                return (EApiProbeVerdict.NotEvaluated, $"answers {(int)response.StatusCode} on its root");

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return (EApiProbeVerdict.NotApi, "returns an HTML page");

            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                return (EApiProbeVerdict.IsApi, string.Empty);

            var prefix = await ReadBodyPrefixAsync(response, MaxApiProbeBodyPrefixLength, cts.Token);
            if (string.IsNullOrWhiteSpace(prefix))
                return (EApiProbeVerdict.NotEvaluated,
                    $"answers {(int)response.StatusCode} with no body");

            var bodyHead = prefix.TrimStart();
            if (bodyHead.Length > 0 && (bodyHead[0] == '{' || bodyHead[0] == '['))
                return (EApiProbeVerdict.IsApi, string.Empty);

            // Some real APIs return a plain-text "You must provide an API key" message with
            // HTTP 200 instead of a structured 401/403 JSON error.  Recognising these auth-gate
            // phrases prevents a false "not an API" verdict for authenticated-only endpoints.
            if (ContainsAuthGatePhrase(prefix))
                return (EApiProbeVerdict.IsApi, "auth-gate response detected");

            return (EApiProbeVerdict.NotApi,
                $"answers {(int)response.StatusCode} with a body nothing can read as data: \"{Preview(prefix)}\"");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The probe itself ran out of time — no evidence either way.
            return (EApiProbeVerdict.NotEvaluated,
                $"did not answer within {ApiProbeTimeout.TotalSeconds}s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (EApiProbeVerdict.NotEvaluated, $"could not be reached ({ex.Message})");
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryModelsEndpointAsync(
        string baseUrl,
        JsonElement root,
        CancellationToken ct)
    {
        string? modelsPath = null;
        if (root.TryGetProperty(ProviderJsonFields.ModelsEndpoint, out var modelsEp)
            && modelsEp.ValueKind == JsonValueKind.String)
        {
            modelsPath = modelsEp.GetString();
        }

        if (string.IsNullOrWhiteSpace(modelsPath))
            return false;

        var baseUrlTrimmed = baseUrl.TrimEnd('/');
        var modelsUrl = $"{baseUrlTrimmed}/{modelsPath.TrimStart('/')}";
        if (!Uri.TryCreate(modelsUrl, UriKind.Absolute, out var modelsUri))
            return false;

        var modelsStatus = await _urlReachabilityChecker.CheckAsync(modelsUri, ct);
        var modelsHttpStatus = modelsStatus.HttpStatus ?? 0;
        return modelsStatus.Reachable || modelsHttpStatus is 401 or 403;
    }

    /// <summary>
    /// Asks for data rather than a document: the scraper client's <c>Accept: text/html</c> default
    /// would let content negotiation answer a different question than the one being asked.
    /// </summary>
    private static HttpRequestMessage CreateJsonRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        return request;
    }

    /// <summary>
    /// Reads a bounded prefix of a response body, enough to tell JSON from an error page without
    /// downloading a document a status alone already described.
    /// </summary>
    private static async Task<string> ReadBodyPrefixAsync(
        HttpResponseMessage response,
        int maxLength,
        CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var buffer = new char[maxLength];
        var read = await reader.ReadAsync(buffer.AsMemory(0, maxLength), ct);

        return read > 0 ? new string(buffer, 0, read) : string.Empty;
    }

    private static string Preview(string body)
    {
        var collapsed = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return collapsed.Length > MaxApiProbeBodyPrefixLength
            ? string.Concat(collapsed.AsSpan(0, MaxApiProbeBodyPrefixLength), "…")
            : collapsed;
    }

    private static bool ContainsAuthGatePhrase(string body)
    {
        foreach (var phrase in AuthGatePhrases)
        {
            if (body.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
