using AIProviderConnect.Models;

namespace AIProviderConnect.Abstractions;

/// <summary>
/// Optional runtime capability for embeddings.
/// Use <c>is IEmbeddingProvider</c> to check at the call site.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Generates embeddings for the given input strings.
    /// </summary>
    /// <param name="request">The embedding request containing the input texts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding response with one vector per input in request order.</returns>
    Task<EmbeddingResponse> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default);
}
