using AIProviderConnect.Models;

namespace ScraperTool.Services.UrlResearch;

public sealed class ResearchContext
{
    public int CompletionTokens { get; set; }

    public string CurrentValue { get; init; } = string.Empty;

    public string Field { get; init; } = string.Empty;

    public required ValidationIssue Issue { get; init; }

    public string ModelName { get; init; } = string.Empty;

    public IProgress<string>? Progress { get; set; }

    public int PromptTokens { get; set; }

    public required ProviderDefinition Provider { get; init; }

    /// <summary>
    /// Research metadata (website, pricing URLs, dynamic-catalog flag, etc.) for the provider,
    /// resolved from the catalog. May be null when the provider has no metadata entry.
    /// </summary>
    public ProviderResearchMetadata? Research { get; init; }

    public string ProviderId { get; init; } = string.Empty;

    public string Region { get; init; } = "unknown";

    public ResearchSession? Session { get; set; }

    public List<string> Steps { get; } = [];

    public string? Website { get; init; }

    /// <summary>
    /// When the validation issue is a redirect, this stores the redirect target URL
    /// so the decision tree can evaluate it as a candidate.
    /// </summary>
    public string? RedirectTargetUrl { get; init; }

    /// <summary>
    /// When the validation issue is WebsiteIsSubdomain, this stores the computed
    /// root domain URL so the decision tree can evaluate it as the top-priority candidate.
    /// </summary>
    public string? SuggestedRootDomain { get; init; }

    public void Report(string step)
    {
        Steps.Add(step);
        Progress?.Report(step);
    }
}
