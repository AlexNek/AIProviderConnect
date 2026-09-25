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

    private IAIProvider BuildProvider(
        ProviderDefinition definition,
        string apiKey,
        HttpClient http)
    {
        var baseUrl = definition.BaseUrl;
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

    private IAIProvider CreateProvider(string providerId, string apiKey)
    {
        var http = _httpClientFactory.CreateClient(HttpConstants.AiApiHttpClientName);
        var definition = _catalog.Get(providerId)
                         ?? throw new InvalidOperationException($"Unknown provider: {providerId}");
        var trimmedKey = apiKey?.Trim() ?? string.Empty;
        return BuildProvider(definition, trimmedKey, http);
    }
}
