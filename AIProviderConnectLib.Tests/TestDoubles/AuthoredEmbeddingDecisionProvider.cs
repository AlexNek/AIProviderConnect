using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;

namespace AIProviderConnect.Tests.TestDoubles;

/// <summary>
/// A consumer-authored transport for a host that answers decisions and embeddings but has no chat
/// surface: it derives <see cref="AIProviderBase"/> (decision-capable, non-streaming) and adds the
/// optional <see cref="IEmbeddingProvider"/> capability. <see cref="EmbedAsync"/> returns a canned
/// response so decorator-forwarding tests stay hermetic.
/// </summary>
public sealed class AuthoredEmbeddingDecisionProvider : AIProviderBase, IEmbeddingProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthoredEmbeddingDecisionProvider"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="catalog">The provider catalog containing the provider's definition.</param>
    /// <param name="options">The options instance, seeded by the consumer.</param>
    /// <param name="providerId">The provider id.</param>
    public AuthoredEmbeddingDecisionProvider(
        HttpClient httpClient,
        IProviderCatalog catalog,
        AIProviderOptions options,
        string providerId)
        : base(httpClient, catalog, options, providerId)
    {
    }

    /// <inheritdoc />
    public Task<EmbeddingResponse> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new EmbeddingResponse
        {
            Data =
            [
                new EmbeddingData { Index = 0, Embedding = [0.5f] }
            ],
            Model = request.Model
        });

    /// <inheritdoc />
    public override Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new AiException(
            AiErrorCodes.ChatNotSupported,
            $"Provider '{Id}' is a decisions-and-embeddings authored transport.");
}
