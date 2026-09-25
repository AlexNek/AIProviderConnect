using System.Text.Json;

using AiCleverness.Abstractions;
using AiCleverness.Models;

using AIProviderConnect.Models;

using Microsoft.Extensions.Logging;

using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed class PricingAgent : IAgent
{
    private readonly ILogger<PricingAgent> _logger;

    private readonly IAgentRuntime _runtime;

    private readonly AppSettings _settings;

    private int _lastCompletionTokens;

    private int _lastPromptTokens;

    public string Name => nameof(PricingAgent);

    public PricingAgent(
        IAgentRuntime runtime,
        IToolRegistry toolRegistry,
        IEnumerable<ITool> tools,
        ILogger<PricingAgent> logger,
        AppSettings settings)
    {
        _runtime = runtime;
        _logger = logger;
        _settings = settings;
        foreach (var tool in tools)
            toolRegistry.Register(tool);
    }

    public async Task<PricingResult> AnalyzeAsync(
        string providerId,
        ProviderDefinition provider,
        ProviderResearchMetadata? research,
        string pricingHtml,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var systemPrompt = "You are a pricing analysis agent. "
                           + "Extract model pricing from provider HTML pages. "
                           + "Identify input/output token costs, free tiers, and special pricing. "
                           + "Respond with JSON: {\"models\": [{\"name\": \"...\", \"inputPrice\": ..., \"outputPrice\": ..., \"currency\": \"USD\"}], \"warnings\": [...]}";

        var userPrompt =
            $"Extract pricing for provider: {provider.DisplayName} (id: {providerId})\n"
            + $"Provider context: {JsonSerializer.Serialize(new { provider.BaseUrl, research?.Website })}\n"
            + $"Pricing HTML:\n{pricingHtml}";

        var parameters = new Dictionary<string, object>
                             {
                                 [AgentPropertyKeys.SystemPrompt] = systemPrompt,
                                 [AgentPropertyKeys.MaxTurns] = 5,
                                 [AgentPropertyKeys.Temperature] = 0.1f
                             };
        _settings.ApplyTranscriptParameters(parameters);

        var request = new AgentRequest(
            userPrompt,
            AllowedToolNames: null,
            parameters,
            AgentName: nameof(PricingAgent));

        try
        {
            _lastPromptTokens = 0;
            _lastCompletionTokens = 0;

            var result = await _runtime.RunAsync(request, progress, ct);
            _lastPromptTokens = result.Usage?.PromptTokens ?? 0;
            _lastCompletionTokens = result.Usage?.CompletionTokens ?? 0;

            if (!result.Success)
                return new PricingResult(
                    false,
                    result.Reasoning,
                        [],
                    _lastPromptTokens,
                    _lastCompletionTokens);

            var warnings = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<JsonElement>(result.Output);
                    if (parsed.TryGetProperty("warnings", out var w))
                        warnings = JsonSerializer.Deserialize<List<string>>(w.GetRawText()) ?? [];
                }
                catch (JsonException)
                {
                    warnings.Add("Response was not valid JSON");
                }
            }

            return new PricingResult(
                true,
                result.Output,
                warnings,
                _lastPromptTokens,
                _lastCompletionTokens);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pricing analysis failed for {ProviderId}", providerId);
            return new PricingResult(
                false,
                ex.Message,
                    [],
                _lastPromptTokens,
                _lastCompletionTokens);
        }
    }

    CapabilityRequirements? IAgent.DetermineCapabilities(AgentRequest request) => null;

    async Task<AgentResult> IAgent.ExecuteAsync(
        AgentRequest request,
        IAgentContext context,
        CancellationToken cancellationToken)
    {
        var providerId = context.GetProperty<string>("provider_id") ?? "";
        var providerJson = context.GetProperty<string>("provider_json") ?? "";
        var pricingHtml = context.GetProperty<string>("pricing_html") ?? "";

        ProviderDefinition? provider = null;
        ProviderResearchMetadata? research = null;
        if (!string.IsNullOrWhiteSpace(providerJson))
        {
            try
            {
                provider = JsonSerializer.Deserialize<ProviderDefinition>(providerJson);
                research = JsonSerializer.Deserialize<ProviderResearchMetadata>(providerJson);
            }
            catch (JsonException)
            {
            }
        }

        if (provider is null)
            return new AgentResult(false, null, "provider_json parameter is required");

        var result = await AnalyzeAsync(providerId, provider, research, pricingHtml, null, cancellationToken);
        return new AgentResult(
            result.Success,
            result.ExtractedData,
            result.Warnings is { Count: > 0 } ? string.Join("; ", result.Warnings) : null);
    }
}
