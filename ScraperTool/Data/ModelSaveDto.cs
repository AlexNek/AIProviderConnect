namespace ScraperTool.Data;

public sealed class ModelSaveDto
{
    public decimal? CompletionPrice { get; init; }

    public int? ContextWindow { get; init; }

    public string? Description { get; init; }

    public required string DisplayName { get; init; }

    public required string ModelId { get; init; }

    public string? OwnedBy { get; init; }

    public decimal? PromptPrice { get; init; }
}
