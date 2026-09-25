namespace AIProviderConnect.Options;

/// <summary>
/// Internal contract for options classes that carry both a chat endpoint and a models endpoint.
/// Allows <c>SeedFromDefinition</c> to set both properties without duplicating switch arms.
/// </summary>
internal interface IChatAndModelsEndpointOptions
{
    string ChatEndpoint { get; set; }

    string ModelsEndpoint { get; set; }
}
