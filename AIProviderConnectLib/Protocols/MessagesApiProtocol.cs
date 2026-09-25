using System.Text.Json;

using AIProviderConnect.Constants;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Services;

namespace AIProviderConnect.Protocols;

/// <summary>
/// Provides Messages API wire protocol mapping.
/// </summary>
public static class MessagesApiProtocol
{
    private const string AnthropicVersionKey = "anthropicVersion";
    private const string MessagesApiVersionHeader = "anthropic-version";

    /// <summary>
    /// Applies protocol-specific configuration from the options' ProtocolConfiguration
    /// dictionary to the typed options. Key names are defined and used only here.
    /// Stores the API version header in DefaultHeaders and resolves the custom auth
    /// header name onto a generic options property so the provider has zero
    /// protocol-specific knowledge.
    /// </summary>
    public static void ApplyProtocolConfiguration(MessagesApiOptions options)
    {
        if (options.ProtocolConfiguration is null)
            return;

        if (options.ProtocolConfiguration.TryGetValue(AnthropicVersionKey, out var version)
            && !string.IsNullOrWhiteSpace(version))
        {
            options.DefaultHeaders[MessagesApiVersionHeader] = version;
        }

        if (ProtocolParsingHelpers.TryExtractApiKeyHeaderName(options.ProtocolConfiguration, out var headerName))
        {
            options.CustomAuthHeaderName = headerName;
        }
    }

    /// <summary>
    /// Maps a <see cref="ChatCompletionRequest"/> to a Messages API request payload.
    /// </summary>
    public static object MapRequest(ChatCompletionRequest request) =>
        MapPayload(request);

    public static Dictionary<string, object?> MapStreamRequest(ChatCompletionRequest request)
    {
        var payload = MapPayload(request);
        payload[MessagesApiPropertyNames.Stream] = true;
        if (payload[MessagesApiPropertyNames.System] is null)
            payload.Remove(MessagesApiPropertyNames.System);
        return payload;
    }

    private static Dictionary<string, object?> MapPayload(ChatCompletionRequest request)
    {
        var systemText = MessageTextResolver.ResolveSystemInstruction(request.Messages);

        var messages = request.Messages
            .Where(x => x.Role != EChatRole.System)
            .Select(x => new
                             {
                                 role = MapRole(x),
                                 content = MapContent(x)
                             })
            .ToList();

        var payload = new Dictionary<string, object?>
        {
            [MessagesApiPropertyNames.Model] = request.Model,
            [MessagesApiPropertyNames.MaxTokens] = request.MaxTokens,
            [MessagesApiPropertyNames.Temperature] = request.Temperature,
            [MessagesApiPropertyNames.System] = string.IsNullOrWhiteSpace(systemText) ? null : systemText,
            [MessagesApiPropertyNames.Messages] = messages
        };

        if (request.Tools is { Count: > 0 })
        {
            payload[MessagesApiPropertyNames.Tools] = request.Tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                input_schema = t.Parameters
            }).ToList();
        }

        return payload;
    }

    private static string MapRole(ChatMessage message) =>
        message.Role switch
        {
            EChatRole.Assistant => "assistant",
            EChatRole.Tool => "user",
            _ => "user"
        };

    /// <summary>
    /// Maps a message's content to the Messages API content shape: a plain string for
    /// text-only messages, an array of content blocks (text/image/tool_use) when
    /// <see cref="ChatMessage.ContentParts"/> or <see cref="ChatMessage.ToolCalls"/> is set,
    /// or a tool_result block for tool result messages.
    /// </summary>
    private static object MapContent(ChatMessage message)
    {
        // Tool result message: emit tool_result block(s)
        if (message.Role == EChatRole.Tool)
        {
            return new[]
            {
                new
                {
                    type = MessagesApiPropertyNames.ToolResult,
                    tool_use_id = message.ToolCallId ?? string.Empty,
                    content = message.Content ?? string.Empty
                }
            };
        }

        // Assistant message with tool calls: emit text blocks + tool_use blocks
        if (message.Role == EChatRole.Assistant && message.ToolCalls is { Count: > 0 })
        {
            var blocks = new List<object>();

            if (!string.IsNullOrWhiteSpace(message.Content))
            {
                blocks.Add(new { type = MessagesApiPropertyNames.Text, text = message.Content });
            }

            foreach (var tc in message.ToolCalls)
            {
                blocks.Add(new
                {
                    type = MessagesApiPropertyNames.ToolUse,
                    id = tc.Id,
                    name = tc.Name,
                    input = ParseToolInput(tc.Arguments)
                });
            }

            return blocks;
        }

        // Regular text-only or multimodal message
        if (message.ContentParts is not { Count: > 0 })
            return message.Content ?? string.Empty;

        return ProtocolParsingHelpers.MapContentParts(
            message.ContentParts,
            imageMapper: p => MapImage(p),
            textMapper: t => (object)new { type = MessagesApiPropertyNames.Text, text = t });
    }

    /// <summary>
    /// Parses a tool call arguments JSON string into a deserialized object for embedding
    /// in the Anthropic <c>input</c> field. Falls back to an empty object on parse failure.
    /// </summary>
    private static object ParseToolInput(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return new Dictionary<string, object?>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments)
                   ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    /// <summary>
    /// Maps an image part to a Messages API image block, using a base64 source when raw bytes
    /// are supplied and a url source otherwise.
    /// </summary>
    private static object MapImage(ImageContent? image)
    {
        var source = image?.ResolveSource() ?? new ImageSource.UrlSource(string.Empty);
        return source switch
        {
            ImageSource.Base64Source(var data, var mediaType) => new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = mediaType,
                    data = Convert.ToBase64String(data)
                }
            },
            ImageSource.UrlSource(var url) => new
            {
                type = "image",
                source = new { type = "url", url }
            },
            _ => throw new InvalidOperationException($"Unexpected image source type: {source.GetType().Name}")
        };
    }

    /// <summary>
    /// Parses a list of models from a Messages API response.
    /// </summary>
    public static IReadOnlyList<AIModel> ParseModels(JsonElement json, string providerId = "") =>
        ProtocolParsingHelpers.ParseModelArray(
            json, MessagesApiPropertyNames.Data, providerId,
            x => new AIModel
            {
                Id = ProtocolParsingHelpers.SafeGetString(x, MessagesApiPropertyNames.Id),
                // The fallback keys on property presence, not on emptiness, so a present-but-empty
                // display_name stays empty — matching OpenAICompatibleWireProtocol.ResolveDisplayName.
                DisplayName = x.TryGetProperty(MessagesApiPropertyNames.DisplayName, out _)
                                  ? ProtocolParsingHelpers.SafeGetString(x, MessagesApiPropertyNames.DisplayName)
                                  : ProtocolParsingHelpers.SafeGetString(x, MessagesApiPropertyNames.Id),
                ProviderId = providerId
            });

    /// <summary>
    /// Parses a Messages API response into a <see cref="ChatCompletionResponse"/>.
    /// </summary>
    public static ChatCompletionResponse ParseResponse(JsonElement json)
    {
        var content = string.Empty;
        IReadOnlyList<ToolCall>? toolCalls = null;

        if (json.TryGetProperty(MessagesApiPropertyNames.Content, out var contentArray)
            && contentArray.ValueKind == JsonValueKind.Array)
        {
            var textParts = new List<string>();
            var toolCallList = new List<ToolCall>();

            foreach (var block in contentArray.EnumerateArray())
            {
                if (!block.TryGetProperty(MessagesApiPropertyNames.Type, out var typeElement))
                    continue;

                var blockType = typeElement.GetString();

                if (blockType == MessagesApiPropertyNames.Text)
                {
                    if (block.TryGetProperty(MessagesApiPropertyNames.Text, out var text))
                        textParts.Add(text.GetString() ?? string.Empty);
                }
                else if (blockType == MessagesApiPropertyNames.ToolUse)
                {
                    var toolId = block.TryGetProperty(MessagesApiPropertyNames.Id, out var idEl)
                                 ? idEl.GetString() ?? string.Empty
                                 : string.Empty;
                    var name = block.TryGetProperty(MessagesApiPropertyNames.Name, out var nameEl)
                                   ? nameEl.GetString() ?? string.Empty
                                   : string.Empty;
                    var input = block.TryGetProperty(MessagesApiPropertyNames.Input, out var inputEl)
                                    ? inputEl.GetRawText()
                                    : "{}";

                    toolCallList.Add(new ToolCall
                    {
                        Id = toolId,
                        Name = name,
                        Arguments = input
                    });
                }
            }

            content = string.Concat(textParts);
            if (toolCallList.Count > 0)
                toolCalls = toolCallList;
        }

        var usage = UsageInfoParser.Parse(
            json,
            MessagesApiPropertyNames.Usage,
            MessagesApiPropertyNames.InputTokens,
            MessagesApiPropertyNames.OutputTokens,
            totalKey: null);

        string? stopReason = null;
        if (json.TryGetProperty(MessagesApiPropertyNames.StopReason, out var sr) && sr.ValueKind == JsonValueKind.String)
        {
            stopReason = sr.GetString();
        }

        return new ChatCompletionResponse
                   {
                       Id = json.TryGetProperty(MessagesApiPropertyNames.Id, out var id)
                                ? id.GetString() ?? string.Empty
                                : string.Empty,
                       Model = json.TryGetProperty(MessagesApiPropertyNames.Model, out var model)
                                   ? model.GetString() ?? string.Empty
                                   : string.Empty,
                       Content = content,
                       FinishReason = stopReason,
                       ToolCalls = toolCalls,
                       Usage = usage
                   };
    }

}
