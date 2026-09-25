using System.Text.Json.Serialization;

namespace AIProviderConnect.Models;

/// <summary>Research metadata from the flat provider manifest, separate from runtime configuration.</summary>
public sealed record ProviderResearchMetadata
{
    /// <summary>
    /// Gets the URL for the provider's API pricing information.
    /// </summary>
    [JsonPropertyName("apiPricingUrl")]
    public string? ApiPricingUrl { get; init; }

    /// <summary>
    /// Gets the URL for the provider's documentation.
    /// </summary>
    [JsonPropertyName("documentationUrl")]
    public string? DocumentationUrl { get; init; }

    /// <summary>
    /// Gets whether the provider has a free tier.
    /// </summary>
    [JsonPropertyName("hasFreeTier")]
    public bool HasFreeTier { get; init; }

    /// <summary>
    /// Gets whether the provider's model catalog is dynamic and cannot be
    /// verified by any external method (API, web search, or page scraping).
    /// True for localhost/self-hosted providers where the catalog depends
    /// on user-downloaded models.
    /// </summary>
    [JsonPropertyName("isDynamicModelCatalog")]
    public bool IsDynamicModelCatalog { get; init; }

    /// <summary>
    /// Gets the URL for logging into the provider's console.
    /// </summary>
    [JsonPropertyName("loginUrl")]
    public string? LoginUrl { get; init; }

    /// <summary>
    /// Gets the minimum commitment required.
    /// </summary>
    [JsonPropertyName("minimumCommitment")]
    public string? MinimumCommitment { get; init; }

    /// <summary>
    /// Gets the number of models available from the provider.
    /// </summary>
    [JsonPropertyName("minModelCount")]
    public int MinModelCount { get; init; }

    /// <summary>
    /// Gets the description of available models.
    /// </summary>
    [JsonPropertyName("modelDescription")]
    public string? ModelDescription { get; init; }

    /// <summary>
    /// Gets notes about model discovery.
    /// </summary>
    [JsonPropertyName("modelDiscoveryNotes")]
    public string? ModelDiscoveryNotes { get; init; }

    /// <summary>
    /// Gets whether the provider offers pay-as-you-go pricing.
    /// </summary>
    [JsonPropertyName("payAsYouGo")]
    public bool PayAsYouGo { get; init; }

    /// <summary>
    /// Gets a description of the provider's pay-as-you-go pricing terms.
    /// </summary>
    [JsonPropertyName("payAsYouGoDescription")]
    public string? PayAsYouGoDescription { get; init; }

    /// <summary>
    /// Gets region-specific API endpoints, keyed by region code (e.g. "china", "us", "eu", "intl").
    /// Only present for providers with regional API variants.
    /// URLs may contain <c>{customer}</c> placeholder — the AI fixer extracts the actual value
    /// from the user's configured <c>baseUrl</c> by matching the URL template structure.
    /// </summary>
    [JsonPropertyName("regionalEndpoints")]
    public IReadOnlyDictionary<string, string>? RegionalEndpoints { get; init; }

    /// <summary>
    /// Gets the URL for the provider's subscription pricing information.
    /// </summary>
    [JsonPropertyName("subscriptionPricingUrl")]
    public string? SubscriptionPricingUrl { get; init; }

    /// <summary>
    /// Gets whether the provider supports fine-tuning.
    /// </summary>
    [JsonPropertyName("supportsFineTuning")]
    public bool SupportsFineTuning { get; init; }

    /// <summary>
    /// Gets the provider's website URL.
    /// </summary>
    [JsonPropertyName("website")]
    public string? Website { get; init; }
}
