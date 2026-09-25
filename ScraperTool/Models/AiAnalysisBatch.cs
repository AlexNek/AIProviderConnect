using AIProviderConnect.Models;

namespace ScraperTool.Models;

public sealed class AiAnalysisBatch
{
    public bool AllReviewed =>
        Suggestions.Count > 0 && Suggestions.All(s => s.IsApproved || s.IsRejected);

    public bool AnalysisSuccess { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public List<AiSuggestion> Suggestions { get; set; } = [];

    public UsageInfo? Usage { get; set; }
}
