namespace ScraperTool.Data.Entities;

public sealed class TokenUsageEntry
{
    public int CompletionTokens { get; set; }

    public decimal Cost { get; set; }

    public string? CostSource { get; set; }

    public DateTime CreatedAt { get; set; }

    public int Id { get; set; }

    public required string ModelName { get; set; }

    public required string Operation { get; set; }

    public int PromptTokens { get; set; }

    public required string ProviderName { get; set; }

    public int TotalTokens => PromptTokens + CompletionTokens;
}
