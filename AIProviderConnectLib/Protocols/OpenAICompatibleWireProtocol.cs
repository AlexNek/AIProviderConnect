using System.Linq;
using System.Text.Json;

using AIProviderConnect.Constants;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;

namespace AIProviderConnect.Protocols;

/// <summary>
/// Provides OpenAI-compatible wire protocol mapping.
/// </summary>
public static class OpenAICompatibleWireProtocol
{
    private const decimal PricePerMillionMultiplier = 1_000_000m;

    private static readonly string[] DisplayNameKeys =
        [OpenAICompatiblePropertyNames.Name, OpenAICompatiblePropertyNames.DisplayName, OpenAICompatiblePropertyNames.Id];

    private static readonly string[] ContextWindowKeys =
        [OpenAICompatiblePropertyNames.ContextLength, OpenAICompatiblePropertyNames.MaxInputTokens];

    /// <summary>
    /// Maps a <see cref="ChatCompletionRequest"/> to an OpenAI-compatible request payload.
    /// </summary>
    public static object MapRequest(ChatCompletionRequest request, bool stream)
    {
        object? responseFormat = null;
        if (request.ResponseFormat?.Type == OpenAICompatiblePropertyNames.JsonSchema
            && request.ResponseFormat.JsonSchema is not null)
        {
            responseFormat = new
                                 {
                                     type = OpenAICompatiblePropertyNames.JsonSchema,
                                     json_schema = new
                                                       {
                                                           name = request.ResponseFormat.JsonSchema
                                                               .Name,
                                                           strict = request.ResponseFormat
                                                               .JsonSchema.Strict,
                                                           schema = request.ResponseFormat
                                                               .JsonSchema.Schema
                                                       }
                                 };
        }

        object? tools = null;
        if (request.Tools is { Count: > 0 })
        {
            tools = request.Tools.Select(t => new
                                                  {
                                                      type = OpenAICompatiblePropertyNames.Function,
                                                      function = new
                                                                     {
                                                                         name = t.Name,
                                                                         description =
                                                                             t.Description,
                                                                         parameters = t.Parameters
                                                                     }
                                                  }).ToList();
        }

        var assistantRole = EChatRole.Assistant.ToString().ToLowerInvariant();
        var toolRole = EChatRole.Tool.ToString().ToLowerInvariant();
        var messages = request.Messages.Select<ChatMessage, object>(m =>
            {
                var role = m.Role.ToString().ToLowerInvariant();
                if (role == assistantRole && m.ToolCalls is { Count: > 0 })
                {
                    return new
                               {
                                   role,
                                   content = (string?)null,
                                   tool_calls = m.ToolCalls.Select(tc => new
                                       {
                                           id = tc.Id,
                                           type = OpenAICompatiblePropertyNames.Function,
                                           function = new
                                                          {
                                                              name = tc.Name,
                                                              arguments = tc.Arguments
                                                          }
                                       })
                               };
                }

                if (role == toolRole)
                {
                    return new { role, content = m.Content ?? "", tool_call_id = m.ToolCallId };
                }

                object contentValue = m.ContentParts is { Count: > 0 }
                    ? (object)ProtocolParsingHelpers.MapContentParts(
                        m.ContentParts,
                        imageMapper: p => MapImageUrlPart(p),
                        textMapper: t => (object)new { type = "text", text = t })
                    : (object)(m.Content ?? "");

                return new { role, content = contentValue };
            }).ToList();

        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = stream,
            ["response_format"] = responseFormat,
            ["tools"] = tools
        };

        if (request.Stop is { Count: > 0 })
            body["stop"] = request.Stop;

        if (request.TopP.HasValue)
            body["top_p"] = request.TopP;

        if (request.FrequencyPenalty.HasValue)
            body["frequency_penalty"] = request.FrequencyPenalty;

        if (request.PresencePenalty.HasValue)
            body["presence_penalty"] = request.PresencePenalty;

        return body;
    }

    /// <summary>
    /// Builds an OpenAI-compatible <c>image_url</c> content part. The optional <c>detail</c> key is
    /// emitted only when <see cref="ImageContent.Detail"/> is set, so an unset detail never
    /// serializes as <c>"detail": null</c>.
    /// </summary>
    private static object MapImageUrlPart(ImageContent? image)
    {
        var imageUrl = new Dictionary<string, object>
        {
            [OpenAICompatiblePropertyNames.Url] = image?.ResolveUrl() ?? ""
        };

        if (image?.Detail is not null)
        {
            imageUrl[OpenAICompatiblePropertyNames.Detail] = image.Detail;
        }

        return new { type = "image_url", image_url = imageUrl };
    }

    /// <summary>
    /// Parses a list of models from an OpenAI-compatible response.
    /// </summary>
    public static IReadOnlyList<AIModel> ParseModels(JsonElement json, string providerId = "") =>
        ProtocolParsingHelpers.ParseModelArray(
            json, OpenAICompatiblePropertyNames.Data, providerId,
            x => new AIModel
            {
                Id = ProtocolParsingHelpers.SafeGetString(x, OpenAICompatiblePropertyNames.Id),
                DisplayName = ResolveDisplayName(x),
                Description =
                    x.TryGetProperty(OpenAICompatiblePropertyNames.Description, out var description)
                        ? description.GetString()
                        : null,
                OwnedBy = ResolveOwnedBy(x),
                ProviderId = providerId,
                ContextWindow = ResolveContextWindow(x),
                PromptPrice = ResolvePricePerMillion(x, OpenAICompatiblePropertyNames.Prompt),
                CompletionPrice = ResolvePricePerMillion(x, OpenAICompatiblePropertyNames.Completion),
                Modality = x.TryGetProperty(OpenAICompatiblePropertyNames.Architecture, out var arch)
                             && arch.TryGetProperty(OpenAICompatiblePropertyNames.Modality, out var mod)
                                 ? mod.GetString()
                                 : null
            });

    private static string ResolveDisplayName(JsonElement model)
    {
        foreach (var key in DisplayNameKeys)
            if (model.TryGetProperty(key, out var value))
                return value.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static string? ResolveOwnedBy(JsonElement model)
    {
        if (model.TryGetProperty(OpenAICompatiblePropertyNames.OwnedBy, out var owner))
            return owner.GetString();
        if (model.TryGetProperty(OpenAICompatiblePropertyNames.Id, out var id) && id.GetString()?.Split('/') is { Length: > 1 } parts)
            return parts[0];
        return null;
    }

    private static int? ResolveContextWindow(JsonElement model)
    {
        foreach (var key in ContextWindowKeys)
            if (model.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number)
                return value.GetInt32();
        return null;
    }

    private static decimal? ResolvePricePerMillion(JsonElement model, string pricingKey)
    {
        if (!model.TryGetProperty(OpenAICompatiblePropertyNames.Pricing, out var pricing)
            || pricing.ValueKind != JsonValueKind.Object
            || !pricing.TryGetProperty(pricingKey, out var value))
            return null;

        decimal price;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out price)
            || value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(),
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out price))
            return price * PricePerMillionMultiplier;
        return null;
    }

    /// <summary>
    /// Parses an OpenAI-compatible response into a <see cref="ChatCompletionResponse"/>.
    /// </summary>
    public static ChatCompletionResponse ParseResponse(JsonElement json)
    {
        var usage = UsageInfoParser.Parse(
            json,
            OpenAICompatiblePropertyNames.Usage,
            OpenAICompatiblePropertyNames.PromptTokens,
            OpenAICompatiblePropertyNames.CompletionTokens,
            OpenAICompatiblePropertyNames.TotalTokens);

        var content = string.Empty;
        string? reasoningContent = null;
        string? finishReason = null;
        List<ToolCall>? toolCalls = null;

        if (json.TryGetProperty(OpenAICompatiblePropertyNames.Choices, out var choices) && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty(OpenAICompatiblePropertyNames.Message, out var message))
            {
                if (message.TryGetProperty(OpenAICompatiblePropertyNames.Content, out var messageContent))
                {
                    content = messageContent.GetString() ?? string.Empty;
                }

                if (message.TryGetProperty(OpenAICompatiblePropertyNames.ReasoningContent, out var reasoningProp))
                {
                    reasoningContent = reasoningProp.GetString();
                }

                if (message.TryGetProperty(OpenAICompatiblePropertyNames.ToolCalls, out var tc)
                    && tc.ValueKind == JsonValueKind.Array)
                {
                    toolCalls = new List<ToolCall>();
                    foreach (var tcElement in tc.EnumerateArray())
                    {
                        var id = tcElement.TryGetProperty(OpenAICompatiblePropertyNames.Id, out var tcId)
                                     ? tcId.GetString() ?? string.Empty
                                     : string.Empty;
                        var name = string.Empty;
                        var arguments = string.Empty;
                        if (tcElement.TryGetProperty(OpenAICompatiblePropertyNames.Function, out var func))
                        {
                            name = func.TryGetProperty(OpenAICompatiblePropertyNames.Name, out var funcName)
                                       ? funcName.GetString() ?? string.Empty
                                       : string.Empty;
                            arguments = func.TryGetProperty(OpenAICompatiblePropertyNames.Arguments, out var args)
                                            ? args.GetString() ?? string.Empty
                                            : string.Empty;
                        }

                        toolCalls.Add(new ToolCall { Id = id, Name = name, Arguments = arguments });
                    }
                }
            }

            if (choice.TryGetProperty(OpenAICompatiblePropertyNames.FinishReason, out var fr)
                && fr.ValueKind == JsonValueKind.String)
            {
                finishReason = fr.GetString();
            }
        }

        var responseId = json.TryGetProperty(OpenAICompatiblePropertyNames.Id, out var idProp)
                             ? idProp.GetString() ?? string.Empty
                             : string.Empty;
        var model = json.TryGetProperty(OpenAICompatiblePropertyNames.Model, out var modelProp)
                        ? modelProp.GetString() ?? string.Empty
                        : string.Empty;

        return new ChatCompletionResponse
                   {
                       Id = responseId,
                       Model = model,
                       Content = content,
                       ReasoningContent = reasoningContent,
                       FinishReason = finishReason,
                       Usage = usage,
                       ToolCalls = toolCalls
                   };
    }

    /// <summary>
    /// Parses a streaming chunk from an OpenAI-compatible response.
    /// </summary>
    public static StreamingChatChunk? ParseStreamChunk(JsonElement json)
    {
        if (!json.TryGetProperty(OpenAICompatiblePropertyNames.Choices, out var choices) || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var choice = choices[0];

        if (!choice.TryGetProperty(OpenAICompatiblePropertyNames.Delta, out var delta))
        {
            // No delta — check for bare finish_reason (some providers).
            var bareFinish = choice.TryGetProperty(OpenAICompatiblePropertyNames.FinishReason, out var bareFr)
                             && bareFr.ValueKind != JsonValueKind.Null
                             && !string.IsNullOrWhiteSpace(bareFr.GetString());
            return bareFinish
                       ? new StreamingChatChunk { IsCompleted = true }
                       : null;
        }

        var chunk = new StreamingChatChunk();
        var hasData = false;

        // Content delta.
        if (delta.TryGetProperty(OpenAICompatiblePropertyNames.Content, out var deltaContent))
        {
            chunk = chunk with { Content = deltaContent.GetString() ?? string.Empty };
            hasData = true;
        }

        // Reasoning content delta (some providers stream chain-of-thought separately).
        if (delta.TryGetProperty(OpenAICompatiblePropertyNames.ReasoningContent, out var deltaReasoning))
        {
            chunk = chunk with { ReasoningContent = deltaReasoning.GetString() };
            hasData = true;
        }

        // Tool-call deltas (OpenAI streaming format).
        if (delta.TryGetProperty(OpenAICompatiblePropertyNames.ToolCalls, out var tcArray)
            && tcArray.ValueKind == JsonValueKind.Array)
        {
            var deltas = new List<StreamingToolCallDelta>();
            foreach (var tc in tcArray.EnumerateArray())
            {
                string? funcName = null;
                string? argsFragment = null;
                if (tc.TryGetProperty(OpenAICompatiblePropertyNames.Function, out var func))
                {
                    funcName = func.TryGetProperty(OpenAICompatiblePropertyNames.Name, out var fn)
                                   ? fn.GetString()
                                   : null;
                    argsFragment = func.TryGetProperty(OpenAICompatiblePropertyNames.Arguments, out var args)
                                       ? args.GetString()
                                       : null;
                }

                var tcDelta = new StreamingToolCallDelta
                {
                    Index = tc.TryGetProperty(OpenAICompatiblePropertyNames.Index, out var idx)
                                && idx.ValueKind == JsonValueKind.Number
                                    ? idx.GetInt32()
                                    : 0,
                    Id = tc.TryGetProperty(OpenAICompatiblePropertyNames.Id, out var tcId)
                             ? tcId.GetString()
                             : null,
                    Name = funcName,
                    ArgumentsFragment = argsFragment
                };

                deltas.Add(tcDelta);
            }

            if (deltas.Count > 0)
            {
                chunk = chunk with { ToolCalls = deltas };
                hasData = true;
            }
        }

        if (hasData)
        {
            return chunk;
        }

        // No content or tool_calls — check finish_reason for completion signal.
        var isCompleted = choice.TryGetProperty(OpenAICompatiblePropertyNames.FinishReason, out var finishReason)
                          && finishReason.ValueKind != JsonValueKind.Null
                          && !string.IsNullOrWhiteSpace(finishReason.GetString());

        return isCompleted
                   ? new StreamingChatChunk { IsCompleted = true }
                   : null;
    }

    /// <summary>
    /// Maps an <see cref="EmbeddingRequest"/> to an OpenAI-compatible request payload.
    /// </summary>
    public static Dictionary<string, object> MapEmbeddingsRequest(EmbeddingRequest request)
    {
        return new Dictionary<string, object>
        {
            [OpenAICompatiblePropertyNames.Model] = request.Model,
            [OpenAICompatiblePropertyNames.Input] = request.Input
        };
    }

    /// <summary>
    /// Parses an OpenAI-compatible embeddings response into an <see cref="EmbeddingResponse"/>.
    /// Validates that each data entry has a valid index and non-empty embedding array.
    /// </summary>
    public static EmbeddingResponse ParseEmbeddingsResponse(JsonElement json)
    {
        var model = json.TryGetProperty(OpenAICompatiblePropertyNames.Model, out var modelProp)
                        ? modelProp.GetString() ?? string.Empty
                        : string.Empty;

        var usage = new EmbeddingUsage();
        if (json.TryGetProperty(OpenAICompatiblePropertyNames.Usage, out var usageProp))
        {
            usage = new EmbeddingUsage
            {
                PromptTokens = usageProp.TryGetProperty(OpenAICompatiblePropertyNames.PromptTokens, out var promptTokens)
                               && promptTokens.ValueKind == JsonValueKind.Number
                                   ? promptTokens.GetInt32()
                                   : 0
            };
        }

        var data = new List<EmbeddingData>();
        if (!json.TryGetProperty(OpenAICompatiblePropertyNames.Data, out var dataProp)
            || dataProp.ValueKind != JsonValueKind.Array)
        {
            throw new AiException(
                AiErrorCodes.EmbeddingFailed,
                "Embedding response is missing the data array.");
        }

        var seenIndices = new HashSet<int>();
        foreach (var item in dataProp.EnumerateArray())
        {
            if (!item.TryGetProperty(OpenAICompatiblePropertyNames.Index, out var indexProp)
                || indexProp.ValueKind != JsonValueKind.Number)
            {
                throw new AiException(
                    AiErrorCodes.EmbeddingFailed,
                    "Embedding response contains a data entry without a valid index.");
            }

            var index = indexProp.GetInt32();
            if (index < 0)
            {
                throw new AiException(
                    AiErrorCodes.EmbeddingFailed,
                    $"Embedding response contains a data entry with a negative index: {index}.");
            }

            if (!seenIndices.Add(index))
            {
                throw new AiException(
                    AiErrorCodes.EmbeddingFailed,
                    $"Embedding response contains duplicate index: {index}.");
            }

            if (!item.TryGetProperty(OpenAICompatiblePropertyNames.Embedding, out var embeddingProp)
                || embeddingProp.ValueKind != JsonValueKind.Array)
            {
                throw new AiException(
                    AiErrorCodes.EmbeddingFailed,
                    $"Embedding response contains a data entry at index {index} without an embedding array.");
            }

            var embedding = new List<float>();
            foreach (var element in embeddingProp.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out var value))
                {
                    embedding.Add(value);
                }
            }

            if (embedding.Count == 0)
            {
                throw new AiException(
                    AiErrorCodes.EmbeddingFailed,
                    $"Embedding response contains an empty embedding array at index {index}.");
            }

            data.Add(new EmbeddingData
            {
                Index = index,
                Embedding = embedding.ToArray()
            });
        }

        // Re-sort by index to ensure the caller receives vectors in input order
        var sortedData = data.OrderBy(d => d.Index).ToList();

        return new EmbeddingResponse
        {
            Data = sortedData,
            Model = model,
            Usage = usage
        };
    }
}