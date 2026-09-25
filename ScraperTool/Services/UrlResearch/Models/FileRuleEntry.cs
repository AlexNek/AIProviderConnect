using ScraperTool.Services.UrlFix;

namespace ScraperTool.Services.UrlResearch.Models;

/// <summary>
/// DTO for URL intelligence rules loaded from JSON config file.
/// </summary>
public sealed class FileRuleEntry
{
    public required string Description { get; set; }

    public required string DisplayName { get; set; }

    public bool IsEnabled { get; set; } = true;

    public int Priority { get; set; }

    public required UrlIntelligenceRuleKind RuleKind { get; set; }
}
