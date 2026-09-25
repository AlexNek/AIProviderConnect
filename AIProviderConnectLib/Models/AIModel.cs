namespace AIProviderConnect.Models;

/// <summary>
/// Represents metadata about an AI model available from a provider.
/// </summary>
public sealed record AIModel
{
    /// <summary>
    /// Gets structured capability flags for this model.
    /// Replaces provider-level booleans (SupportsVision, SupportsTools, SupportsStreaming).
    /// </summary>
    public EModelCapability Capabilities { get; init; }

    /// <summary>
    /// Gets the price per million completion tokens.
    /// </summary>
    public decimal? CompletionPrice { get; init; }

    /// <summary>
    /// Gets the maximum context window size in tokens.
    /// </summary>
    public int? ContextWindow { get; init; }

    /// <summary>
    /// Gets the description of the model.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the human-readable display name of the model.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the unique identifier of the model.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the input/output modalities (e.g. "text+image->text").
    /// </summary>
    public string? Modality { get; init; }

    /// <summary>
    /// Gets the organization or provider that owns the model.
    /// </summary>
    public string? OwnedBy { get; init; }

    /// <summary>
    /// Gets the unit of measure for pricing.
    /// </summary>
    public EModelPriceUnit PriceUnit { get; init; } = EModelPriceUnit.Per1M;

    /// <summary>
    /// Gets the price per million prompt tokens.
    /// </summary>
    public decimal? PromptPrice { get; init; }

    /// <summary>
    /// Gets the ID of the provider this model belongs to.
    /// </summary>
    public string ProviderId { get; init; } = string.Empty;
}
