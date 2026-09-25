using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed record AiUrlFixRunResult(
    int TotalSuggestions,
    int FailedCount,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    decimal TotalCost,
    string CostSourceLabel,
    string UsedModelName,
    TimeSpan Elapsed,
    List<AiSuggestion> Suggestions);
