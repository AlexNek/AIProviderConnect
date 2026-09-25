using ScraperTool.Data.Entities;
using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed class SuggestionProcessingService
{
    private readonly AiUrlFixService _aiUrlFix;

    private readonly AiDefinitionAnalyzer _analyzer;

    private readonly IOperationLogger _uiLogger;

    public SuggestionProcessingService(
        AiUrlFixService aiUrlFix,
        AiDefinitionAnalyzer analyzer,
        IOperationLogger uiLogger)
    {
        _aiUrlFix = aiUrlFix;
        _analyzer = analyzer;
        _uiLogger = uiLogger;
    }

    public List<ValidationIssue> GetBrokenUrls(List<ValidationIssue> lastBadIssues) =>
        lastBadIssues
            .Where(i => ValidationIssueCodes.UrlErrorCodes.Contains(i.Code))
            .OrderBy(i => i.FileName).ThenBy(i => i.Message)
            .ToList();

    public List<ValidationIssue> GetFailedItems(List<ValidationIssue> lastBadIssues) =>
        lastBadIssues
            .Where(i => ValidationIssueCodes.UrlErrorCodes.Contains(i.Code)
                        && i.SuggestionStatus == (int)IssueSuggestionStatus.Failed)
            .OrderBy(i => i.FileName).ThenBy(i => i.Message)
            .ToList();

    public async Task<SuggestionResult> RunAiFixAsync(
        List<ValidationIssue> issues,
        string primaryModel,
        string? fallbackModel,
        IProgress<AiUrlFixProgress> progress,
        CancellationToken ct = default)
    {
        // If primary is empty but fallback is set, use the fallback as the model.
        // This is configuration selection, not failover — model recovery is
        // exclusively the AI runtime's job, so no retries happen at this layer.
        var model = primaryModel;
        if (string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(fallbackModel))
        {
            progress?.Report(
                new AiUrlFixProgress(
                    $"Primary model not configured — using fallback model '{fallbackModel}'"));
            model = fallbackModel;
        }

        var result = await _aiUrlFix.RunAsync(issues, model, progress, ct);
        return ToResult(result);
    }

    private static SuggestionResult ToResult(AiUrlFixRunResult result)
    {
        return new SuggestionResult
                   {
                       Suggestions = result.Suggestions.ToList(),
                       TotalPromptTokens = result.TotalPromptTokens,
                       TotalCompletionTokens = result.TotalCompletionTokens,
                       TotalCost = result.TotalCost,
                       CostSourceLabel = result.CostSourceLabel,
                       UsedModelName = result.UsedModelName,
                       FailedCount = result.FailedCount,
                       TotalSuggestions = result.TotalSuggestions
                   };
    }
}
