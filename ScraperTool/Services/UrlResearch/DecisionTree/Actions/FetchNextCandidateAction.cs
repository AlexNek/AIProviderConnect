using System.Text.Json;

using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using ScraperTool.Services.UrlResearch.DecisionTree.Formatting;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Fetches the next candidate URL from the candidate queue stored as decision data.
/// Produces PageText evidence with the fetched content.
/// Uses <see cref="Quality.ICandidateRegionContentSelector"/> to extract bounded candidate regions
/// for LLM classification, but falls back to full Markdown when regions are low quality.
/// <para>
/// A reported success always carries content for the URL it names: candidates already fetched
/// in this run are stepped over inside the call, and a queue with nothing left to fetch fails
/// rather than succeeding with someone else's page in hand.
/// </para>
/// </summary>
public sealed class FetchNextCandidateAction : IDecisionAction
{
    private const int MaxFetchedContentSummaryLength = 500;
    private const char VisitedUrlSeparator = ',';

    private readonly IWebContentFetcher _fetcher;
    private readonly ITextSummarizer _textSummarizer;
    private readonly ICandidateRegionContentSelector _regionContentSelector;
    private readonly ICandidateUrlProvider _candidateUrlProvider;
    private readonly ProviderResearchCache _cache;

    public string Key => "fetchNextCandidate";

    public FetchNextCandidateAction(
        IWebContentFetcher fetcher,
        ITextSummarizer textSummarizer,
        ICandidateRegionContentSelector regionContentSelector,
        ICandidateUrlProvider candidateUrlProvider,
        ProviderResearchCache cache)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _textSummarizer = textSummarizer ?? throw new ArgumentNullException(nameof(textSummarizer));
        _regionContentSelector = regionContentSelector ?? throw new ArgumentNullException(nameof(regionContentSelector));
        _candidateUrlProvider = candidateUrlProvider ?? throw new ArgumentNullException(nameof(candidateUrlProvider));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        var candidateUrls = _candidateUrlProvider.GetCandidateUrls(context.Data);

        if (candidateUrls.Count == 0)
        {
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["fetchResult"] = "no-candidates" },
                DecisionActionStatus.PermanentFailure,
                "No candidates available.");
        }

        var currentIndex = GetCurrentIndex(context);
        var visitedUrls = GetVisitedUrls(context);
        var visitedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var visited in visitedUrls)
            visitedKeys.Add(CandidateUrlNormalizer.Normalize(visited));

        // Candidates this run already fetched are stepped over here rather than reported: a
        // "success" for an already-visited page leaves the evidence holding the previously
        // fetched page's content while the state names this one, so the classifier judges a page
        // it was not given and the run concludes against the wrong source.
        while (currentIndex < candidateUrls.Count
               && visitedKeys.Contains(CandidateUrlNormalizer.Normalize(candidateUrls[currentIndex])))
        {
            currentIndex++;
        }

        if (currentIndex >= candidateUrls.Count)
        {
            context.State.Properties["candidateIndex"] = candidateUrls.Count;
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["fetchResult"] = "exhausted" },
                DecisionActionStatus.PermanentFailure,
                "All candidates exhausted.");
        }

        var url = candidateUrls[currentIndex];

        // An API base URL (e.g. https://api.example.com or https://api.example.com/v1/...)
        // is not a fetchable web page — GETting its root typically returns 404/405 even
        // when the API itself is valid, which previously caused the correct baseUrl
        // candidate to be discarded before it could be probed. For the baseUrl field,
        // skip the page fetch and defer validation to probe-models-endpoint, which tests
        // {base}/v1/models directly and correctly interprets JSON / 401 (auth-gated) /
        // 4xx responses.
        var isBaseUrlField = context.TemplateParameters.TryGetValue("fieldKind", out var fieldKindValue)
                             && string.Equals(fieldKindValue, "baseUrl", StringComparison.Ordinal);
        if (isBaseUrlField && ScanSiblingContentAction.IsApiLikeUrl(url))
        {
            visitedUrls.Add(url);
            context.State.Properties["visitedUrls"] = FormatVisitedUrls(visitedUrls);
            context.State.Properties["candidateIndex"] = currentIndex + 1;
            context.State.Properties["lastFetchFailed"] = false;
            context.State.Properties["lastFetchedUrl"] = url;
            context.State.Properties["lastFetchedContent"] = "";
            context.State.Properties["lastVerifySucceeded"] = false;

            return new DecisionActionResult(
                null,
                new Dictionary<string, string>
                {
                    ["fetchResult"] = "api-candidate-deferred-to-probe",
                    ["url"] = url
                },
                DecisionActionStatus.Success);
        }

        try
        {
            // Check the shared cache first — another tree for the same provider
            // may have already fetched this page.
            var cachedPage = _cache.GetPageFetch(url);
            WebContent markdownResult;
            WebContent htmlResult;

            if (cachedPage is not null)
            {
                markdownResult = new WebContent(
                    cachedPage.Success,
                    cachedPage.MarkdownContent ?? string.Empty,
                    cachedPage.ErrorMessage,
                    cachedPage.FinalUrl ?? url);
                htmlResult = new WebContent(
                    cachedPage.Success,
                    cachedPage.HtmlContent ?? string.Empty,
                    cachedPage.ErrorMessage,
                    cachedPage.FinalUrl ?? url);
            }
            else
            {
                var markdownTask = _fetcher.FetchAsAsync(url, EContentFormat.Markdown, ct: cancellationToken);
                var htmlTask = _fetcher.FetchAsAsync(url, EContentFormat.Html, ct: cancellationToken);
                await Task.WhenAll(markdownTask, htmlTask);

                markdownResult = markdownTask.Result;
                htmlResult = htmlTask.Result;

                // Cache the raw fetch results so subsequent trees skip the HTTP calls.
                _cache.SetPageFetch(url, new PageFetchCacheEntry
                {
                    MarkdownContent = markdownResult.Content,
                    HtmlContent = htmlResult.Success ? htmlResult.Content : null,
                    ErrorMessage = markdownResult.ErrorMessage,
                    FinalUrl = markdownResult.FinalUrl,
                    Success = markdownResult.Success
                });
            }

            visitedUrls.Add(url);
            context.State.Properties["visitedUrls"] = FormatVisitedUrls(visitedUrls);

            // Reset verification state from any previous iteration so stale
            // values cannot leak through on the failure path.
            context.State.Properties["lastVerifySucceeded"] = false;

            if (!markdownResult.Success || string.IsNullOrWhiteSpace(markdownResult.Content))
            {
                context.State.Properties["candidateIndex"] = currentIndex + 1;
                context.State.Properties["lastFetchFailed"] = true;
                context.State.Properties["lastFetchedUrl"] = url;
                context.State.Properties["lastFetchedContent"] = "";

                // 4xx client errors are permanent — retrying the same dead URL
                // won't help. Advance to the next candidate immediately.
                var isPermanentError = IsClientError(markdownResult.ErrorMessage);

                return new DecisionActionResult(
                    null,
                    new Dictionary<string, string>
                    {
                        ["fetchResult"] = "failed",
                        ["url"] = url,
                        ["error"] = markdownResult.ErrorMessage ?? "empty response"
                    },
                    isPermanentError
                        ? DecisionActionStatus.PermanentFailure
                        : DecisionActionStatus.TransientFailure,
                    markdownResult.ErrorMessage);
            }

            var selection = _regionContentSelector.Select(
                markdownResult.Content,
                htmlResult.Success ? htmlResult.Content : null,
                Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null);
            var regionCount = selection.RegionCount;
            var llmContent = selection.Content;

            var pageText = new DecisionData
            {
                Id = $"page-{Guid.NewGuid():N}",
                Source = url,
                Type = "PageText",
                Content = llmContent,
                CreatedAt = DateTimeOffset.UtcNow,
                ActionId = context.NodeId,
                Metadata = new Dictionary<string, string>
                {
                    ["finalUrl"] = markdownResult.FinalUrl ?? url,
                    ["redirected"] = (!string.IsNullOrWhiteSpace(markdownResult.FinalUrl) && markdownResult.FinalUrl != url).ToString(),
                    ["candidateRegions"] = regionCount.ToString()
                }
            };

            context.State.Properties["candidateIndex"] = currentIndex + 1;
            context.State.Properties["lastFetchFailed"] = false;
            context.State.Properties["lastFetchedUrl"] = url;
            context.State.Properties["lastFetchedContent"] = _textSummarizer.Summarize(llmContent, MaxFetchedContentSummaryLength);

            return new DecisionActionResult(
                new[] { pageText },
                new Dictionary<string, string>
                {
                    ["fetchResult"] = "success",
                    ["url"] = url,
                    ["candidateRegions"] = regionCount.ToString()
                },
                DecisionActionStatus.Success);
        }
        catch (JsonException ex)
        {
            // Content parsing errors are permanent — retrying the same or next candidate
            // with the same broken content won't help. Advance to next candidate.
            context.State.Properties["candidateIndex"] = currentIndex + 1;
            context.State.Properties["lastFetchFailed"] = true;

            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["fetchResult"] = "parse-error" },
                DecisionActionStatus.TransientFailure,
                ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.State.Properties["candidateIndex"] = currentIndex + 1;
            context.State.Properties["lastFetchFailed"] = true;

            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["fetchResult"] = "error" },
                DecisionActionStatus.TransientFailure,
                ex.Message);
        }
    }

    /// <summary>
    /// Returns true when the error message indicates a 4xx client error
    /// (e.g. HTTP 404, 403, 410). These are permanent failures that should
    /// not be retried — the URL is gone.
    /// </summary>
    private static bool IsClientError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return false;

        // Match patterns like "HTTP 404", "HTTP 403", "HTTP 410", etc.
        if (errorMessage.Length >= 8
            && errorMessage.StartsWith("HTTP 4", StringComparison.OrdinalIgnoreCase)
            && char.IsDigit(errorMessage[6])
            && char.IsDigit(errorMessage[7]))
        {
            return true;
        }

        return false;
    }

    private static int GetCurrentIndex(DecisionActionContext context)
    {
        if (!context.State.Properties.TryGetValue("candidateIndex", out var indexObj) || indexObj is null)
            return 0;

        if (indexObj is int intVal)
            return intVal;

        if (indexObj is string strVal && int.TryParse(strVal, out var parsed))
            return parsed;

        return 0;
    }

    private static List<string> GetVisitedUrls(DecisionActionContext context)
    {
        if (context.State.Properties.TryGetValue("visitedUrls", out var visitedObj)
            && visitedObj is string visitedString
            && !string.IsNullOrWhiteSpace(visitedString))
        {
            return visitedString
                .Split(VisitedUrlSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(u => u.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .ToList();
        }

        return new List<string>();
    }

    /// <summary>
    /// Stores every visited address, complete. This string is read back as the record of the
    /// pages already handled — here, to step over them, and by
    /// <see cref="ScanCandidateContentAction"/>, to keep re-queuing them — so dropping entries
    /// from it or shortening one silently un-visits a page, and the run spends another fetch
    /// and another classification on a page it already judged.
    /// </summary>
    private static string FormatVisitedUrls(IReadOnlyList<string> visitedUrls)
        => string.Join(VisitedUrlSeparator.ToString(), visitedUrls);
}
