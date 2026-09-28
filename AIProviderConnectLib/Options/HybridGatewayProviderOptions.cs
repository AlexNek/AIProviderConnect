using AIProviderConnect.Constants;

namespace AIProviderConnect.Options;

/// <summary>
/// Configuration options for hybrid gateway providers.
/// </summary>
public sealed class HybridGatewayProviderOptions : AIProviderOptions, IChatAndModelsEndpointOptions, IEmbeddingsEndpointOptions
{
    /// <summary>
    /// Gets or sets the endpoint path for chat completions.
    /// </summary>
    public string ChatEndpoint { get; set; } = EndpointDefaults.ChatCompletions;

    /// <summary>
    /// Gets or sets the endpoint path for listing models.
    /// </summary>
    public string ModelsEndpoint { get; set; } = EndpointDefaults.Models;

    /// <summary>
    /// Gets or sets the endpoint path for embeddings. Default is "embeddings".
    /// </summary>
    public string EmbeddingsEndpoint { get; set; } = EndpointDefaults.Embeddings;

    /// <summary>
    /// Gets or sets the default embedding model identifier.
    /// Used when <see cref="Models.EmbeddingRequest.Model"/> is empty.
    /// </summary>
    public string DefaultEmbeddingModel { get; set; } = string.Empty;
}
