using AIProviderConnect.Models;

namespace ScraperTool.Services;

/// <summary>
/// Aggregate result of a <see cref="ProviderBatchAnalysisService"/> batch run.
/// </summary>
public sealed record BatchAnalysisResult(
    int SuggestionCount,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    decimal InputCost,
    decimal OutputCost,
    decimal TotalCost,
    string CostNote,
    PriceSource PriceSource,
    TimeSpan Elapsed);
