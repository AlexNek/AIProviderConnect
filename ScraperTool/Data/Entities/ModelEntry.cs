using ScraperTool.Data.Enums;

namespace ScraperTool.Data.Entities;

public sealed class ModelEntry
{
    public decimal? CompletionPrice { get; set; }

    public int? ContextWindow { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? Description { get; set; }

    public required string DisplayName { get; set; }

    public int Id { get; set; }

    public DateTime? LastValidatedAt { get; set; }

    public required string ModelId { get; set; }

    public string? OwnedBy { get; set; }

    public decimal? PromptPrice { get; set; }

    public ProviderEntry Provider { get; set; } = null!;

    public int ProviderEntryId { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ModelValidationState ValidationState { get; set; } = ModelValidationState.Unknown;
}
