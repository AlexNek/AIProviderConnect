using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using ScraperTool.Models;

namespace ScraperTool.Services;

/// <summary>
/// Runs a batch of AI provider analyses over every catalog provider that has a pricing URL,
/// accumulates token usage, resolves pricing, and logs progress and summary information.
/// Shared by the AI Analysis panel and the Check Data deep-analysis workflow.
/// </summary>
public sealed class ProviderBatchAnalysisService
{
    private readonly AiDefinitionAnalyzer _analyzer;

    private readonly IProviderCatalog _catalog;

    private readonly ModelPriceResolver _priceResolver;

    private readonly string _modelName;

    public ProviderBatchAnalysisService(
        AiDefinitionAnalyzer analyzer,
        IProviderCatalog catalog,
        ModelPriceResolver priceResolver,
        string modelName)
    {
        _analyzer = analyzer;
        _catalog = catalog;
        _priceResolver = priceResolver;
        _modelName = modelName;
    }

    /// <summary>
    /// Filters catalog providers that have a usable API pricing URL, analyses each one,
    /// logs progress and a cost summary, and returns aggregate results.
    /// </summary>
    /// <param name="ct">Cancellation token; cancellation propagates to the analyzer.</param>
    /// <param name="onProgress">
    /// Called before each provider analysis with (currentIndex, totalCount, displayName, elapsed).
    /// The caller uses this to update its status text.
    /// </param>
    /// <param name="onSuggestion">
    /// Called for each suggestion produced by a successful analysis.
    /// The caller typically adds it to an ObservableCollection.
    /// </param>
    /// <param name="logger">Operation logger for progress and summary lines.</param>
    /// <param name="timer">
    /// An <see cref="IOperationTimer"/> the caller controls. The service reads it for progress
    /// messages and uses its final value for the summary. The caller is responsible for starting,
    /// pausing, and stopping the timer.
    /// </param>
    public async Task<BatchAnalysisResult> RunAsync(
        CancellationToken ct,
        Action<int, int, string, TimeSpan> onProgress,
        Action<AiSuggestion> onSuggestion,
        IOperationLogger logger,
        IOperationTimer timer)
    {
        var providers = _catalog.All.Where(p =>
        {
            var url = _catalog.GetResearchMetadata(p.Id)?.ApiPricingUrl;
            return !string.IsNullOrWhiteSpace(url)
                   && url != ProviderJsonFields.NotApplicable;
        }).ToList();

        logger.Add($"Analyzing {providers.Count} providers...");
        var suggestionCount = 0;
        var totalPromptTokens = 0;
        var totalCompletionTokens = 0;

        for (var idx = 0; idx < providers.Count; idx++)
        {
            ct.ThrowIfCancellationRequested();

            var provider = providers[idx];
            onProgress(idx, providers.Count, provider.DisplayName, timer.Elapsed);
            logger.Add($"  [{idx + 1}/{providers.Count}] {provider.Id}: Sending to AI...");

            var result = await _analyzer.AnalyzeProviderAsync(provider, ct: ct);

            if (result.AnalysisSuccess)
            {
                logger.Add($"    AI returned {result.Suggestions.Count} suggestion(s).");
                if (result.Usage is not null)
                {
                    totalPromptTokens += result.Usage.PromptTokens;
                    totalCompletionTokens += result.Usage.CompletionTokens;
                    logger.Add(
                        $"    Tokens: {result.Usage.PromptTokens} prompt + {result.Usage.CompletionTokens} completion");
                }

                foreach (var suggestion in result.Suggestions)
                {
                    onSuggestion(suggestion);
                    suggestionCount++;
                }
            }
            else
            {
                logger.Add($"    Analysis failed: {result.ErrorMessage}");
            }
        }

        var (inPrice, outPrice, source) = await _priceResolver.ResolveAsync(_modelName);
        var inputCost = totalPromptTokens * inPrice / 1_000_000m;
        var outputCost = totalCompletionTokens * outPrice / 1_000_000m;
        var totalCost = inputCost + outputCost;
        var costNote = source == PriceSource.Confirmed ? "" : " (estimated)";

        logger.Add("=== Analysis complete ===");
        logger.Add($"  Elapsed: {timer.Elapsed.Minutes}m {timer.Elapsed.Seconds}s");
        logger.Add("  Profile model used");
        logger.Add(
            $"  Total tokens: {totalPromptTokens} prompt + {totalCompletionTokens} completion = {totalPromptTokens + totalCompletionTokens} total");
        if (source != PriceSource.Unknown)
            logger.Add(
                $"  Cost: ${inputCost:F4} input + ${outputCost:F4} output = ${totalCost:F4}{costNote}");

        return new BatchAnalysisResult(
            suggestionCount,
            totalPromptTokens,
            totalCompletionTokens,
            inputCost,
            outputCost,
            totalCost,
            costNote,
            source,
            timer.Elapsed);
    }
}
