using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using ScraperTool.Services.UrlResearch.DecisionTree.Formatting;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

using WebTools.NET.Abstractions;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Performs a web search using the template parameters to build the query.
/// Adds discovered URLs to the candidate queue stored as decision data.
/// </summary>
public sealed class WebSearchAction : IDecisionAction
{
    private const int MaxSearchResults = 5;
    private const int MaxUrlsInSummary = 5;
    private const int MaxUrlLengthInSummary = 80;

    private readonly IWebSearchProvider _search;
    private readonly IStringListFormatter _stringListFormatter;
    private readonly ICandidateUrlProvider _candidateUrlProvider;

    public string Key => "webSearch";

    public WebSearchAction(
        IWebSearchProvider search,
        IStringListFormatter stringListFormatter,
        ICandidateUrlProvider candidateUrlProvider)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _stringListFormatter = stringListFormatter ?? throw new ArgumentNullException(nameof(stringListFormatter));
        _candidateUrlProvider = candidateUrlProvider ?? throw new ArgumentNullException(nameof(candidateUrlProvider));
    }

    public async Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!context.TemplateParameters.TryGetValue("searchQueryTemplate", out var queryTemplate)
            || string.IsNullOrWhiteSpace(queryTemplate))
        {
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["searchResult"] = "no-query-template" },
                DecisionActionStatus.PermanentFailure,
                "No search query template configured for this field kind.");
        }

        var query = queryTemplate;
        foreach (var kvp in context.TemplateParameters)
        {
            query = query.Replace("{" + kvp.Key + "}", kvp.Value, StringComparison.Ordinal);
        }

        try
        {
            var result = await _search.SearchAsync(query, MaxSearchResults, cancellationToken);

            if (!result.Success || result.Results.Count == 0)
            {
                return new DecisionActionResult(
                    null,
                    new Dictionary<string, string> { ["searchResult"] = "no-results" },
                    DecisionActionStatus.TransientFailure,
                    result.ErrorMessage ?? "No search results.");
            }

            // Extract the provider's root domain from providerUrl so we can
            // filter search results to the provider's own site. For URL discovery
            // (pricing, docs, login) the answer is almost always on the provider's
            // own domain — third-party pages that merely mention the provider name
            // are not valid results.
            var providerDomain = GetProviderRootDomain(
                context.TemplateParameters.TryGetValue("providerUrl", out var pUrl) ? pUrl : null);

            // The page the finding names has already been fetched and judged by the rungs above this
            // one — that is why the search is running at all. Handing it back as a candidate spends a
            // search slot and a classification call to arrive at an answer the tree already has, and
            // the only value it could propose is the one being replaced.
            var currentValueKey = CandidateUrlNormalizer.Normalize(
                context.TemplateParameters.TryGetValue("currentValue", out var cValue) ? cValue : null);

            var candidateUrls = _candidateUrlProvider.GetCandidateUrls(context.Data).ToList();
            var evidence = new List<DecisionData>();

            foreach (var item in result.Results)
            {
                if (string.IsNullOrWhiteSpace(item.Url))
                    continue;

                if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var resultUri))
                    continue;

                // Compared as the page it addresses, not as the string the search printed: the same
                // address arrives with or without its trailing slash, and a second spelling of the
                // rejected page is the same rejection.
                if (currentValueKey.Length > 0
                    && string.Equals(
                        CandidateUrlNormalizer.Normalize(item.Url),
                        currentValueKey,
                        StringComparison.Ordinal))
                    continue;

                var host = resultUri.Host.ToLowerInvariant();
                if (IsSearchEngineHost(host))
                    continue;

                // Skip non-provider domains that don't contain actual provider data
                // (GitHub repos, npm packages, Docker Hub, etc.)
                if (IsNonProviderHost(host))
                    continue;

                // Prefer the provider's own domain. Third-party sites that merely
                // mention the provider (e.g. talkie-ai.com for MiniMax) are not
                // valid pricing/docs/login pages.
                if (!string.IsNullOrWhiteSpace(providerDomain)
                    && !IsSameOrSubDomain(host, providerDomain))
                    continue;

                if (!candidateUrls.Contains(item.Url, StringComparer.OrdinalIgnoreCase))
                {
                    candidateUrls.Add(item.Url);

                    evidence.Add(new DecisionData
                    {
                        Id = $"search-{evidence.Count}-{Guid.NewGuid():N}",
                        Source = query,
                        Type = "SearchResult",
                        Content = item.Url,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ActionId = context.NodeId,
                        Metadata = new Dictionary<string, string>
                        {
                            ["title"] = item.Title ?? "",
                            ["snippet"] = item.Snippet ?? ""
                        }
                    });
                }
            }

            // Store only the candidate count in state — NOT the URL list.
            // The full candidate queue is available as CandidateLink evidence in the data store.
            // Storing URLs in state caused the LLM to classify based on candidate URLs
            // it saw in the state text rather than the actual fetched page content.
            context.State.Properties["candidateCount"] = candidateUrls.Count;

            if (evidence.Count == 0)
            {
                return new DecisionActionResult(
                    null,
                    new Dictionary<string, string> { ["searchResult"] = "no-new-candidates" },
                    DecisionActionStatus.Success);
            }

            return new DecisionActionResult(
                evidence,
                new Dictionary<string, string>
                {
                    ["searchResult"] = "success",
                    ["newCandidateCount"] = evidence.Count.ToString()
                },
                DecisionActionStatus.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["searchResult"] = "error" },
                DecisionActionStatus.TransientFailure,
                ex.Message);
        }
    }

    private static bool IsSearchEngineHost(string host)
    {
        var searchEngines = new[]
        {
            "google.com", "www.google.com",
            "bing.com", "www.bing.com",
            "duckduckgo.com", "www.duckduckgo.com",
            "yahoo.com", "search.yahoo.com",
            "yandex.com", "yandex.ru",
            "baidu.com"
        };

        return searchEngines.Contains(host, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the host is a non-provider domain that doesn't contain
    /// actual provider data (GitHub repos, npm packages, Docker Hub, etc.).
    /// These pages may have numbers (stars, downloads, etc.) that the LLM
    /// could mistake for model counts.
    /// </summary>
    private static bool IsNonProviderHost(string host)
    {
        var nonProviderHosts = new[]
        {
            "github.com", "www.github.com",
            "gitlab.com", "www.gitlab.com",
            "bitbucket.org", "www.bitbucket.org",
            "npmjs.com", "www.npmjs.com",
            "hub.docker.com", "docker.com",
            "pypi.org", "www.pypi.org",
            "crates.io",
            "maven.org", "central.sonatype.com",
            "nuget.org", "www.nuget.org",
            "huggingface.co", "www.huggingface.co"
        };

        return nonProviderHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the root domain from a URL string. Strips "www." prefix and
    /// returns the registrable domain (e.g. "minimax.io" from
    /// "https://platform.minimax.io/docs/pricing").
    /// <para>
    /// The whole host cannot stand in for the provider's site: when the stored value is a page on a
    /// subdomain, the homepage the search is asked to find sits at the root, and matching on the
    /// subdomain throws that answer away as a third party — leaving the fallback unable to reach the
    /// one page it exists for.
    /// </para>
    /// </summary>
    private static string? GetProviderRootDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www."))
            host = host[4..];

        // A numeric address has no domain suffix to cut from, and cutting one would leave an
        // unrelated fragment of the host.
        if (uri.HostNameType != UriHostNameType.Dns)
            return host;

        var parts = host.Split('.');

        // A ccTLD can carry a second level of its own ("co.uk", "com.au"), so a short trailing pair
        // keeps three labels rather than splitting the real domain in two.
        if (parts.Length >= 3 && parts[^2].Length <= 3 && parts[^1].Length <= 2)
            return string.Join(".", parts[^3..]);

        return parts.Length >= 2 ? string.Join(".", parts[^2..]) : host;
    }

    /// <summary>
    /// Returns true when <paramref name="host"/> is the same as, or a subdomain
    /// of, <paramref name="rootDomain"/>. E.g. "platform.minimax.io" matches
    /// root domain "minimax.io".
    /// </summary>
    private static bool IsSameOrSubDomain(string host, string rootDomain)
    {
        if (host.StartsWith("www."))
            host = host[4..];

        return string.Equals(host, rootDomain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + rootDomain, StringComparison.OrdinalIgnoreCase);
    }
}
