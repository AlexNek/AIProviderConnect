using System.Runtime.CompilerServices;
using System.Text.Json;

using AiCleverness.Abstractions;
using AiCleverness.Models;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using ScraperTool.Models;

using ChatMessage = AIProviderConnect.Models.ChatMessage;
using EChatRole = AIProviderConnect.Models.EChatRole;
using ToolDefinition = AIProviderConnect.Models.ToolDefinition;

namespace ScraperTool.Services.UrlResearch.Adapters;

/// <summary>
/// Adapts the application's AI provider (via <see cref="IAIProviderFactory"/>) to the provider-neutral
/// <see cref="ILlmClient"/> abstraction used by AiCleverness.
/// When the underlying provider also implements <see cref="IStreamingChatProvider"/>,
/// this client exposes token-level streaming via <see cref="IStreamingLlmClient"/>.
/// </summary>
public sealed class AIProviderLlmClient : IStreamingLlmClient
{
    /// <summary>Default sampling temperature applied when the caller does not specify one.</summary>
    private const float DefaultTemperature = 0.1f;

    private readonly IAIProviderFactory _providerFactory;

    private readonly AppSettings _settings;

    public AIProviderLlmClient(IAIProviderFactory providerFactory, AppSettings settings)
    {
        _providerFactory =
            providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    private IAIProvider ResolveProvider() =>
        _providerFactory.GetProvider(_settings.SelectedProviderId ?? string.Empty);

    private ChatCompletionRequest BuildRequest(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<AiCleverness.Models.ToolDefinition>? tools,
        LlmCompletionOptions? options) =>
        new()
        {
            Model = options?.Model ?? _settings.PrimaryModel,
            Messages = messages.Select(MapMessage).ToList(),
            Temperature = options?.Temperature ?? DefaultTemperature,
            Tools = tools?.Select(MapToolDefinition).ToList()
        };

    public async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<AiCleverness.Models.ToolDefinition>? tools = null,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, tools, options);
        var provider = ResolveProvider();

        var response = await provider.ChatAsync(request, cancellationToken);

        if (response is null)
            return new LlmResponse(null, null, "no_response");

        var toolCalls = response.ToolCalls
            ?.Select(tc => new LlmToolCall(tc.Id, tc.Name, tc.Arguments)).ToList();
        var usage = response.Usage is not null
                        ? new LlmTokenUsage(
                            response.Usage.PromptTokens,
                            response.Usage.CompletionTokens)
                        : null;
        return new LlmResponse(
            response.Content,
            toolCalls,
            response.FinishReason,
            usage,
            response.ReasoningContent);
    }

    private static ChatMessage MapMessage(LlmMessage message)
    {
        var toolCalls = message.ToolCalls is { Count: > 0 }
            ? message.ToolCalls
                .Select(tc => new ToolCall { Id = tc.Id, Name = tc.Name, Arguments = tc.Arguments })
                .ToList()
            : null;

        return new ChatMessage
                   {
                       Role = MapRole(message.Role),
                       Content = message.Content,
                       ToolCallId = message.ToolCallId,
                       ToolCalls = toolCalls
                   };
    }

    private static EChatRole MapRole(string role)
    {
        return role.ToLowerInvariant() switch
            {
                "system" => EChatRole.System,
                "assistant" => EChatRole.Assistant,
                "tool" => EChatRole.Tool,
                _ => EChatRole.User
            };
    }

    private static ToolDefinition MapToolDefinition(AiCleverness.Models.ToolDefinition tool)
    {
        JsonElement parameters;
        if (string.IsNullOrWhiteSpace(tool.ParametersSchema))
        {
            parameters = JsonDocument.Parse("{}").RootElement.Clone();
        }
        else
        {
            using var doc = JsonDocument.Parse(tool.ParametersSchema);
            parameters = doc.RootElement.Clone();
        }

        return new ToolDefinition
                   {
                       Name = tool.Name, Description = tool.Description, Parameters = parameters
                   };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<LlmChunk> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<AiCleverness.Models.ToolDefinition>? tools = null,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider();

        if (provider is not IStreamingChatProvider streamingProvider)
            throw new NotSupportedException(
                $"Provider '{_settings.SelectedProviderId}' does not implement IStreamingChatProvider.");

        var request = BuildRequest(messages, tools, options);

        await foreach (var chunk in streamingProvider
                           .StreamAsync(request, cancellationToken)
                           .ConfigureAwait(false))
        {
            IReadOnlyList<LlmToolCallDelta>? toolCallDeltas = null;
            if (chunk.ToolCalls is { Count: > 0 })
            {
                toolCallDeltas = chunk.ToolCalls
                    .Select(d => new LlmToolCallDelta(
                        d.Index, d.Id, d.Name, d.ArgumentsFragment))
                    .ToList();
            }

            yield return new LlmChunk(
                string.IsNullOrEmpty(chunk.Content) ? null : chunk.Content,
                ToolCalls: toolCallDeltas,
                chunk.IsCompleted);
        }
    }
}
