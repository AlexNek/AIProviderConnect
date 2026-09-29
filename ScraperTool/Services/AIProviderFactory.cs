using System.Net.Http;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;

using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed class AIProviderFactory : ITransientCredentialProviderFactory
{
    private readonly IProviderCatalog _catalog;

    private readonly IHttpClientFactory _httpClientFactory;

    private readonly AppSettings _settings;

    public AIProviderFactory(
        AppSettings settings,
        IHttpClientFactory httpClientFactory,
        IProviderCatalog catalog)
    {
        _settings = settings;
        _httpClientFactory = httpClientFactory;
        _catalog = catalog;
    }

    public IAIProvider GetProvider(string providerId) =>
        CreateProvider(providerId, _settings.ApiKey ?? string.Empty);

    /// <summary>
    /// Creates a provider for an explicit (possibly unsaved) API key, used to test a
    /// connection before the key is committed to settings. Does not mutate shared state.
    /// </summary>
    public IAIProvider GetProvider(string providerId, string apiKey) =>
        CreateProvider(providerId, apiKey);

    /// <summary>
    /// Creates a provider bound to per-call <see cref="RequestCredentials"/> overrides. The effective API
    /// key is <see cref="RequestCredentials.ApiKey"/> when non-empty, else the saved setting; the effective
    /// base URL is <see cref="RequestCredentials.BaseUrl"/> when non-empty, else the catalog definition's
    /// base URL. <see cref="RequestCredentials.Model"/> is attached as the provider's fixed credentials
    /// so it takes priority over <see cref="ChatCompletionRequest.Model"/> on every request.
    /// </summary>
    public IAIProvider GetProvider(string providerId, RequestCredentials overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var apiKey = !string.IsNullOrWhiteSpace(overrides.ApiKey) ? overrides.ApiKey : _settings.ApiKey;
        var baseUrlOverride = !string.IsNullOrWhiteSpace(overrides.BaseUrl) ? overrides.BaseUrl : null;
        var provider = CreateProvider(providerId, apiKey ?? string.Empty, baseUrlOverride);
        if (provider is AIProviderBase concrete)
        {
            concrete.FixedCredentials = overrides;
        }

        return provider;
    }

    private IAIProvider BuildProvider(
        ProviderDefinition definition,
        string apiKey,
        HttpClient http,
        string? baseUrlOverride = null)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(baseUrlOverride) ? baseUrlOverride : definition.BaseUrl;
        var commonHeaders = new Dictionary<string, string>
        {
            ["HTTP-Referer"] = "https://github.com/opencode-ai",
            ["X-Title"] = "AI Provider Catalog Researcher"
        };

        return definition.Protocol switch
            {
                EProviderProtocol.OpenAICompatible => new OpenAICompatibleProvider(
                    http,
                    new OpenAICompatibleProviderOptions
                        {
                            ApiKey = apiKey,
                            BaseUrl = baseUrl,
                            Enabled = true,
                            DefaultHeaders = commonHeaders
                        },
                    _catalog, definition.Id),
                EProviderProtocol.MessagesApi => new MessagesApiProvider(
                    http,
                    new MessagesApiOptions
                        {
                            ApiKey = apiKey,
                            BaseUrl = baseUrl,
                            Enabled = true,
                            DefaultHeaders = commonHeaders
                        },
                    _catalog, definition.Id),
                EProviderProtocol.HybridGateway => new OpenAICompatibleProvider(
                    http,
                    new HybridGatewayProviderOptions
                        {
                            ApiKey = apiKey,
                            BaseUrl = baseUrl,
                            Enabled = true,
                            DefaultHeaders = commonHeaders
                        },
                    _catalog, definition.Id),
                EProviderProtocol.KeyQuery => new KeyQueryProvider(
                    http,
                    new KeyQueryOptions
                        {
                            ApiKey = apiKey,
                            BaseUrl = baseUrl,
                            Enabled = true,
                            DefaultHeaders = commonHeaders
                        },
                    _catalog, definition.Id),
                EProviderProtocol.Catalog => new ModelCatalogProvider(
                    http,
                    new OpenAICompatibleProviderOptions
                        {
                            ApiKey = apiKey,
                            BaseUrl = baseUrl,
                            Enabled = true,
                            DefaultHeaders = commonHeaders
                        },
                    _catalog, definition.Id),
                _ => new OpenAICompatibleProvider(
                    http,
                    new OpenAICompatibleProviderOptions
                        {
                            ApiKey = apiKey,
                            BaseUrl = baseUrl,
                            Enabled = true,
                            DefaultHeaders = commonHeaders
                        },
                    _catalog, definition.Id)
            };
    }

    private IAIProvider CreateProvider(string providerId, string apiKey, string? baseUrlOverride = null)
    {
        var http = _httpClientFactory.CreateClient(HttpConstants.AiApiHttpClientName);
        var definition = _catalog.Get(providerId)
                         ?? throw new InvalidOperationException($"Unknown provider: {providerId}");
        var trimmedKey = apiKey?.Trim() ?? string.Empty;
        return BuildProvider(definition, trimmedKey, http, baseUrlOverride);
    }
}
