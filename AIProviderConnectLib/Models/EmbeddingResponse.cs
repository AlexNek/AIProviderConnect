namespace AIProviderConnect.Models;

/// <summary>
/// Represents an embedding response from an AI provider.
/// </summary>
public sealed record EmbeddingResponse
{
    /// <summary>
    /// Gets the embedding data, one entry per input string, ordered by Index.
    /// </summary>
    public IReadOnlyList<EmbeddingData> Data { get; init; } = [];

    /// <summary>
    /// Gets the model identifier that served the request.
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Gets the token usage information for the batch.
    /// </summary>
    public EmbeddingUsage Usage { get; init; } = new();
}
