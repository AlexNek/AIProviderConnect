using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Services.Validation;

/// <summary>
/// Default implementation of <see cref="IUrlReachabilityChecker"/>.
/// </summary>
public sealed class UrlReachabilityChecker : IUrlReachabilityChecker
{
    private readonly IWebContentFetcher _fetcher;

    private readonly IWebAccessService _web;

    public UrlReachabilityChecker(IWebAccessService web, IWebContentFetcher fetcher)
    {
        _web = web;
        _fetcher = fetcher;
    }

    public async Task<UrlCheckResult> CheckAsync(Uri uri, CancellationToken ct = default)
    {
        var url = uri.ToString();
        var result = await _web.CheckReachabilityAsync(url, ct);

        var finalUri = result.FinalUrl is not null ? new Uri(result.FinalUrl) : null;

        if (result.Reachable)
            return new UrlCheckResult(
                true,
                result.HttpStatus ?? 200,
                null,
                result.RedirectCount,
                finalUri?.ToString());

        if (result.HttpStatus is 401 or 403)
        {
            var browserCheck = await _fetcher.CheckReachabilityAsync(url, ct);
            if (browserCheck.ProtectionType is not null)
                return new UrlCheckResult(
                    false,
                    result.HttpStatus ?? 0,
                    null,
                    result.RedirectCount,
                    finalUri?.ToString(),
                    browserCheck.ProtectionType);
        }

        var fetchResult = await _fetcher.FetchAsync(url, ct: ct);
        if (IsStructuredJsonError(fetchResult.Content, result.HttpStatus ?? 0))
            return new UrlCheckResult(
                true,
                result.HttpStatus ?? 0,
                null,
                result.RedirectCount,
                finalUri?.ToString());

        return new UrlCheckResult(
            false,
            result.HttpStatus ?? 0,
            null,
            result.RedirectCount,
            finalUri?.ToString());
    }

    private static bool IsStructuredJsonError(string? content, int statusCode)
    {
        if (statusCode is >= 200 and < 300)
            return false;

        if (string.IsNullOrWhiteSpace(content))
            return false;

        var trimmed = content.TrimStart();
        if (!trimmed.StartsWith('{'))
            return false;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            return root.TryGetProperty("error", out _)
                   || root.TryGetProperty("message", out _)
                   || root.TryGetProperty("statusCode", out _)
                   || root.TryGetProperty("status", out _);
        }
        catch (Exception)
        {
            // Any parse or unexpected failure — not an error response shape.
            return false;
        }
    }
}
