namespace ScraperTool.Services;

public sealed record DataValidationResult(
    bool Success,
    string? Analysis,
    IReadOnlyList<string> Issues,
    int PromptTokens,
    int CompletionTokens);
