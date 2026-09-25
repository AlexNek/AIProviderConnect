using System.Text.Json;

using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Protocols;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Protocols;

public class KeyQueryWireProtocolTests
{
    [Fact]
    public void ParseResponse_MalformedCandidate_NoContentProperty_DoesNotThrow()
    {
        // Arrange — candidate exists but has no "content" property
        var json = JsonDocument.Parse("""
        {
            "id": "resp-1",
            "candidates": [{ "finishReason": "STOP" }],
            "usageMetadata": {
                "promptTokenCount": 10,
                "candidatesTokenCount": 5,
                "totalTokenCount": 15
            }
        }
        """).RootElement;

        // Act
        var response = KeyQueryWireProtocol.ParseResponse("test-model", json);

        // Assert
        response.Content.Should().BeEmpty();
        response.Usage.PromptTokens.Should().Be(10);
        response.Usage.CompletionTokens.Should().Be(5);
        response.Usage.TotalTokens.Should().Be(15);
    }

    [Fact]
    public void ParseResponse_AbsentTotalTokenCount_FallsBackToPromptPlusCompletion()
    {
        // Arrange — usageMetadata has prompt/candidates counts but no totalTokenCount
        var json = JsonDocument.Parse("""
        {
            "id": "resp-1",
            "candidates": [{
                "content": { "parts": [{ "text": "Hello" }] },
                "finishReason": "STOP"
            }],
            "usageMetadata": {
                "promptTokenCount": 8,
                "candidatesTokenCount": 3
            }
        }
        """).RootElement;

        // Act
        var response = KeyQueryWireProtocol.ParseResponse("test-model", json);

        // Assert
        response.Usage.PromptTokens.Should().Be(8);
        response.Usage.CompletionTokens.Should().Be(3);
        response.Usage.TotalTokens.Should().Be(11, "absent totalTokenCount falls back to prompt + completion");
    }

    [Fact]
    public void ParseResponse_EmptyCandidatesArray_ReturnsEmptyContent()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "id": "resp-1",
            "candidates": [],
            "usageMetadata": { "promptTokenCount": 1, "candidatesTokenCount": 0, "totalTokenCount": 1 }
        }
        """).RootElement;

        // Act
        var response = KeyQueryWireProtocol.ParseResponse("test-model", json);

        // Assert
        response.Content.Should().BeEmpty();
    }

    [Fact]
    public void ApplyProtocolConfiguration_SetsCustomAuthHeaderName()
    {
        // Arrange
        var options = new KeyQueryOptions
        {
            ProtocolConfiguration = new Dictionary<string, string>
            {
                ["apiKeyHeaderName"] = "x-goog-api-key"
            }
        };

        // Act
        KeyQueryWireProtocol.ApplyProtocolConfiguration(options);

        // Assert
        options.CustomAuthHeaderName.Should().Be("x-goog-api-key");
    }

    [Fact]
    public void ApplyProtocolConfiguration_NullDictionary_NoOp()
    {
        // Arrange
        var options = new KeyQueryOptions { ProtocolConfiguration = null };

        // Act
        var act = () => KeyQueryWireProtocol.ApplyProtocolConfiguration(options);

        // Assert
        act.Should().NotThrow();
        options.CustomAuthHeaderName.Should().BeNull();
    }

    [Fact]
    public void ApplyProtocolConfiguration_SetsEndpointPatternsFromProtocolConfiguration()
    {
        // Arrange
        var options = new KeyQueryOptions
        {
            ProtocolConfiguration = new Dictionary<string, string>
            {
                ["generationEndpoint"] = "v1/{model}:customGenerate",
                ["streamEndpoint"] = "v1/{model}:customStream"
            }
        };

        // Act
        KeyQueryWireProtocol.ApplyProtocolConfiguration(options);

        // Assert
        options.ChatEndpoint.Should().Be("v1/{model}:customGenerate");
        options.StreamEndpoint.Should().Be("v1/{model}:customStream");
    }

    [Fact]
    public void ApplyProtocolConfiguration_MissingEndpointKeys_KeepsDefaults()
    {
        // Arrange — only apiKeyHeaderName, no endpoint keys
        var options = new KeyQueryOptions
        {
            ProtocolConfiguration = new Dictionary<string, string>
            {
                ["apiKeyHeaderName"] = "x-custom-key"
            }
        };

        // Act
        KeyQueryWireProtocol.ApplyProtocolConfiguration(options);

        // Assert — defaults from EndpointDefaults.KeyQuery are preserved
        options.ChatEndpoint.Should().Be(
            AIProviderConnect.Constants.EndpointDefaults.KeyQuery.GenerateContent);
        options.StreamEndpoint.Should().Be(
            AIProviderConnect.Constants.EndpointDefaults.KeyQuery.StreamGenerateContent);
    }

    [Fact]
    public void MapRequest_WithTools_SerializesFunctionDeclarations()
    {
        // Arrange
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"city":{"type":"string"}}}""").RootElement;
        var request = new ChatCompletionRequest
        {
            Model = "gemini-pro",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Weather?" }],
            Tools = [new ToolDefinition { Name = "get_weather", Description = "Get weather", Parameters = schema }]
        };

        // Act
        var payload = KeyQueryWireProtocol.MapRequest(request);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        // Assert
        var tools = doc.RootElement.GetProperty("tools");
        tools.GetArrayLength().Should().Be(1);
        var declarations = tools[0].GetProperty("functionDeclarations");
        declarations.GetArrayLength().Should().Be(1);
        var decl = declarations[0];
        decl.GetProperty("name").GetString().Should().Be("get_weather");
        decl.GetProperty("description").GetString().Should().Be("Get weather");
        decl.GetProperty("parameters").GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public void MapRequest_AssistantWithToolCalls_SerializesFunctionCallParts()
    {
        // Arrange
        var request = new ChatCompletionRequest
        {
            Model = "gemini-pro",
            Messages =
            [
                new ChatMessage { Role = EChatRole.User, Content = "Weather?" },
                new ChatMessage
                {
                    Role = EChatRole.Assistant,
                    ToolCalls = [new ToolCall { Id = "fc1", Name = "get_weather", Arguments = """{"city":"Berlin"}""" }]
                }
            ]
        };

        // Act
        var payload = KeyQueryWireProtocol.MapRequest(request);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        // Assert
        var contents = doc.RootElement.GetProperty("contents");
        contents.GetArrayLength().Should().Be(2);
        var modelMsg = contents[1];
        modelMsg.GetProperty("role").GetString().Should().Be("model");
        var parts = modelMsg.GetProperty("parts");
        parts.GetArrayLength().Should().Be(1);
        var fc = parts[0].GetProperty("functionCall");
        fc.GetProperty("name").GetString().Should().Be("get_weather");
        fc.GetProperty("args").GetProperty("city").GetString().Should().Be("Berlin");
    }

    [Fact]
    public void MapRequest_ToolResultMessage_SerializesAsFunctionRoleWithFunctionResponse()
    {
        // Arrange
        var request = new ChatCompletionRequest
        {
            Model = "gemini-pro",
            Messages =
            [
                new ChatMessage { Role = EChatRole.User, Content = "Weather?" },
                new ChatMessage
                {
                    Role = EChatRole.Tool,
                    ToolCallId = "get_weather",
                    Content = """{"temperature":18}"""
                }
            ]
        };

        // Act
        var payload = KeyQueryWireProtocol.MapRequest(request);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        // Assert
        var contents = doc.RootElement.GetProperty("contents");
        var functionMsg = contents[1];
        functionMsg.GetProperty("role").GetString().Should().Be("function");
        var parts = functionMsg.GetProperty("parts");
        parts.GetArrayLength().Should().Be(1);
        var fr = parts[0].GetProperty("functionResponse");
        fr.GetProperty("name").GetString().Should().Be("get_weather");
        fr.GetProperty("response").GetProperty("temperature").GetInt32().Should().Be(18);
    }

    [Fact]
    public void ParseResponse_WithFunctionCall_PopulatesToolCalls()
    {
        // Arrange
        var json = JsonDocument.Parse("""
        {
            "candidates": [{
                "content": {
                    "parts": [
                        {"text": "I'll use the tool."},
                        {"functionCall": {"name": "get_weather", "args": {"city": "Berlin"}}}
                    ]
                },
                "finishReason": "STOP"
            }],
            "usageMetadata": {
                "promptTokenCount": 10,
                "candidatesTokenCount": 5,
                "totalTokenCount": 15
            }
        }
        """).RootElement;

        // Act
        var response = KeyQueryWireProtocol.ParseResponse("gemini-pro", json);

        // Assert
        response.Content.Should().Be("I'll use the tool.");
        response.ToolCalls.Should().HaveCount(1);
        response.ToolCalls![0].Name.Should().Be("get_weather");
        response.ToolCalls![0].Arguments.Should().Contain("Berlin");
        response.ToolCalls![0].Id.Should().NotBeEmpty("a synthetic ID is generated since Gemini provides none");
    }
}
