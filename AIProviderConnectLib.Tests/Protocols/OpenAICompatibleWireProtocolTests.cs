using System.Text.Json;

using AIProviderConnect.Models;
using AIProviderConnect.Protocols;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Protocols;

/// <summary>
/// Tests for <see cref="OpenAICompatibleWireProtocol"/>, focusing on the streaming
/// chunk parser that was silently dropping tool-call deltas.
/// </summary>
public class OpenAICompatibleWireProtocolTests
{
    // --- ParseStreamChunk: content ---

    [Fact]
    public void ParseStreamChunk_ContentDelta_ReturnsContent()
    {
        var json = JsonDocument.Parse("""
            {"choices":[{"delta":{"content":"Hello"}}]}
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().NotBeNull();
        chunk!.Content.Should().Be("Hello");
        chunk.ToolCalls.Should().BeNull();
        chunk.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void ParseStreamChunk_NullContent_ReturnsEmptyString()
    {
        var json = JsonDocument.Parse("""
            {"choices":[{"delta":{"content":null}}]}
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().NotBeNull();
        chunk!.Content.Should().BeEmpty();
    }

    // --- ParseStreamChunk: tool-call deltas ---

    [Fact]
    public void ParseStreamChunk_ToolCallDelta_ExtractsIndexIdNameArgs()
    {
        var json = JsonDocument.Parse("""
            {
              "choices": [{
                "delta": {
                  "tool_calls": [{
                    "index": 0,
                    "id": "call_abc",
                    "function": {
                      "name": "search_web",
                      "arguments": "{\"query\":"
                    }
                  }]
                }
              }]
            }
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().NotBeNull();
        chunk!.Content.Should().BeEmpty();
        chunk.ToolCalls.Should().HaveCount(1);
        chunk.ToolCalls![0].Index.Should().Be(0);
        chunk.ToolCalls[0].Id.Should().Be("call_abc");
        chunk.ToolCalls[0].Name.Should().Be("search_web");
        chunk.ToolCalls[0].ArgumentsFragment.Should().Be("{\"query\":");
        chunk.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void ParseStreamChunk_ToolCallArgumentFragment_ExtractsPartialJson()
    {
        var json = JsonDocument.Parse("""
            {
              "choices": [{
                "delta": {
                  "tool_calls": [{
                    "index": 0,
                    "function": {
                      "arguments": "AI pricing\"}"
                    }
                  }]
                }
              }]
            }
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().NotBeNull();
        chunk!.ToolCalls.Should().HaveCount(1);
        chunk.ToolCalls![0].ArgumentsFragment.Should().Be("AI pricing\"}");
        chunk.ToolCalls[0].Id.Should().BeNull();
        chunk.ToolCalls[0].Name.Should().BeNull();
    }

    [Fact]
    public void ParseStreamChunk_MultipleToolCallDeltas_ExtractsAll()
    {
        var json = JsonDocument.Parse("""
            {
              "choices": [{
                "delta": {
                  "tool_calls": [
                    { "index": 0, "id": "call_1", "function": { "name": "search", "arguments": "" } },
                    { "index": 1, "id": "call_2", "function": { "name": "fetch", "arguments": "" } }
                  ]
                }
              }]
            }
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().NotBeNull();
        chunk!.ToolCalls.Should().HaveCount(2);
        chunk.ToolCalls![0].Index.Should().Be(0);
        chunk.ToolCalls[0].Id.Should().Be("call_1");
        chunk.ToolCalls[0].Name.Should().Be("search");
        chunk.ToolCalls[1].Index.Should().Be(1);
        chunk.ToolCalls[1].Id.Should().Be("call_2");
        chunk.ToolCalls[1].Name.Should().Be("fetch");
    }

    [Fact]
    public void ParseStreamChunk_ContentAndToolCalls_BothExtracted()
    {
        var json = JsonDocument.Parse("""
            {
              "choices": [{
                "delta": {
                  "content": "Let me search",
                  "tool_calls": [{
                    "index": 0,
                    "id": "call_x",
                    "function": { "name": "search_web", "arguments": "{}" }
                  }]
                }
              }]
            }
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().NotBeNull();
        chunk!.Content.Should().Be("Let me search");
        chunk.ToolCalls.Should().HaveCount(1);
        chunk.ToolCalls![0].Name.Should().Be("search_web");
    }

    // --- ParseStreamChunk: completion ---

    [Fact]
    public void ParseStreamChunk_FinishReason_ReturnsIsCompleted()
    {
        var json = JsonDocument.Parse("""
            {"choices":[{"finish_reason":"stop"}]}
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().NotBeNull();
        chunk!.IsCompleted.Should().BeTrue();
        chunk.Content.Should().BeEmpty();
        chunk.ToolCalls.Should().BeNull();
    }

    [Fact]
    public void ParseStreamChunk_FinishReasonToolCalls_ReturnsIsCompleted()
    {
        var json = JsonDocument.Parse("""
            {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        // Empty delta with finish_reason → completion signal, no data
        chunk.Should().NotBeNull();
        chunk!.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void ParseStreamChunk_EmptyDelta_NoFinishReason_ReturnsNull()
    {
        var json = JsonDocument.Parse("""
            {"choices":[{"delta":{}}]}
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().BeNull();
    }

    [Fact]
    public void ParseStreamChunk_NoChoices_ReturnsNull()
    {
        var json = JsonDocument.Parse("""
            {"choices":[]}
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().BeNull();
    }

    [Fact]
    public void ParseStreamChunk_NoDelta_ChecksFinishReason()
    {
        // Some providers send finish_reason directly in the choice without a delta
        var json = JsonDocument.Parse("""
            {"choices":[{"finish_reason":"length"}]}
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().NotBeNull();
        chunk!.IsCompleted.Should().BeTrue();
    }

    // --- ParseResponse: reasoning_content ---

    [Fact]
    public void ParseResponse_WithReasoningContent_ExtractsField()
    {
        var json = JsonDocument.Parse("""
            {
              "id": "chatcmpl-123",
              "model": "deepseek-reasoner",
              "choices": [{
                "message": {
                  "role": "assistant",
                  "content": "The answer is 42.",
                  "reasoning_content": "Let me think step by step."
                },
                "finish_reason": "stop"
              }]
            }
            """).RootElement;

        var response = OpenAICompatibleWireProtocol.ParseResponse(json);

        response.Content.Should().Be("The answer is 42.");
        response.ReasoningContent.Should().Be("Let me think step by step.");
    }

    [Fact]
    public void ParseResponse_WithoutReasoningContent_FieldIsNull()
    {
        var json = JsonDocument.Parse("""
            {
              "id": "chatcmpl-456",
              "model": "gpt-4",
              "choices": [{
                "message": {
                  "role": "assistant",
                  "content": "Hello"
                },
                "finish_reason": "stop"
              }]
            }
            """).RootElement;

        var response = OpenAICompatibleWireProtocol.ParseResponse(json);

        response.Content.Should().Be("Hello");
        response.ReasoningContent.Should().BeNull();
    }

    [Fact]
    public void ParseResponse_WithEmptyReasoningContent_FieldIsEmpty()
    {
        var json = JsonDocument.Parse("""
            {
              "id": "chatcmpl-789",
              "model": "deepseek-reasoner",
              "choices": [{
                "message": {
                  "role": "assistant",
                  "content": "Answer",
                  "reasoning_content": ""
                },
                "finish_reason": "stop"
              }]
            }
            """).RootElement;

        var response = OpenAICompatibleWireProtocol.ParseResponse(json);

        response.ReasoningContent.Should().BeEmpty();
    }

    [Fact]
    public void ParseResponse_WithNullReasoningContent_FieldIsNull()
    {
        var json = JsonDocument.Parse("""
            {
              "id": "chatcmpl-101",
              "model": "deepseek-reasoner",
              "choices": [{
                "message": {
                  "role": "assistant",
                  "content": "Answer",
                  "reasoning_content": null
                },
                "finish_reason": "stop"
              }]
            }
            """).RootElement;

        var response = OpenAICompatibleWireProtocol.ParseResponse(json);

        response.ReasoningContent.Should().BeNull();
    }

    // --- ParseStreamChunk: reasoning_content ---

    [Fact]
    public void ParseStreamChunk_ReasoningContentDelta_ExtractsField()
    {
        var json = JsonDocument.Parse("""
            {"choices":[{"delta":{"reasoning_content":"Let me think"}}]}
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().NotBeNull();
        chunk!.ReasoningContent.Should().Be("Let me think");
        chunk.Content.Should().BeEmpty();
    }

    [Fact]
    public void ParseStreamChunk_ContentAndReasoningContent_BothExtracted()
    {
        var json = JsonDocument.Parse("""
            {"choices":[{"delta":{"content":"The answer","reasoning_content":"thinking..."}}]}
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().NotBeNull();
        chunk!.Content.Should().Be("The answer");
        chunk.ReasoningContent.Should().Be("thinking...");
    }

    [Fact]
    public void ParseStreamChunk_WithoutReasoningContent_FieldIsNull()
    {
        var json = JsonDocument.Parse("""
            {"choices":[{"delta":{"content":"Hello"}}]}
            """).RootElement;

        var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);

        chunk.Should().NotBeNull();
        chunk!.ReasoningContent.Should().BeNull();
    }

    // --- ParseStreamChunk: real-world multi-chunk tool call sequence ---

    [Fact]
    public void ParseStreamChunk_FullToolCallSequence_AssemblesCorrectly()
    {
        // Simulates the exact SSE sequence from a small model calling search_web("AI pricing")
        var chunks = new[]
        {
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_99","function":{"name":"search_web","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"query\":"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"AI pricing\"}"}}]}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""",
        };

        var argsBuilder = new System.Text.StringBuilder();

        foreach (var raw in chunks)
        {
            var json = JsonDocument.Parse(raw).RootElement;
            var chunk = OpenAICompatibleWireProtocol.ParseStreamChunk(json);
            chunk.Should().NotBeNull();

            if (chunk!.ToolCalls is { Count: > 0 })
            {
                foreach (var tc in chunk.ToolCalls)
                {
                    if (tc.ArgumentsFragment is not null)
                        argsBuilder.Append(tc.ArgumentsFragment);
                }
            }
        }

        argsBuilder.ToString().Should().Be("{\"query\":\"AI pricing\"}");
    }

    // --- MapRequest: multimodal content parts ---

    [Fact]
    public void MapRequest_WithTextAndImageContentParts_SerializesContentAsArray()
    {
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages =
            [
                new ChatMessage
                {
                    Role = EChatRole.User,
                    ContentParts =
                    [
                        new ContentPart { Type = "text", Text = "Describe this page." },
                        new ContentPart
                        {
                            Type = "image_url",
                            Image = ImageContent.FromUrl("https://test.example.com/img.png", "high")
                        }
                    ]
                }
            ]
        };

        var payload = OpenAICompatibleWireProtocol.MapRequest(request, stream: false);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        content.ValueKind.Should().Be(JsonValueKind.Array);
        content.GetArrayLength().Should().Be(2);
        content[0].GetProperty("type").GetString().Should().Be("text");
        content[0].GetProperty("text").GetString().Should().Be("Describe this page.");
        content[1].GetProperty("type").GetString().Should().Be("image_url");
        content[1].GetProperty("image_url").GetProperty("url").GetString()
            .Should().Be("https://test.example.com/img.png");
        content[1].GetProperty("image_url").GetProperty("detail").GetString().Should().Be("high");
    }

    [Fact]
    public void MapRequest_WithImageBytes_SerializesAsDataUri()
    {
        // Arrange
        var bytes = new byte[] { 1, 2, 3 };
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages =
            [
                new ChatMessage
                {
                    Role = EChatRole.User,
                    ContentParts =
                    [
                        new ContentPart
                        {
                            Type = "image_url",
                            Image = ImageContent.FromBytes(bytes, "image/png")
                        }
                    ]
                }
            ]
        };

        // Act
        var payload = OpenAICompatibleWireProtocol.MapRequest(request, stream: false);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        // Assert
        var url = content[0].GetProperty("image_url").GetProperty("url").GetString();
        url.Should().Be($"data:image/png;base64,{Convert.ToBase64String(bytes)}");
        content[0].GetProperty("image_url").TryGetProperty("detail", out _).Should().BeFalse();
    }

    [Fact]
    public void MapRequest_WithImageTypeAlias_SerializesAsImagePart()
    {
        // Arrange — "image" is an accepted alias for "image_url"
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages =
            [
                new ChatMessage
                {
                    Role = EChatRole.User,
                    ContentParts =
                    [
                        new ContentPart
                        {
                            Type = "image",
                            Image = ImageContent.FromUrl("https://test.example.com/img.png", "low")
                        }
                    ]
                }
            ]
        };

        // Act
        var payload = OpenAICompatibleWireProtocol.MapRequest(request, stream: false);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var part = doc.RootElement.GetProperty("messages")[0].GetProperty("content")[0];

        // Assert
        part.GetProperty("type").GetString().Should().Be("image_url");
        part.GetProperty("image_url").GetProperty("url").GetString()
            .Should().Be("https://test.example.com/img.png");
        part.GetProperty("image_url").GetProperty("detail").GetString().Should().Be("low");
    }

    [Fact]
    public void MapRequest_WithImageWithoutDetail_OmitsDetailKey()
    {
        // Arrange
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages =
            [
                new ChatMessage
                {
                    Role = EChatRole.User,
                    ContentParts =
                    [
                        new ContentPart
                        {
                            Type = "image_url",
                            Image = ImageContent.FromUrl("https://test.example.com/img.png")
                        }
                    ]
                }
            ]
        };

        // Act
        var payload = OpenAICompatibleWireProtocol.MapRequest(request, stream: false);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var imageUrl = doc.RootElement.GetProperty("messages")[0]
            .GetProperty("content")[0].GetProperty("image_url");

        // Assert
        imageUrl.GetProperty("url").GetString().Should().Be("https://test.example.com/img.png");
        imageUrl.TryGetProperty("detail", out _).Should().BeFalse();
    }

    [Fact]
    public void MapRequest_WithPlainTextContent_SerializesContentAsString()
    {
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
        };

        var payload = OpenAICompatibleWireProtocol.MapRequest(request, stream: false);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        content.ValueKind.Should().Be(JsonValueKind.String);
        content.GetString().Should().Be("Hello");
    }

    [Fact]
    public void MapRequest_WithEmptyContentParts_FallsBackToStringContent()
    {
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages =
            [
                new ChatMessage { Role = EChatRole.User, Content = "Hello", ContentParts = [] }
            ]
        };

        var payload = OpenAICompatibleWireProtocol.MapRequest(request, stream: false);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        content.ValueKind.Should().Be(JsonValueKind.String);
        content.GetString().Should().Be("Hello");
    }

    // --- MapRequest: optional parameter serialization ---

    [Fact]
    public void MapRequest_WithAllOptionalParameters_SerializesCorrectKeys()
    {
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }],
            Stop = ["\n", "END"],
            TopP = 0.9f,
            FrequencyPenalty = 1.5f,
            PresencePenalty = 0.5f
        };

        var payload = OpenAICompatibleWireProtocol.MapRequest(request, stream: false);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        root.GetProperty("stop").GetArrayLength().Should().Be(2);
        root.GetProperty("stop")[0].GetString().Should().Be("\n");
        root.GetProperty("stop")[1].GetString().Should().Be("END");
        root.GetProperty("top_p").GetSingle().Should().Be(0.9f);
        root.GetProperty("frequency_penalty").GetSingle().Should().Be(1.5f);
        root.GetProperty("presence_penalty").GetSingle().Should().Be(0.5f);
    }

    [Fact]
    public void MapRequest_WithNullOptionalParameters_OmitsKeys()
    {
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
        };

        var payload = OpenAICompatibleWireProtocol.MapRequest(request, stream: false);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var root = doc.RootElement;

        root.TryGetProperty("stop", out _).Should().BeFalse();
        root.TryGetProperty("top_p", out _).Should().BeFalse();
        root.TryGetProperty("frequency_penalty", out _).Should().BeFalse();
        root.TryGetProperty("presence_penalty", out _).Should().BeFalse();
    }

    [Fact]
    public void ParseResponse_AbsentTotalTokens_FallsBackToPromptPlusCompletion()
    {
        // Arrange — usage node has prompt_tokens and completion_tokens but no total_tokens
        var json = JsonDocument.Parse("""
        {
            "id": "resp-1",
            "choices": [{ "message": { "role": "assistant", "content": "Hi" }, "finish_reason": "stop" }],
            "usage": { "prompt_tokens": 10, "completion_tokens": 5 }
        }
        """).RootElement;

        // Act
        var response = OpenAICompatibleWireProtocol.ParseResponse(json);

        // Assert
        response.Usage.PromptTokens.Should().Be(10);
        response.Usage.CompletionTokens.Should().Be(5);
        response.Usage.TotalTokens.Should().Be(15, "absent total_tokens falls back to prompt + completion");
    }

    [Fact]
    public void ParseResponse_PresentTotalTokens_UsesExplicitValue()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "id": "resp-1",
            "choices": [{ "message": { "role": "assistant", "content": "Hi" }, "finish_reason": "stop" }],
            "usage": { "prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 20 }
        }
        """).RootElement;

        // Act
        var response = OpenAICompatibleWireProtocol.ParseResponse(json);

        // Assert
        response.Usage.TotalTokens.Should().Be(20);
    }
}
