using AIProviderConnect.Abstractions;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;

using Microsoft.Extensions.Logging;

namespace AIProviderConnect.Tests.TestDoubles;

/// <summary>
/// A concrete OpenAI-compatible provider used by tests to prove a consumer override of the
/// <see cref="ConfigureHeaders(HttpRequestMessage)"/> extension point still runs on the no-override path.
/// </summary>
public class HeaderCustomizingProvider : OpenAICompatibleProviderBase
{
    public HeaderCustomizingProvider(
        HttpClient httpClient,
        AIProviderOptions options,
        IProviderCatalog catalog,
        string providerId,
        ILogger logger,
        ICredentialResolver? credentialResolver)
        : base(httpClient, catalog, options, providerId, logger, credentialResolver)
    {
    }

    protected override void ConfigureHeaders(HttpRequestMessage request)
    {
        base.ConfigureHeaders(request);
        request.Headers.TryAddWithoutValidation("X-Custom", "custom-value");
    }
}
