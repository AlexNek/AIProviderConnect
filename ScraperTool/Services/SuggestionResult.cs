using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed class SuggestionResult
{
    public string? CostSourceLabel { get; set; }

    public int FailedCount { get; set; }

    public List<AiSuggestion> Suggestions { get; set; } = [];

    public int TotalCompletionTokens { get; set; }

    public decimal TotalCost { get; set; }

    public int TotalPromptTokens { get; set; }

    public int TotalSuggestions { get; set; }

    public string UsedModelName { get; set; } = string.Empty;
}
