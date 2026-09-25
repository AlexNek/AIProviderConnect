using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed class AiDefinitionAnalyzer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
                                                                    {
                                                                        PropertyNameCaseInsensitive =
                                                                            true
                                                                    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    private readonly AiAnalysisService _ai;

    private readonly IProviderCatalog _catalog;

    private readonly HttpClient _http;

    private readonly IAIProviderFactory _providerFactory;

    private readonly AppSettings _settings;

    public AiAnalysisService AiService => _ai;

    public bool IsAvailable => _ai.IsConfigured;

    public bool IsModelConfigured => _ai.IsModelConfigured;

    public AiDefinitionAnalyzer(
        IAIProviderFactory providerFactory,
        AiAnalysisService ai,
        AppSettings settings,
        HttpClient http,
        IProviderCatalog catalog)
    {
        _providerFactory = providerFactory;
        _ai = ai;
        _settings = settings;
        _http = http;
        _catalog = catalog;
    }

    public async Task<AiAnalysisBatch> AnalyzeProviderAsync(
        ProviderDefinition provider,
        string? scrapeError = null,
        CancellationToken ct = default)
    {
        var result = new AiAnalysisBatch
                         {
                             ProviderId = provider.Id,
                             DisplayName = provider.DisplayName,
                             AnalysisSuccess = false
                         };

        if (!IsAvailable)
        {
            result.ErrorMessage = "OpenRouter API key not configured.";
            return result;
        }

        try
        {
            var research = _catalog.GetResearchMetadata(provider.Id);
            var apiPricingHtml = await FetchPricingPageAsync(research?.ApiPricingUrl, ct);
            var subscriptionPricingHtml =
                await FetchPricingPageAsync(research?.SubscriptionPricingUrl, ct);

            var systemPrompt = """
                               You are an AI assistant that analyzes AI provider pricing pages.
                               Providers have TWO distinct pricing concepts:
                                 - apiPricingUrl: the page showing per-token / per-request API pricing (pay-as-you-go costs)
                                 - subscriptionPricingUrl: the page showing subscription plans, tiers, or monthly/annual pricing

                               Given a provider's JSON definition and the HTML of their pricing pages, you identify issues and suggest corrections.
                               If a provider is self-hosted (localhost), both pricing fields should be "-".
                               Respond ONLY with a valid JSON object (no markdown, no code fences).
                               """;

            var providerJson = BuildProviderJson(provider, research);
            var scrapeErrorText = scrapeError ?? "none";

            var apiHtmlPreview = apiPricingHtml is not null
                                     ? apiPricingHtml.Length > 12000
                                           ? apiPricingHtml[..12000] + "..."
                                           : apiPricingHtml
                                     : "Page could not be fetched";

            var subscriptionHtmlPreview = subscriptionPricingHtml is not null
                                              ? subscriptionPricingHtml.Length > 8000
                                                    ? subscriptionPricingHtml[..8000] + "..."
                                                    : subscriptionPricingHtml
                                              : "Page not set or could not be fetched";

            var userPrompt =
                "Provider definition:\n" +
                providerJson + "\n\n" +
                "Scrape error (if any): " + scrapeErrorText + "\n\n" +
                "API Pricing page HTML (apiPricingUrl):\n" +
                apiHtmlPreview + "\n\n" +
                "Subscription Pricing page HTML (subscriptionPricingUrl):\n" +
                subscriptionHtmlPreview + "\n\n" +
                "Analyze the provider definition and both pricing pages. Identify any issues with:\n"
                +
                "1. The apiPricingUrl — does it show per-token/per-request API costs? It must NOT be a subscription/plans page.\n"
                +
                "2. The subscriptionPricingUrl — does it show subscription tiers/plans? It must NOT be the same as apiPricingUrl.\n"
                +
                "3. If scraping failed, suggest the correct XPath to the pricing table, row offset, and cell indices (modelCellIndex, promptCellIndex, completionCellIndex)\n"
                +
                "4. Any other fields that seem wrong (displayName, category, etc.)\n\n" +
                "Return ONLY valid JSON with this exact structure (no markdown, no code fences):\n"
                +
                "{\"suggestions\":[{\"field\":\"apiPricingUrl\",\"currentValue\":\"...\",\"suggestedValue\":\"...\",\"reason\":\"...\",\"severity\":\"error|warning|info\"}],"
                +
                "\"apiPricingUrl\":\"corrected URL or null\",\"subscriptionPricingUrl\":\"corrected URL or null\","
                +
                "\"tableXPath\":\"suggested XPath or null\",\"rowOffset\":0,\"modelCellIndex\":0,\"promptCellIndex\":1,\"completionCellIndex\":2,\"priceUnit\":\"PerToken|Per1K|Per1M\"}\n";

            var response = await SendPromptWithUsageAsync(systemPrompt, userPrompt, ct);
            if (response is null)
            {
                result.ErrorMessage = "AI returned no response.";
                return result;
            }

            if (response.Usage is not null)
                result.Usage = response.Usage;

            var analysis =
                JsonSerializer.Deserialize<AiAnalysisResponse>(response.Content, JsonOptions);
            if (analysis is null)
            {
                result.ErrorMessage = "Failed to parse AI response.";
                return result;
            }

            result.Suggestions = analysis.Suggestions.Select(s => new AiSuggestion
                {
                    ProviderId = provider.Id,
                    DisplayName = provider.DisplayName,
                    Field = s.Field,
                    CurrentValue = s.CurrentValue,
                    SuggestedValue = s.SuggestedValue,
                    Reason = s.Reason,
                    Severity = s.Severity
                }).ToList();

            result.AnalysisSuccess = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Rebuilds the flat provider manifest JSON for the LLM prompt by delegating to
    /// <see cref="ProviderManifestSerializer"/>, preserving the single-object shape the prompt expects.
    /// </summary>
    private static string BuildProviderJson(
        ProviderDefinition provider,
        ProviderResearchMetadata? research)
        => ProviderManifestSerializer.Flatten(provider, research).ToJsonString(JsonWriteOptions);

    private async Task<string?> FetchPricingPageAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || url == ProviderJsonFields.NotApplicable)
            return null;

        try
        {
            return await _http.GetStringAsync(url, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<ChatCompletionResponse?> SendPromptWithUsageAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct)
    {
        var provider = _providerFactory.GetProvider(_settings.SelectedProviderId ?? string.Empty);
        var request = new ChatCompletionRequest
                          {
                              Model = _settings.PrimaryModel,
                              Messages =
                                  [
                                      new ChatMessage
                                          {
                                              Role = EChatRole.System, Content = systemPrompt
                                          },
                                      new ChatMessage { Role = EChatRole.User, Content = userPrompt }
                                  ],
                              Temperature = 0.1f
                          };
        return await provider.ChatAsync(request, ct);
    }
}
