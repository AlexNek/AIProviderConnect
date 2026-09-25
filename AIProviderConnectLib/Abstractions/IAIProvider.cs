using AIProviderConnect.Models;

namespace AIProviderConnect.Abstractions;

/// <summary>
/// Core runtime interface for an AI provider.
/// Executes requests against a provider. Does not expose descriptive metadata
/// (URLs, display names, pricing) — obtain those from <see cref="Services.ProviderCatalog"/>.
/// </summary>
public interface IAIProvider
{
    /// <summary>
    /// Gets the unique identifier of the provider (e.g. "openai", "anthropic").
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets whether this provider is currently enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets the wire protocol used by this provider.
    /// </summary>
    EProviderProtocol Protocol { get; }

    /// <summary>
    /// Sends a chat completion request and receives a response.
    /// </summary>
    /// <param name="request">The chat completion request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chat completion response.</returns>
    Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}
