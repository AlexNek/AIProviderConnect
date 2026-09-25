using System.Text.Json;

using AiCleverness.Abstractions;
using AiCleverness.Models;

using AIProviderConnect.Models;

using Microsoft.Extensions.Logging;

using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed class DataValidationAgent : IAgent
{
    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    private readonly ILogger<DataValidationAgent> _logger;

    private readonly IAgentRuntime _runtime;

    private readonly AppSettings _settings;

    private int _lastCompletionTokens;

    private int _lastPromptTokens;

    public string Name => nameof(DataValidationAgent);

    public DataValidationAgent(
        IAgentRuntime runtime,
        IToolRegistry toolRegistry,
        IEnumerable<ITool> tools,
        ILogger<DataValidationAgent> logger,
        AppSettings settings)
    {
        _runtime = runtime;
        _logger = logger;
        _settings = settings;
        foreach (var tool in tools)
            toolRegistry.Register(tool);
    }

    public async Task<DataValidationResult> ValidateAsync(
        string providerId,
        ProviderDefinition provider,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var systemPrompt = "You are a provider data validation agent. "
                           + "Analyze provider definitions for correctness, completeness, and consistency. "
                           + "Check that URLs are valid, models are defined, pricing is present, and fields are populated. "
                           + "Respond with JSON: {\"analysis\": \"summary\", \"issues\": [\"issue1\", \"issue2\", ...]}";

        var userPrompt = $"Validate provider: {provider.DisplayName} (id: {providerId})\n"
                         + $"JSON: {JsonSerializer.Serialize(provider, JsonWriteOptions)}";

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
            AgentName: nameof(DataValidationAgent));

        try
        {
            _lastPromptTokens = 0;
            _lastCompletionTokens = 0;

            var result = await _runtime.RunAsync(request, progress, ct);
            _lastPromptTokens = result.Usage?.PromptTokens ?? 0;
            _lastCompletionTokens = result.Usage?.CompletionTokens ?? 0;

            if (!result.Success)
                return new DataValidationResult(
                    false,
                    result.Reasoning,
                        [],
                    _lastPromptTokens,
                    _lastCompletionTokens);

            var parsed = JsonSerializer.Deserialize<JsonElement>(result.Output ?? "{}");
            var analysis = parsed.TryGetProperty("analysis", out var a) ? a.GetString() : null;
            var issues = parsed.TryGetProperty("issues", out var i)
                             ? JsonSerializer.Deserialize<List<string>>(i.GetRawText()) ?? []
                             : [];

            return new DataValidationResult(
                true,
                analysis,
                issues,
                _lastPromptTokens,
                _lastCompletionTokens);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data validation failed for {ProviderId}", providerId);
            return new DataValidationResult(
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

        ProviderDefinition? provider = null;
        if (!string.IsNullOrWhiteSpace(providerJson))
        {
            try
            {
                provider = JsonSerializer.Deserialize<ProviderDefinition>(providerJson);
            }
            catch (JsonException)
            {
            }
        }

        if (provider is null)
            return new AgentResult(false, null, "provider_json parameter is required");

        var result = await ValidateAsync(providerId, provider, null, cancellationToken);
        return new AgentResult(
            result.Success,
            result.Analysis,
            result.Issues is { Count: > 0 } ? string.Join("; ", result.Issues) : null);
    }
}
