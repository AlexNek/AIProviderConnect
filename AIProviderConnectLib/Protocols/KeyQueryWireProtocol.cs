using System.Text.Json;

using AIProviderConnect.Constants;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Services;

namespace AIProviderConnect.Protocols;

public static class KeyQueryWireProtocol
{
    private const string GenerationEndpointKey = "generationEndpoint";
    private const string StreamEndpointKey = "streamEndpoint";

    /// <summary>
    /// Applies protocol-specific configuration from the options' ProtocolConfiguration
    /// dictionary to the typed options. Key names are defined and used only here.
    /// Resolves the custom auth header name and endpoint patterns onto generic options
    /// properties so the provider has zero protocol-specific knowledge.
    /// </summary>
    public static void ApplyProtocolConfiguration(KeyQueryOptions options)
    {
        if (options.ProtocolConfiguration is null)
            return;

        if (ProtocolParsingHelpers.TryExtractApiKeyHeaderName(options.ProtocolConfiguration, out var headerName))
        {
            options.CustomAuthHeaderName = headerName;
        }

        if (options.ProtocolConfiguration.TryGetValue(GenerationEndpointKey, out var generationEndpoint)
            && !string.IsNullOrWhiteSpace(generationEndpoint))
        {
            options.ChatEndpoint = generationEndpoint;
        }

        if (options.ProtocolConfiguration.TryGetValue(StreamEndpointKey, out var streamEndpoint)
            && !string.IsNullOrWhiteSpace(streamEndpoint))
        {
            options.StreamEndpoint = streamEndpoint;
        }
    }

    public static object MapRequest(ChatCompletionRequest request)
    {
        var systemInstruction = MessageTextResolver.ResolveSystemInstruction(request.Messages);

        var contents = request.Messages
            .Where(x => x.Role != EChatRole.System)
            .Select(x => new
                             {
                                 role = MapRole(x),
                                 parts = MapParts(x)
                             })
            .ToList();

        var body = new Dictionary<string, object?>
        {
            [KeyQueryPropertyNames.SystemInstruction] = string.IsNullOrWhiteSpace(systemInstruction)
                                        ? null
                                        : new
                                              {
                                                  parts = new[]
                                                              {
                                                                  new
                                                                  {
                                                                      text = systemInstruction
                                                                  }
                                                              }
                                              },
            [KeyQueryPropertyNames.Contents] = contents,
            [KeyQueryPropertyNames.GenerationConfig] = MapGenerationConfig(request)
        };

        if (request.Tools is { Count: > 0 })
        {
            body[KeyQueryPropertyNames.Tools] = new[]
            {
                new
                {
                    functionDeclarations = request.Tools.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = t.Parameters
                    }).ToList()
                }
            };
        }

        return body;
    }

    private static string MapRole(ChatMessage message) =>
        message.Role switch
        {
            EChatRole.Assistant => "model",
            EChatRole.Tool => "function",
            _ => "user"
        };

    /// <summary>
    /// Maps a message to a Gemini <c>parts</c> array: a single text part for text-only messages,
    /// the <see cref="ChatMessage.ContentParts"/> (text plus inline/file image parts) when set,
    /// <c>functionCall</c> parts for assistant tool calls, or a <c>functionResponse</c> part for
    /// tool result messages.
    /// </summary>
    private static object[] MapParts(ChatMessage message)
    {
        // Tool result message: emit functionResponse part
        if (message.Role == EChatRole.Tool)
        {
            var responseObj = ParseToolArguments(message.Content);
            return
            [
                new
                {
                    functionResponse = new
                    {
                        name = message.ToolCallId ?? string.Empty,
                        response = responseObj
                    }
                }
            ];
        }

        // Assistant message with tool calls: emit functionCall parts
        if (message.Role == EChatRole.Assistant && message.ToolCalls is { Count: > 0 })
        {
            var parts = new List<object>();

            if (!string.IsNullOrWhiteSpace(message.Content))
            {
                parts.Add(new { text = message.Content });
            }

            foreach (var tc in message.ToolCalls)
            {
                parts.Add(new
                {
                    functionCall = new
                    {
                        name = tc.Name,
                        args = ParseToolArguments(tc.Arguments)
                    }
                });
            }

            return parts.ToArray();
        }

        // Regular text-only or multimodal message
        if (message.ContentParts is { Count: > 0 })
        {
            return ProtocolParsingHelpers.MapContentParts(
                message.ContentParts,
                imageMapper: p => MapImagePart(p),
                textMapper: t => (object)new { text = t });
        }

        return [new { text = message.Content ?? string.Empty }];
    }

    /// <summary>
    /// Parses a tool arguments JSON string into a deserialized object. Falls back to an empty
    /// object on parse failure.
    /// </summary>
    private static object ParseToolArguments(string? arguments)
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
    /// Maps an image part to a Gemini image part: <c>inlineData</c> (base64) when raw bytes are
    /// supplied, otherwise <c>fileData</c> referencing the URL. The <c>fileData.mimeType</c> key is
    /// emitted only when a media type is known, so a URL image never serializes
    /// <c>"mimeType": null</c>.
    /// </summary>
    private static object MapImagePart(ImageContent? image)
    {
        var source = image?.ResolveSource() ?? new ImageSource.UrlSource(string.Empty);
        if (source is ImageSource.Base64Source(var data, var mediaType))
            return new { inlineData = new { mimeType = mediaType, data = Convert.ToBase64String(data) } };

        var url = ((ImageSource.UrlSource)source).Url;
        var fileData = new Dictionary<string, object> { [KeyQueryPropertyNames.FileUri] = url };
        if (image?.MediaType is not null)
            fileData[KeyQueryPropertyNames.MimeType] = image.MediaType;
        return new { fileData };
    }

    public static ChatCompletionResponse ParseResponse(string model, JsonElement json)
    {
        var content = string.Empty;
        IReadOnlyList<ToolCall>? toolCalls = null;
        ChatContentLogprobs? logprobs = null;

        if (json.TryGetProperty(KeyQueryPropertyNames.Candidates, out var candidates)
            && candidates.GetArrayLength() > 0)
        {
            logprobs = ParseLogprobs(candidates[0]);
        }

        if (json.TryGetProperty(KeyQueryPropertyNames.Candidates, out var candidatesForContent)
            && candidatesForContent.GetArrayLength() > 0
            && candidatesForContent[0].TryGetProperty(KeyQueryPropertyNames.Content, out var contentNode)
            && contentNode.TryGetProperty(KeyQueryPropertyNames.Parts, out var parts))
        {
            var textParts = new List<string>();
            var toolCallList = new List<ToolCall>();

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty(KeyQueryPropertyNames.Text, out var textEl))
                {
                    textParts.Add(textEl.GetString() ?? string.Empty);
                }
                else if (part.TryGetProperty(KeyQueryPropertyNames.FunctionCall, out var fcEl))
                {
                    var name = fcEl.TryGetProperty(KeyQueryPropertyNames.Name, out var nameEl)
                                   ? nameEl.GetString() ?? string.Empty
                                   : string.Empty;
                    var args = fcEl.TryGetProperty(KeyQueryPropertyNames.Args, out var argsEl)
                                   ? argsEl.GetRawText()
                                   : "{}";

                    toolCallList.Add(new ToolCall
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = name,
                        Arguments = args
                    });
                }
            }

            content = string.Concat(textParts);
            if (toolCallList.Count > 0)
                toolCalls = toolCallList;
        }

        var usage = UsageInfoParser.Parse(
            json,
            KeyQueryPropertyNames.UsageMetadata,
            KeyQueryPropertyNames.PromptTokenCount,
            KeyQueryPropertyNames.CandidatesTokenCount,
            KeyQueryPropertyNames.TotalTokenCount);

        return new ChatCompletionResponse
                   {
                       Id = Guid.NewGuid().ToString("N"),
                       Model = model,
                       Content = content,
                       ToolCalls = toolCalls,
                       Usage = usage,
                       Logprobs = logprobs
                   };
    }

    /// <summary>
    /// Maps the <c>generationConfig</c> block. The logprob fields are emitted only when the request
    /// asked for log probabilities, so an ordinary chat request keeps its current body byte for byte.
    /// </summary>
    private static object MapGenerationConfig(ChatCompletionRequest request)
    {
        if (request.Logprobs != true)
        {
            return new
                       {
                           temperature = request.Temperature,
                           maxOutputTokens = request.MaxTokens
                       };
        }

        return new
                   {
                       temperature = request.Temperature,
                       maxOutputTokens = request.MaxTokens,
                       responseLogprobs = true,
                       logprobs = request.TopLogprobs
                   };
    }

    // Reads candidates[0].logprobsResult. The first decoding step is the answer position; its
    // candidates become the alternative list, and chosenCandidates[0] is the generated token.
    private static ChatContentLogprobs? ParseLogprobs(JsonElement candidate)
    {
        if (!candidate.TryGetProperty(KeyQueryPropertyNames.LogprobsResult, out var result)
            || result.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!result.TryGetProperty(KeyQueryPropertyNames.TopCandidates, out var topCandidates)
            || topCandidates.ValueKind != JsonValueKind.Array
            || topCandidates.GetArrayLength() == 0)
        {
            return null;
        }

        var tops = new List<ChatTopLogprob>();
        if (topCandidates[0].ValueKind == JsonValueKind.Object
            && topCandidates[0].TryGetProperty(KeyQueryPropertyNames.Candidates, out var stepCandidates)
            && stepCandidates.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in stepCandidates.EnumerateArray())
            {
                if (TryReadCandidate(entry, out var token, out var logprob))
                    tops.Add(new ChatTopLogprob(token, logprob));
            }
        }

        if (tops.Count == 0)
            return null;

        var chosenToken = tops[0].Token;
        var chosenLogprob = tops[0].Logprob;
        if (result.TryGetProperty(KeyQueryPropertyNames.ChosenCandidates, out var chosen)
            && chosen.ValueKind == JsonValueKind.Array
            && chosen.GetArrayLength() > 0
            && TryReadCandidate(chosen[0], out var chosenTokenValue, out var chosenLogprobValue))
        {
            chosenToken = chosenTokenValue;
            chosenLogprob = chosenLogprobValue;
        }

        return new ChatContentLogprobs
                   {
                       Content = [new ChatTokenLogprob(chosenToken, chosenLogprob, tops)]
                   };
    }

    private static bool TryReadCandidate(JsonElement entry, out string token, out double logprob)
    {
        token = string.Empty;
        logprob = 0d;

        if (entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty(KeyQueryPropertyNames.Token, out var tokenProp)
            || tokenProp.ValueKind != JsonValueKind.String
            || !entry.TryGetProperty(KeyQueryPropertyNames.LogProbability, out var logprobProp)
            || logprobProp.ValueKind != JsonValueKind.Number
            || !logprobProp.TryGetDouble(out logprob))
        {
            return false;
        }

        token = tokenProp.GetString() ?? string.Empty;
        return true;
    }

    public static StreamingChatChunk? ParseStreamChunk(JsonElement json)
    {
        if (json.TryGetProperty(KeyQueryPropertyNames.Candidates, out var candidates)
            && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            if (candidate.TryGetProperty(KeyQueryPropertyNames.Content, out var content) &&
                content.TryGetProperty(KeyQueryPropertyNames.Parts, out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                var text = string.Concat(
                    parts.EnumerateArray()
                        .Where(x => x.TryGetProperty(KeyQueryPropertyNames.Text, out _))
                        .Select(x => x.GetProperty(KeyQueryPropertyNames.Text).GetString() ?? string.Empty));

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return new StreamingChatChunk { Content = text };
                }
            }

            if (candidate.TryGetProperty(KeyQueryPropertyNames.FinishReason, out var finishReason) &&
                finishReason.ValueKind == JsonValueKind.String)
            {
                return new StreamingChatChunk { IsCompleted = true };
            }
        }

        return null;
    }

    public static IReadOnlyList<AIModel> ParseModels(
        JsonElement json,
        string providerId = "",
        IReadOnlyDictionary<string, string>? protocolConfiguration = null) =>
        ProtocolParsingHelpers.ParseModelArray(
            json, KeyQueryPropertyNames.Models, providerId,
            x => new AIModel
            {
                Id = ProtocolParsingHelpers.SafeGetString(x, KeyQueryPropertyNames.Name)
                         .Replace("models/", string.Empty, StringComparison.Ordinal),
                ProviderId = providerId,
                Capabilities = ProtocolParsingHelpers.ParseCapabilities(x, protocolConfiguration),
                DisplayName = ProtocolParsingHelpers.SafeGetString(x, KeyQueryPropertyNames.DisplayName)
            });
}
