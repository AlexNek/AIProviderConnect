using AIProviderConnect.Abstractions;

namespace AIProviderConnect.Exceptions;

/// <summary>
/// Error codes for AI provider operations.
/// </summary>
public static class AiErrorCodes
{
    /// <summary>
    /// A decision provider was asked to perform a chat call, which it does not support.
    /// </summary>
    public const string ChatNotSupported = "ai/chat-not-supported";

    /// <summary>
    /// The provider configuration is invalid.
    /// </summary>
    public const string ConfigurationError = "ai/configuration-error";

    /// <summary>
    /// A decision call was requested on a provider that does not support decisions.
    /// Check <see cref="IDecisionProvider.SupportsDecisions"/> or <c>is IDecisionProvider</c> first.
    /// </summary>
    public const string DecisionNotSupported = "ai/decision-not-supported";

    /// <summary>
    /// The requested endpoint was not found.
    /// </summary>
    public const string EndpointNotFound = "ai/endpoint-not-found";

    /// <summary>
    /// The provider rejected the request due to insufficient permissions.
    /// </summary>
    public const string Forbidden = "ai/forbidden";

    /// <summary>
    /// The provider returned an error response when asked to embed.
    /// Maps HTTP 400/404/422 from the embeddings endpoint.
    /// </summary>
    public const string EmbeddingFailed = "ai/embedding-failed";

    /// <summary>
    /// EmbedAsync was called with an empty Model and no DefaultEmbeddingModel is set.
    /// </summary>
    public const string EmbeddingModelNotConfigured = "ai/embedding-model-not-configured";

    /// <summary>
    /// The request was invalid — malformed syntax, unknown model, schema violation, or
    /// other client-side error (HTTP 400).
    /// </summary>
    public const string InvalidRequest = "ai/invalid-request";

    /// <summary>
    /// Model discovery is not supported by the configured provider instance.
    /// Check <see cref="IModelDiscoveryProvider.SupportsModelDiscovery"/> before calling.
    /// </summary>
    public const string ModelDiscoveryNotSupported = "ai/model-discovery-not-supported";

    /// <summary>
    /// No API key was provided for the provider.
    /// </summary>
    public const string NoApiKey = "ai/no-api-key";

    /// <summary>
    /// No base URL was provided for the provider.
    /// </summary>
    public const string NoBaseUrl = "ai/no-base-url";

    /// <summary>
    /// Could not connect to the provider.
    /// </summary>
    public const string NoConnection = "ai/no-connection";

    /// <summary>
    /// The provider server is not available.
    /// </summary>
    public const string NoServer = "ai/no-server";

    /// <summary>
    /// The provider call failed.
    /// </summary>
    public const string ProviderCallFailed = "ai/provider-call-failed";

    /// <summary>
    /// The provider is disabled.
    /// </summary>
    public const string ProviderDisabled = "ai/provider-disabled";

    /// <summary>The requested provider has not been registered.</summary>
    public const string ProviderNotFound = "ai/provider-not-found";

    /// <summary>
    /// The provider returned an error.
    /// </summary>
    public const string ProviderError = "ai/provider-error";

    /// <summary>
    /// The provider is missing a required configuration.
    /// </summary>
    public const string ProviderMissingConfiguration = "ai/provider-missing-configuration";

    /// <summary>
    /// The provider has rate-limited the request.
    /// </summary>
    public const string RateLimited = "ai/rate-limited";

    /// <summary>
    /// The request timed out.
    /// </summary>
    public const string Timeout = "ai/timeout";

    /// <summary>
    /// The provider rejected the request due to invalid credentials.
    /// </summary>
    public const string Unauthorized = "ai/unauthorized";
}
