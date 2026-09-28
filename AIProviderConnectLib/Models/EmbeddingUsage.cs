namespace AIProviderConnect.Models;

/// <summary>
/// Represents token usage information for an embedding request.
/// </summary>
public sealed record EmbeddingUsage
{
    /// <summary>
    /// Gets the number of input tokens consumed by the batch.
    /// </summary>
    public int PromptTokens { get; init; }
}
