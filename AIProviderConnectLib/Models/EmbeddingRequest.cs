namespace AIProviderConnect.Models;

/// <summary>
/// Represents a request for text embeddings.
/// </summary>
public sealed record EmbeddingRequest
{
    /// <summary>
    /// Gets the embedding model identifier.
    /// Falls back to <see cref="Options.IEmbeddingsEndpointOptions.DefaultEmbeddingModel"/> when empty.
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Gets the input strings to embed.
    /// </summary>
    public required IReadOnlyList<string> Input { get; init; }
}
