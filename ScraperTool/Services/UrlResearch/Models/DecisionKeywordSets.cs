namespace ScraperTool.Services.UrlResearch.Models;

/// <summary>
/// Editable decision data for deterministic URL-research verdicts.
/// The matching algorithms live in code; the words they match on live in
/// <c>Config/decision-keywords.json</c> (loaded via the metadata provider) —
/// this class is a pure model with no defaults, so the JSON is the single
/// source of truth.
/// </summary>
public sealed class DecisionKeywordSets
{
    /// <summary>
    /// Text markers that name a host as the API base URL in documentation
    /// (SDK samples, config tables), e.g. <c>base_url = "https://..."</c>.
    /// </summary>
    public IReadOnlyList<string> ApiBaseUrlMarkers { get; init; } = [];

    /// <summary>
    /// Words that indicate pay-as-you-go (per-usage) API pricing — used to
    /// decide that a subscription pricing page does not exist.
    /// </summary>
    public IReadOnlyList<string> PayAsYouGoKeywords { get; init; } = [];

    /// <summary>
    /// URL path fragments that identify a pricing page when selecting which
    /// discovered link to follow.
    /// </summary>
    public IReadOnlyList<string> PricingUrlKeywords { get; init; } = [];

    /// <summary>
    /// Words that indicate subscription plans/tiers in page content — used to
    /// score sections and pick the subscription pricing page.
    /// </summary>
    public IReadOnlyList<string> SubscriptionKeywords { get; init; } = [];
}
