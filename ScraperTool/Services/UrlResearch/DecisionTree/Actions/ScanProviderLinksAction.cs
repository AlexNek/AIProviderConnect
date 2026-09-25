using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using ScraperTool.Models;
using ScraperTool.Services.UrlResearch.DecisionTree.Formatting;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

using WebTools.NET.Abstractions;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Scans the provider's website for candidate links using the deterministic link scanner.
/// Produces CandidateLink evidence items for each discovered link and stores a readable
/// summary of the candidate queue in state.
/// </summary>
public sealed class ScanProviderLinksAction : IDecisionAction
{
    private const int MaxUrlsInSummary = 5;
    private const int MaxUrlLengthInSummary = 80;

    private readonly IDeterministicLinkScanner _scanner;
    private readonly IStringListFormatter _stringListFormatter;
    private readonly ProviderResearchCache _cache;

    public string Key => "scanProviderLinks";

    public ScanProviderLinksAction(
        IDeterministicLinkScanner scanner,
        IStringListFormatter stringListFormatter,
        ProviderResearchCache cache)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _stringListFormatter = stringListFormatter ?? throw new ArgumentNullException(nameof(stringListFormatter));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        // Get the provider's base URL from template parameters
        if (!context.TemplateParameters.TryGetValue("providerUrl", out var providerUrl)
            || string.IsNullOrWhiteSpace(providerUrl))
        {
            return new DecisionActionResult(
                null,
                null,
                DecisionActionStatus.PermanentFailure,
                "Missing 'providerUrl' template parameter.");
        }

        try
        {
            // Check the shared cache first — another tree for the same provider
            // may have already scanned this website.
            var rawLinks = await ScanLinksCachedAsync(providerUrl, cancellationToken);

            // When a suggested root domain is present (WebsiteIsSubdomain), skip the
            // early return even with no scanned links — the root domain is the candidate.
            var hasSuggestedRootDomain = context.TemplateParameters.TryGetValue(
                    "suggestedRootDomain", out var suggestedRootDomainValue)
                && !string.IsNullOrWhiteSpace(suggestedRootDomainValue);

            var hasCurrentFieldUrl = TryGetCurrentFieldUrl(context.TemplateParameters, out var currentFieldUrl);

            if (rawLinks.Count == 0
                && !TryGetSiblingUrls(context.TemplateParameters, out _)
                && !hasCurrentFieldUrl
                && !hasSuggestedRootDomain)
            {
                return new DecisionActionResult(
                    null,
                    new Dictionary<string, string> { ["scanResult"] = "no-links-found" },
                    DecisionActionStatus.Success);
            }

            // Scan sibling field URLs (e.g. apiPricingUrl, documentationUrl) that are
            // already known from the provider definition. Links found on these pages are
            // higher-quality candidates than generic homepage links because the pages are
            // topically related to the field being researched.
            var siblingLinks = new List<(DeterministicLinkScanner.LinkInfo Link, string SourceField)>();
            if (TryGetSiblingUrls(context.TemplateParameters, out var siblingUrls))
            {
                foreach (var (fieldKey, url) in siblingUrls)
                {
                    try
                    {
                        foreach (var link in await ScanLinksCachedAsync(url, cancellationToken))
                        {
                            siblingLinks.Add((link, fieldKey));
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Sibling scan failure is non-fatal — continue with whatever
                        // links we have from the homepage and other siblings.
                    }
                }
            }

            // The page the field currently points to is the strongest remaining link
            // source the definition holds: a broken URL is usually a near-miss, and the
            // page it names often links the correct destination (a docs overview whose
            // "Pricing" item points at the real pricing page). Queue those links only —
            // never the page itself, which validation has just proved wrong for the field.
            if (hasCurrentFieldUrl && !IsSameUrl(currentFieldUrl, providerUrl))
            {
                try
                {
                    foreach (var link in await ScanLinksCachedAsync(currentFieldUrl, cancellationToken))
                    {
                        siblingLinks.Add((link, "currentValue"));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Current-page scan failure is non-fatal — continue with the other sources.
                }
            }

            // Merge homepage links and sibling links, deduplicating by URL.
            // Homepage links come first; sibling links fill in after.
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allLinks = new List<DeterministicLinkScanner.LinkInfo>();

            foreach (var link in rawLinks)
            {
                if (seenUrls.Add(link.Url))
                    allLinks.Add(link);
            }

            foreach (var (link, sourceField) in siblingLinks)
            {
                if (seenUrls.Add(link.Url))
                {
                    allLinks.Add(new DeterministicLinkScanner.LinkInfo(
                        link.Url,
                        $"[from {sourceField}] {link.Description}"));
                }
            }

            // Collect sibling URLs (the sibling pages themselves, not just links
            // extracted from them). These are known-good pages topically related
            // to the field — the tree should evaluate them as candidates first.
            // A profile can declare that its sibling pages only ever link to the
            // answer instead of being the answer, in which case queueing them costs
            // a fetch and a classification per page for a guaranteed non-answer.
            var fieldKind = context.TemplateParameters.TryGetValue("fieldKind", out var fk)
                ? fk
                : string.Empty;
            var queueSiblingPages = SiblingPagesMayBeAnswer(context.TemplateParameters);
            var siblingPageUrls = new List<(string FieldKey, string Url)>();
            if (queueSiblingPages
                && TryGetSiblingUrls(context.TemplateParameters, out var siblingPageUrlList))
            {
                foreach (var (fieldKey, url) in siblingPageUrlList)
                {
                    siblingPageUrls.Add((fieldKey, url));
                }
            }

            if (allLinks.Count == 0 && siblingPageUrls.Count == 0 && !hasSuggestedRootDomain)
            {
                return new DecisionActionResult(
                    null,
                    new Dictionary<string, string> { ["scanResult"] = "no-links-found" },
                    DecisionActionStatus.Success);
            }

            // Sort links by relevance to the field kind so the most promising
            // candidates are evaluated first by the decision tree.
            var sortedLinks = SortByRelevance(
                allLinks,
                fieldKind,
                ReadRelevanceTerms(context.TemplateParameters));

            // Remove content-only paths (blog posts, news articles, press releases)
            // that are structurally unlikely to be pricing, API, or documentation pages.
            // This prevents the decision tree from wasting its budget fetching and
            // classifying pages that the LLM will correctly but expensively reject.
            var filteredLinks = sortedLinks
                .Where(l => !IsContentOnlyPath(l.Url))
                .ToList();

            // When validation flagged the website as a subdomain of the provider's own
            // root domain, inject the computed root domain as the very first candidate.
            // The validator already proved the root domain is reachable and names the
            // provider, so it is a stronger signal than any scanned link.
            var sortedList = filteredLinks;
            string? pinnedRootDomainUrl = null;
            if (hasSuggestedRootDomain)
            {
                pinnedRootDomainUrl = suggestedRootDomainValue;
                sortedList.Insert(0, new DeterministicLinkScanner.LinkInfo(
                    suggestedRootDomainValue!,
                    "Root domain derived from subdomain website (validator computed)"));
            }

            // When validation detected a redirect, inject the redirect target as the
            // first candidate so the tree evaluates it before any scanned links.
            // The redirect target is a strong signal — the original URL explicitly
            // navigated to it, so it is more reliable than scanned links.
            string? pinnedRedirectUrl = null;
            if (context.TemplateParameters.TryGetValue("redirectTargetUrl", out var redirectUrl)
                && !string.IsNullOrWhiteSpace(redirectUrl))
            {
                pinnedRedirectUrl = redirectUrl;
                sortedList.Insert(0, new DeterministicLinkScanner.LinkInfo(
                    redirectUrl,
                    "Redirect target (original URL redirected here)"));
            }

            // Add the sibling page URLs themselves as top-priority candidates.
            // Sibling pages (e.g. apiPricingUrl, documentationUrl) are known-good
            // pages topically related to the field being researched. The tree should
            // evaluate these pages directly before scanning their extracted links —
            // the page itself may be the answer (e.g. a pricing overview page that
            // contains subscription plan information).
            // When a sibling URL is already in the list (e.g. from the homepage scan),
            // remove it from its current position and re-insert at the front so it
            // is always evaluated first regardless of relevance-sort penalties.
            var siblingSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Insert after the pinned entries (root domain, redirect), otherwise at the front.
            var siblingInsertIndex = 0;
            if (pinnedRootDomainUrl is not null) siblingInsertIndex++;
            if (pinnedRedirectUrl is not null) siblingInsertIndex++;
            foreach (var (fieldKey, url) in siblingPageUrls)
            {
                if (!siblingSeen.Add(url))
                    continue;

                // Pinned entries stay at the front: the root domain is the validator's
                // computed answer and the redirect target is where the field actually
                // resolved to. A sibling pointing at either adds no new candidate.
                if (IsSameUrl(url, pinnedRootDomainUrl) || IsSameUrl(url, pinnedRedirectUrl))
                    continue;

                // Remove from current position if already present (from homepage scan)
                var existingIdx = sortedList.FindIndex(l => IsSameUrl(l.Url, url));
                if (existingIdx >= 0)
                    sortedList.RemoveAt(existingIdx);

                sortedList.Insert(siblingInsertIndex, new DeterministicLinkScanner.LinkInfo(
                    url,
                    $"Sibling page ({fieldKey}) — known-good related page"));
                siblingInsertIndex++;
            }

            // Produce evidence for each discovered link
            var evidence = new List<DecisionData>();
            foreach (var link in sortedList)
            {
                evidence.Add(new DecisionData
                {
                    Id = $"link-{evidence.Count}-{Guid.NewGuid():N}",
                    Source = providerUrl,
                    Type = "CandidateLink",
                    Content = link.Url,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ActionId = context.NodeId,
                    Metadata = new Dictionary<string, string>
                    {
                        ["description"] = link.Description ?? ""
                    }
                });
            }

            // Store only the candidate count in state — NOT the URL list.
            // The full ordered queue is available as CandidateLink evidence in the data store
            // (rendered in the Data section of the classify prompt). Storing URLs in state
            // caused the LLM to classify based on candidate URLs it saw in the state text
            // rather than the actual fetched page content.
            context.State.Properties["candidateCount"] = sortedList.Count;
            context.State.Properties["candidateIndex"] = 0;

            return new DecisionActionResult(
                evidence,
                new Dictionary<string, string>
                {
                    ["scanResult"] = "success",
                    ["linkCount"] = sortedList.Count.ToString()
                },
                DecisionActionStatus.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["scanResult"] = "error" },
                DecisionActionStatus.TransientFailure,
                ex.Message);
        }
    }

    /// <summary>
    /// Compares two URLs ignoring a trailing slash, matching the normalization the
    /// validator uses when it detects a redirect.
    /// </summary>
    private static bool IsSameUrl(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(
            left.TrimEnd('/'),
            right.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sorts links by relevance to the field kind. Links whose URL path or description
    /// match more field-kind keywords rank higher. Links with no keyword match keep
    /// their original scan order.
    /// </summary>
    private static IReadOnlyList<DeterministicLinkScanner.LinkInfo> SortByRelevance(
        IReadOnlyList<DeterministicLinkScanner.LinkInfo> links,
        string fieldKind,
        IReadOnlyList<string> relevanceTerms)
    {
        if (string.IsNullOrWhiteSpace(fieldKind))
            return links;

        var keywords = FieldUrlRelevance.BuildKeywords(fieldKind, relevanceTerms);
        if (keywords.Count == 0)
            return links;

        return links
            .Select((link, index) => (link, index, score: ScoreLink(link, keywords)))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)
            .Select(x => x.link)
            .ToList();
    }

    /// <summary>
    /// Reads the field's extra relevance vocabulary, which the research service passes
    /// through from <c>Config/field-definitions.json</c>. Providers name the same
    /// destination in many words, so the field name alone is not a usable relevance signal.
    /// </summary>
    private static IReadOnlyList<string> ReadRelevanceTerms(
        IReadOnlyDictionary<string, string> templateParameters)
    {
        if (!templateParameters.TryGetValue("relevanceTerms", out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Whether the profile allows a sibling page to be queued as a candidate. Absent the
    /// parameter the long-standing behaviour applies: a topically related page is worth
    /// classifying, because it may itself be the answer.
    /// </summary>
    private static bool SiblingPagesMayBeAnswer(
        IReadOnlyDictionary<string, string> templateParameters)
        => !templateParameters.TryGetValue("siblingPageMayBeAnswer", out var raw)
           || !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);

    private static int ScoreLink(DeterministicLinkScanner.LinkInfo link, IReadOnlyList<string> keywords)
    {
        var urlPath = FieldUrlRelevance.GetPath(link.Url);
        var description = (link.Description ?? string.Empty).ToLowerInvariant();

        var score = FieldUrlRelevance.CountKeywordMatches(keywords, urlPath, description);

        // Penalise URL paths that are structurally unlikely to be pricing pages.
        // This prevents API reference and model pages from ranking alongside actual
        // pricing pages when keyword scores would otherwise tie.
        if (IsAlwaysIrrelevantPath(urlPath))
        {
            score -= 10;
        }
        // A documentation path is demoted only when it does not itself name the field's
        // destination (e.g. /docs/ai-gateway/pricing): many providers host their real
        // pricing page under a documentation path, and sinking it below generic
        // marketing pages means the tree never reaches it within budget. Path shape is
        // only a ranking hint — the classifier still decides what the page is.
        else if (IsDocumentationPath(urlPath)
            && FieldUrlRelevance.CountKeywordMatches(keywords, urlPath) == 0)
        {
            score -= 10;
        }

        return score;
    }

    /// <summary>
    /// Returns true when the URL path is a structural section that never denotes the
    /// field's destination (API reference, model description, guide, SDK integration,
    /// signup/onboarding), regardless of the field being researched. Such pages are
    /// always demoted in the candidate ranking.
    /// </summary>
    private static bool IsAlwaysIrrelevantPath(string urlPath)
    {
        return urlPath.Contains("/api-reference/", StringComparison.OrdinalIgnoreCase)
            || urlPath.Contains("/models/", StringComparison.OrdinalIgnoreCase)
            || urlPath.Contains("/guides/", StringComparison.OrdinalIgnoreCase)
            || urlPath.Contains("/sdks-and-apis/", StringComparison.OrdinalIgnoreCase)
            || urlPath.Contains("/signup", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the URL path sits under a documentation section. Unlike
    /// <see cref="IsAlwaysIrrelevantPath"/>, a documentation path can legitimately host
    /// the destination page (e.g. <c>/docs/pricing</c>), so it is only demoted when the
    /// path itself does not name the destination.
    /// </summary>
    private static bool IsDocumentationPath(string urlPath)
    {
        return urlPath.Contains("/docs/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the URL path indicates a content-only page (blog post,
    /// news article, press release) that is structurally unlikely to be a pricing,
    /// API endpoint, or documentation page. Filtering these early prevents the
    /// decision tree from wasting its node-visits budget on pages the LLM
    /// classifier would correctly but expensively reject as "unrelated".
    /// </summary>
    private static bool IsContentOnlyPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Normalize: ensure leading and trailing slash so segment matching
        // works for both "/blog" (exact path) and "/blog/post" (prefix).
        var path = uri.AbsolutePath.ToLowerInvariant();
        if (!path.StartsWith('/')) path = "/" + path;
        if (!path.EndsWith('/')) path += "/";

        return path.Contains("/blog/")
            || path.Contains("/news/")
            || path.Contains("/articles/")
            || path.Contains("/press/")
            || path.Contains("/media/");
    }

    /// <summary>
    /// Scans one page for links, reusing the shared per-provider scan cache so a page
    /// another tree has already scanned costs no extra HTTP call.
    /// </summary>
    private async Task<IReadOnlyList<DeterministicLinkScanner.LinkInfo>> ScanLinksCachedAsync(
        string url,
        CancellationToken cancellationToken)
    {
        var cachedScan = _cache.GetScan(url);
        if (cachedScan is not null)
        {
            return cachedScan.Links
                .Select(l => new DeterministicLinkScanner.LinkInfo(l.Url, l.Description))
                .ToList();
        }

        var scanResult = await _scanner.ScanAsync(url, cancellationToken);

        // Cache the raw scan results so subsequent trees skip the HTTP call.
        _cache.SetScan(url, new ScanCacheEntry
        {
            Links = scanResult.Links
                .Select(l => (l.Url, l.Description))
                .ToList(),
            Error = scanResult.Error
        });

        return scanResult.Links;
    }

    /// <summary>
    /// Reads the field's current value from the template parameters. The resolver writes
    /// "none" when the field is empty and "-" marks a field that does not apply to the
    /// provider; neither names a page that can be scanned.
    /// </summary>
    private static bool TryGetCurrentFieldUrl(
        IReadOnlyDictionary<string, string> templateParameters,
        out string currentUrl)
    {
        currentUrl = string.Empty;

        if (!templateParameters.TryGetValue("currentValue", out var raw))
            return false;

        raw = raw.Trim();
        if (raw.Length == 0
            || string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase)
            || raw == ProviderJsonFields.NotApplicable)
            return false;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || !uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return false;

        currentUrl = raw;
        return true;
    }

    /// <summary>
    /// Parses the siblingUrls template parameter (format: "field1=url1;field2=url2")
    /// into a dictionary of field name → URL pairs.
    /// </summary>
    private static bool TryGetSiblingUrls(
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
}
