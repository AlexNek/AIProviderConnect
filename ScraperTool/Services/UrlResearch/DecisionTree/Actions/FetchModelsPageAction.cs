using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using ScraperTool.Services.UrlResearch.DecisionTree.Formatting;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Fetches the provider's models listing page and extracts model names from the content.
/// Used for data verification trees when no API model discovery endpoint is available.
/// Uses <see cref="Quality.ICandidateRegionContentSelector"/> to extract bounded candidate regions
/// for downstream LLM classification instead of sending the full page.
/// Falls back to full Markdown when extracted regions are dominated by UI chrome.
/// Produces PageText evidence with the extracted content.
/// </summary>
public sealed class FetchModelsPageAction : IDecisionAction
{
    private const int MaxModelsPageContentSummaryLength = 500;

    private readonly IWebContentFetcher _fetcher;
    private readonly ITextSummarizer _textSummarizer;
    private readonly ICandidateRegionContentSelector _regionContentSelector;

    public string Key => "fetchModelsPage";

    public FetchModelsPageAction(
        IWebContentFetcher fetcher,
        ITextSummarizer textSummarizer,
        ICandidateRegionContentSelector regionContentSelector)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _textSummarizer = textSummarizer ?? throw new ArgumentNullException(nameof(textSummarizer));
        _regionContentSelector = regionContentSelector ?? throw new ArgumentNullException(nameof(regionContentSelector));
    }

    public async Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!context.TemplateParameters.TryGetValue("modelsPageUrl", out var modelsPageUrl)
            || string.IsNullOrWhiteSpace(modelsPageUrl))
        {
            return new DecisionActionResult(
                null,
                null,
                DecisionActionStatus.PermanentFailure,
                "Missing 'modelsPageUrl' template parameter.");
        }

        // If we've already attempted this fetch and failed, don't retry — fall through to web search.
        if (context.State.Properties.ContainsKey("modelsPageAttempted"))
        {
            return new DecisionActionResult(
                null,
                null,
                DecisionActionStatus.PermanentFailure,
                $"Models page fetch already attempted — falling back to web search. ({modelsPageUrl})");
        }

        // Mark that we've attempted this fetch (even if it fails).
        context.State.Properties["modelsPageAttempted"] = true;

        try
        {
            var markdownTask = _fetcher.FetchAsAsync(modelsPageUrl, EContentFormat.Markdown, ct: cancellationToken);
            var htmlTask = _fetcher.FetchAsAsync(modelsPageUrl, EContentFormat.Html, ct: cancellationToken);
            await Task.WhenAll(markdownTask, htmlTask);

            var markdownResult = markdownTask.Result;
            var htmlResult = htmlTask.Result;

            if (!markdownResult.Success || string.IsNullOrWhiteSpace(markdownResult.Content))
            {
                var failDetail = markdownResult.ErrorMessage ?? "empty response";
                return new DecisionActionResult(
                    null,
                    null,
                    DecisionActionStatus.TransientFailure,
                    $"{failDetail} — {modelsPageUrl}");
            }

            var selection = _regionContentSelector.Select(
                markdownResult.Content,
                htmlResult.Success ? htmlResult.Content : null,
                Uri.TryCreate(modelsPageUrl, UriKind.Absolute, out var uri) ? uri : null);
            var regionCount = selection.RegionCount;
            var llmContent = selection.Content;

            context.State.Properties["modelsPageContent"] = _textSummarizer.Summarize(llmContent, MaxModelsPageContentSummaryLength);
            context.State.Properties["modelsPageUrl"] = modelsPageUrl;

            var pageData = new DecisionData
            {
                Id = $"models-page-{Guid.NewGuid():N}",
                Source = modelsPageUrl,
                Type = "PageText",
                Content = markdownResult.Content,
                CreatedAt = DateTimeOffset.UtcNow,
                ActionId = context.NodeId,
                Metadata = new Dictionary<string, string>
                {
                    ["url"] = modelsPageUrl,
                    ["contentType"] = "models-page",
                    ["candidateRegions"] = regionCount.ToString()
                }
            };

            return new DecisionActionResult(
                new[] { pageData },
                new Dictionary<string, string>
                {
                    ["fetchResult"] = "success",
                    ["url"] = modelsPageUrl,
                    ["contentLength"] = markdownResult.Content.Length.ToString(),
                    ["candidateRegions"] = regionCount.ToString()
                },
                DecisionActionStatus.Success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DecisionActionResult(
                null,
                null,
                DecisionActionStatus.TransientFailure,
                $"{ex.Message} — {modelsPageUrl}");
        }
    }
}
