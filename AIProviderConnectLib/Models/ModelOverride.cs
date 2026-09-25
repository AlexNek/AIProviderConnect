namespace AIProviderConnect.Models;

/// <summary>
/// A partial, consumer-supplied patch for one model in a provider's catalog. Every patchable
/// field is nullable so the merge can distinguish "not set" from "set to a value"; only non-null
/// fields are applied over a live <see cref="AIModel"/>. <see cref="Hidden"/> removes a matching
/// live model from the discovery result instead of patching it.
/// </summary>
public sealed record ModelOverride
{
    /// <summary>
    /// Gets the model id this override applies to (matches <see cref="AIModel.Id"/>).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the value that overrides <see cref="AIModel.DisplayName"/> when set.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the value that overrides <see cref="AIModel.Description"/> when set.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the value that overrides <see cref="AIModel.Modality"/> when set.
    /// </summary>
    public string? Modality { get; init; }

    /// <summary>
    /// Gets the value that overrides <see cref="AIModel.OwnedBy"/> when set.
    /// </summary>
    public string? OwnedBy { get; init; }

    /// <summary>
    /// Gets the value that overrides <see cref="AIModel.PromptPrice"/> when set.
    /// </summary>
    public decimal? PromptPrice { get; init; }

    /// <summary>
    /// Gets the value that overrides <see cref="AIModel.CompletionPrice"/> when set.
    /// </summary>
    public decimal? CompletionPrice { get; init; }

    /// <summary>
    /// Gets the value that overrides <see cref="AIModel.PriceUnit"/> when set.
    /// </summary>
    public EModelPriceUnit? PriceUnit { get; init; }

    /// <summary>
    /// Gets the value that overrides <see cref="AIModel.ContextWindow"/> when set.
    /// </summary>
    public int? ContextWindow { get; init; }

    /// <summary>
    /// Gets the value that overrides <see cref="AIModel.Capabilities"/> when set.
    /// </summary>
    public EModelCapability? Capabilities { get; init; }

    /// <summary>
    /// Gets a value indicating whether the matching live model is removed from the result.
    /// </summary>
    public bool Hidden { get; init; }
}
