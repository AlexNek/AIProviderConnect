using System.IO;
using System.Text.Json;

using ScraperTool.Models;

using WebTools.NET.Abstractions;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Default <see cref="IWebsiteOwnershipJudge"/>: derives the root domain,
/// checks its reachability, fetches its page, and judges whether the
/// provider is named there.
/// </summary>
public sealed class WebsiteOwnershipJudge : IWebsiteOwnershipJudge
{
    private static readonly char[] IdentityTokenSeparators =
        [' ', '(', ')', '[', ']', '-', '_', ',', '/', ':'];

    /// <summary>
    /// Words that appear in provider names without identifying anyone. A page that merely says
    /// "AI" is not evidence about who owns it.
    /// </summary>
    private static readonly HashSet<string> NonIdentityWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ai", "api", "app", "cloud", "inc", "llc", "ltd", "labs", "model", "models",
            "platform", "studio", "the"
        };

    private readonly IWebContentFetcher _fetcher;

    public WebsiteOwnershipJudge(IWebContentFetcher fetcher)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
    }

    /// <inheritdoc />
    public async Task<(EWebsiteRootVerdict Verdict, string Reason)?> CheckWebsiteRootOwnershipAsync(
        Uri websiteUri,
        JsonElement root,
        string fileName,
        CancellationToken ct)
    {
        var rootDomain = UrlDomainRules.GetRootDomain(websiteUri);
        if (rootDomain is null)
            return null;

        var rootCheck = await _fetcher.CheckReachabilityAsync(rootDomain, ct);
        if (!rootCheck.Reachable)
            return null;

        var rootPage = await _fetcher.FetchAsync(rootDomain, ct: ct);

        if (!rootPage.Success || string.IsNullOrWhiteSpace(rootPage.Content))
            return (
                EWebsiteRootVerdict.NotEvaluated,
                rootPage.ErrorMessage ?? "the root domain page carried no text");

        var rootHost = new Uri(rootDomain).Host;

        // "www" is the conventional alias of a root, not a different site; anything else that ends
        // up answering there is a different page than the one being asked about.
        if (Uri.TryCreate(rootPage.FinalUrl, UriKind.Absolute, out var landedUri)
            && !string.IsNullOrWhiteSpace(rootPage.FinalUrl)
            && !string.Equals(
                UrlDomainRules.StripWww(landedUri.Host),
                rootHost,
                StringComparison.OrdinalIgnoreCase))
            return (
                EWebsiteRootVerdict.ForeignRoot,
                $"root domain '{rootHost}' hands its visitors to '{landedUri.Host}'");

        var identityTokens = GetProviderIdentityTokens(root, fileName);
        var matchedToken = identityTokens.FirstOrDefault(
            token => rootPage.Content.Contains(token, StringComparison.OrdinalIgnoreCase));

        return matchedToken is null
            ? (
                EWebsiteRootVerdict.ForeignRoot,
                $"root domain '{rootHost}' does not name this provider — the subdomain is the service's own site")
            : (
                EWebsiteRootVerdict.ProviderOwnsRoot,
                $"the root domain '{rootHost}' names the provider ('{matchedToken}')");
    }

    /// <inheritdoc />
    public async Task<bool> TrySettleWebsiteRootAsync(
        Uri websiteUri,
        JsonElement root,
        string fileName,
        string field,
        string url,
        IValidationIssueSink sink,
        CancellationToken ct)
    {
        var rootResult = await CheckWebsiteRootOwnershipAsync(websiteUri, root, fileName, ct);
        if (rootResult is null) return false;

        var (rootVerdict, rootReason) = rootResult.Value;

        if (rootVerdict == EWebsiteRootVerdict.ProviderOwnsRoot)
        {
            var rootDomain = UrlDomainRules.GetRootDomain(websiteUri);
            var issue = sink.FailWith(fileName, field, url,
                ValidationIssueCodes.WebsiteIsSubdomain,
                $"Field 'website' is a page on the provider's own subdomain '{websiteUri.Host}' — {rootReason}, so the public homepage is expected at '{rootDomain}': {url}",
                $"subdomain of the provider's own site — {rootReason}");
            issue.SuggestedRootDomain = rootDomain;
            return true;
        }

        // ForeignRoot or UnknownRoot: emit the root verdict pass, but continue to the
        // "URL is reachable" pass (the original code did not return here).
        sink.Pass(fileName, field, url,
            rootVerdict == EWebsiteRootVerdict.ForeignRoot
                ? rootReason
                : $"website's root domain could not be judged ({rootReason}) — stored value kept");
        return false;
    }

    /// <summary>
    /// The words a provider is called by: its declared display name taken apart, plus its id. Both
    /// come from the definition itself, so no provider is recognised by a list kept in code.
    /// Words that only say what kind of thing a service is ("ai", "platform", "models") cannot
    /// pick out a particular provider and are dropped, as is anything too short to be a name.
    /// </summary>
    private static List<string> GetProviderIdentityTokens(JsonElement root, string fileName)
    {
        var displayName = root.TryGetProperty(
                                  ProviderJsonFields.DisplayName,
                                  out var nameProp)
                              && nameProp.ValueKind == JsonValueKind.String
                          ? nameProp.GetString()
                          : null;

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(displayName))
            candidates.AddRange(
                displayName.Split(IdentityTokenSeparators, StringSplitOptions.RemoveEmptyEntries));

        candidates.Add(Path.GetFileNameWithoutExtension(fileName));

        return candidates
            .Select(candidate => candidate.Trim())
            .Where(candidate =>
                candidate.Length >= 3
                && !NonIdentityWords.Contains(candidate.ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
