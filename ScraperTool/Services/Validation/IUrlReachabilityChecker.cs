using WebTools.NET.Models;

namespace ScraperTool.Services.Validation;

/// <summary>
/// Checks URL reachability with HTTP fallback and bot-protection detection.
/// </summary>
public interface IUrlReachabilityChecker
{
    /// <summary>
    /// Checks whether the specified URI is reachable.
    /// </summary>
    Task<UrlCheckResult> CheckAsync(Uri uri, CancellationToken ct = default);
}
