using ScraperTool.Models;

namespace ScraperTool.Services.UrlResearch;

public sealed record UrlResearchResult(
    string? SuggestedValue,
    string? Reason,
    bool Success,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    IReadOnlyList<string> Steps,
    IReadOnlyList<AiSuggestion> Suggestions);
