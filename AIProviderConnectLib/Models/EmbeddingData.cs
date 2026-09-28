namespace AIProviderConnect.Models;

/// <summary>
/// Represents a single embedding vector with its index in the batch.
/// </summary>
public sealed record EmbeddingData
{
    /// <summary>
    /// Gets the position of this vector in the input batch.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Gets the embedding vector.
    /// </summary>
    public float[] Embedding { get; init; } = [];
}
