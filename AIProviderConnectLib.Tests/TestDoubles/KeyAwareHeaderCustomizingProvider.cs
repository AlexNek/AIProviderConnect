using AIProviderConnect.Abstractions;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;

using Microsoft.Extensions.Logging;

namespace AIProviderConnect.Tests.TestDoubles;

/// <summary>
/// A concrete OpenAI-compatible provider used by tests to prove a consumer override of the
/// <see cref="ConfigureHeaders(HttpRequestMessage, string)"/> overload applies bespoke headers
/// together with the effective per-call API key.
/// </summary>
public class KeyAwareHeaderCustomizingProvider : OpenAICompatibleProviderBase
{
    public KeyAwareHeaderCustomizingProvider(
        HttpClient httpClient,
        AIProviderOptions options,
        IProviderCatalog catalog,
        string providerId,
        ILogger logger,
        ICredentialResolver? credentialResolver)
        : base(httpClient, catalog, options, providerId, logger, credentialResolver)
    {
    }

    protected override void ConfigureHeaders(HttpRequestMessage request, string apiKey)
    {
        base.ConfigureHeaders(request, apiKey);
        request.Headers.TryAddWithoutValidation("X-Custom", "custom-value");
    }
}
