using System.Text.Json;

using ScraperTool.Models;

using WebTools.NET.Abstractions;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Determines whether a URL behaves like an API endpoint (JSON reply) or like
/// a web page (HTML), and checks whether a 404 <c>baseUrl</c> has a reachable
/// models endpoint as a fallback.
/// </summary>
public interface IApiEndpointProbe
{
    /// <summary>
    /// Probes the URL to determine whether it behaves like an API endpoint or
    /// like a web page. Sends a GET request with <c>Accept: application/json</c>
    /// and judges the reply.
    /// </summary>
    Task<(EApiProbeVerdict Verdict, string Reason)> ProbeIsApiEndpointAsync(
        string url,
        CancellationToken ct);

    /// <summary>
    /// When a <c>baseUrl</c> returns 404, checks whether the provider declares a
    /// models endpoint that is reachable. Returns true when the models endpoint
    /// is reachable (or returns 401/403), false otherwise.
    /// </summary>
    Task<bool> TryModelsEndpointAsync(
        string baseUrl,
        JsonElement root,
        CancellationToken ct);
}
