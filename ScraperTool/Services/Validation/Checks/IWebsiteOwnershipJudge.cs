using System.Text.Json;

using ScraperTool.Models;

using WebTools.NET.Abstractions;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Determines whether a <c>website</c> field on a subdomain is the provider's
/// own site or a section of a site the provider lives at. The root domain
/// settles the question: whoever answers there decides whether the subdomain
/// is one of its sections or a service of its own.
/// </summary>
public interface IWebsiteOwnershipJudge
{
    /// <summary>
    /// Derives the root domain of <paramref name="websiteUri"/>, checks its
    /// reachability, fetches its page, and judges whether the provider is
    /// named there. Returns null when the website is not on a subdomain or
    /// the root domain is unreachable.
    /// </summary>
    Task<(EWebsiteRootVerdict Verdict, string Reason)?> CheckWebsiteRootOwnershipAsync(
        Uri websiteUri,
        JsonElement root,
        string fileName,
        CancellationToken ct);

    /// <summary>
    /// Runs the full website-root check and emits the appropriate findings to the sink.
    /// Returns true when the check settled the field (i.e., the caller should return without
    /// further checks); false when the caller should continue to the next check.
    /// </summary>
    Task<bool> TrySettleWebsiteRootAsync(
        Uri websiteUri,
        JsonElement root,
        string fileName,
        string field,
        string url,
        IValidationIssueSink sink,
        CancellationToken ct);
}
