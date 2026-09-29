namespace AIProviderConnect.Models;

/// <summary>
/// Represents token usage information from an AI provider response.
/// </summary>
public sealed record UsageInfo
{
    /// <summary>
    /// Gets the number of tokens generated in the completion.
    /// </summary>
    public int CompletionTokens { get; init; }

    /// <summary>
    /// Gets the number of tokens used in the prompt.
    /// </summary>
    public int PromptTokens { get; init; }

    /// <summary>
    /// Gets the total number of tokens used.
    /// </summary>
    public int TotalTokens { get; init; }

    /// <summary>
    /// Gets the per-call cost in USD reported by the provider, or <c>null</c> when the provider
    /// does not report a cost. Chat, model-discovery, and embeddings parsers leave this unset;
    /// the decisions parser populates it when the response carries a cost.
    /// </summary>
    public decimal? Cost { get; init; }
}
