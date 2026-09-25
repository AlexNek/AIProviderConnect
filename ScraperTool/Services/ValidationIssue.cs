namespace ScraperTool.Services;

public sealed record ValidationIssue(string FileName, string Code, string Message)
{
    public DateTime? AiAttemptedAt { get; set; }

    public string? CurrentValue { get; init; }

    /// <summary>
    /// When the validation issue is a redirect (e.g. PricingUrlRedirected),
    /// this stores the redirect target URL so the research pipeline can evaluate it.
    /// </summary>
    public string? RedirectTargetUrl { get; init; }

    /// <summary>
    /// When the validation issue is WebsiteIsSubdomain, this stores the computed
    /// root domain URL (e.g. "https://kimi.ai" from "https://platform.kimi.ai")
    /// so the research pipeline can evaluate it as the top-priority candidate.
    /// </summary>
    public string? SuggestedRootDomain { get; set; }

    public string Display => $"[{Code}] {FileName}: {Message}";

    public string? Field { get; init; }

    public string? SuggestedValue { get; set; }

    public string? SuggestionReason { get; set; }

    public string? SuggestionSeverity { get; set; }

    public int SuggestionStatus { get; set; }

    /// <summary>
    /// Set when the research run reached a conclusion about this issue's field and that
    /// conclusion was "no value found", as opposed to the research itself breaking. Both leave
    /// no suggestion behind and both are recorded as a failed suggestion status, but they are
    /// not the same news: one is an answer about the provider, the other is a defect in this
    /// run, and only the second is an error.
    /// </summary>
    public bool ResearchCompletedWithoutValue { get; set; }
}
