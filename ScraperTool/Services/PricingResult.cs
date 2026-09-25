namespace ScraperTool.Services;

public sealed record PricingResult(
    bool Success,
    string? ExtractedData,
    IReadOnlyList<string> Warnings,
    int PromptTokens,
    int CompletionTokens);
