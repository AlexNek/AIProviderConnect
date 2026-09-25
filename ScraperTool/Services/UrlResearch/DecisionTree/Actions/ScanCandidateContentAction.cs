using System.Text.RegularExpressions;

using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// After fetching a candidate page, scans its content for additional URLs.
/// This enables two-level URL extraction: sibling pages → candidate pages → API URLs.
/// For example, a documentation page (quickstart-preparation) may contain API base URLs
/// in code examples that are not present on the sibling pages themselves.
/// </summary>
/// <remarks>
/// Runs once per candidate evaluation cycle, guarded by the <c>candidateContentScanned</c>
/// state flag to prevent infinite re-scanning when newly discovered candidates also fail.
/// </remarks>
public sealed partial class ScanCandidateContentAction : IDecisionAction
{
    private const int MaxExtractedUrls = 15;

    private readonly IWebContentFetcher _fetcher;
    private readonly ICandidateUrlProvider _candidateUrlProvider;

    public string Key => "scanCandidateContent";

    public ScanCandidateContentAction(
        IWebContentFetcher fetcher,
        ICandidateUrlProvider candidateUrlProvider)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _candidateUrlProvider = candidateUrlProvider
            ?? throw new ArgumentNullException(nameof(candidateUrlProvider));
    }

    public async Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        // Prevent infinite loop: only scan once per candidate evaluation cycle.
        // When newly discovered candidates are tried and fail, the second pass
        // skips this action and falls through to sibling/web-search fallback.
        if (IsCandidateContentAlreadyScanned(context))
        {
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["scanResult"] = "already-scanned" },
                DecisionActionStatus.Success);
        }

        if (!context.State.Properties.TryGetValue("lastFetchedUrl", out var urlObj)
            || urlObj is not string lastFetchedUrl
            || string.IsNullOrWhiteSpace(lastFetchedUrl))
        {
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["scanResult"] = "no-candidate-url" },
                DecisionActionStatus.Success);
        }

        // An API endpoint carries a response body, not a page: rendering it in a browser to look
        // for links costs a full page load and can only find the URL the probe is about to ask.
        var isBaseUrlField = context.TemplateParameters.TryGetValue("fieldKind", out var fieldKindValue)
                             && string.Equals(fieldKindValue, "baseUrl", StringComparison.Ordinal);
        if (isBaseUrlField && ScanSiblingContentAction.IsApiLikeUrl(lastFetchedUrl))
        {
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["scanResult"] = "endpoint-has-no-page" },
                DecisionActionStatus.Success);
        }

        try
        {
            var content = await _fetcher.FetchAsAsync(
                lastFetchedUrl, EContentFormat.Markdown, ct: cancellationToken);

            if (!content.Success || string.IsNullOrWhiteSpace(content.Content))
            {
                return new DecisionActionResult(
                    null,
                    new Dictionary<string, string> { ["scanResult"] = "fetch-failed" },
                    DecisionActionStatus.Success);
            }

            // Same ranking as the sibling scan: this page's own endpoints are the reason it was
            // fetched, and the cap below keeps only the first MaxExtractedUrls of them.
            var extractedUrls = ScanSiblingContentAction.OrderProbeCandidatesFirst(
                content.Content,
                ScanSiblingContentAction.ExtractUrlsFromContent(content.Content),
                QueryDocumentedModelsEndpointAction.CollectProviderHosts(context.TemplateParameters));

            // Build seen-URL set from existing candidates + visited URLs, keyed by the page each
            // address points at: a link to /go/ is the page already queued as /go and already
            // judged, and re-queueing it restarts the pool the scan is meant to widen.
            var seenUrls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidateUrl in _candidateUrlProvider.GetCandidateUrls(context.Data))
                seenUrls.Add(CandidateUrlNormalizer.Normalize(candidateUrl));

            if (context.State.Properties.TryGetValue("visitedUrls", out var visitedObj)
                && visitedObj is string visitedStr
                && !string.IsNullOrWhiteSpace(visitedStr))
            {
                foreach (var v in visitedStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    seenUrls.Add(CandidateUrlNormalizer.Normalize(v));
            }

            var evidence = new List<DecisionData>();
            foreach (var url in extractedUrls)
            {
                if (evidence.Count >= MaxExtractedUrls)
                    break;

                if (seenUrls.Add(CandidateUrlNormalizer.Normalize(url)))
                {
                    evidence.Add(new DecisionData
                    {
                        Id = $"candidate-content-{evidence.Count}-{Guid.NewGuid():N}",
                        Source = lastFetchedUrl,
                        Type = "CandidateLink",
                        Content = url,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ActionId = context.NodeId,
                        Metadata = new Dictionary<string, string>
                        {
                            ["description"] = $"Extracted from candidate page content ({lastFetchedUrl})"
                        }
                    });
                }
            }

            context.State.Properties["candidateContentScanned"] = true;

            if (evidence.Count == 0)
            {
                return new DecisionActionResult(
                    null,
                    new Dictionary<string, string> { ["scanResult"] = "no-new-urls" },
                    DecisionActionStatus.Success);
            }

            // Reset candidate index so the new URLs are tried from the beginning
            // of the newly expanded candidate pool.
            context.State.Properties["candidateIndex"] = 0;

            return new DecisionActionResult(
                evidence,
                new Dictionary<string, string>
                {
                    ["scanResult"] = "success",
                    ["extractedUrlCount"] = evidence.Count.ToString()
                },
                DecisionActionStatus.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Candidate content scan failure is non-fatal.
            context.State.Properties["candidateContentScanned"] = true;
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["scanResult"] = "error" },
                DecisionActionStatus.Success);
        }
    }

    private static bool IsCandidateContentAlreadyScanned(DecisionActionContext context)
    {
        if (!context.State.Properties.TryGetValue("candidateContentScanned", out var flag))
            return false;

        return flag is bool b && b
               || flag is string s && bool.TryParse(s, out var parsed) && parsed;
    }
}
