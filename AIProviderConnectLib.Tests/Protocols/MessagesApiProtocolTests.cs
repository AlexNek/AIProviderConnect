using System.Text.Json;

using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Protocols;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Protocols;

public class MessagesApiProtocolTests
{
    private static JsonElement FirstMessageContent(ChatCompletionRequest request)
    {
        var payload = MessagesApiProtocol.MapRequest(request);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return doc.RootElement.GetProperty("messages")[0].GetProperty("content").Clone();
    }

    [Fact]
    public void MapRequest_TextOnly_SerializesContentAsString()
    {
        // Arrange
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
        };

        // Act
        var content = FirstMessageContent(request);

        // Assert
        content.ValueKind.Should().Be(JsonValueKind.String);
        content.GetString().Should().Be("Hello");
    }

    [Fact]
    public void MapRequest_WithImageBytes_SerializesBase64Source()
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
        var content = FirstMessageContent(request);

        // Assert
        content.ValueKind.Should().Be(JsonValueKind.Array);
        var block = content[0];
        block.GetProperty("type").GetString().Should().Be("image");
        var source = block.GetProperty("source");
        source.GetProperty("type").GetString().Should().Be("base64");
        source.GetProperty("media_type").GetString().Should().Be("image/png");
        source.GetProperty("data").GetString().Should().Be(Convert.ToBase64String(bytes));
    }

    [Fact]
    public void MapRequest_WithImageUrl_SerializesUrlSource()
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
        var content = FirstMessageContent(request);

        // Assert
        var source = content[0].GetProperty("source");
        source.GetProperty("type").GetString().Should().Be("url");
        source.GetProperty("url").GetString().Should().Be("https://test.example.com/img.png");
    }

    [Fact]
    public void MapRequest_WithTextAndImage_SerializesBothBlocks()
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
                        new ContentPart { Type = "text", Text = "Describe this page." },
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
        var content = FirstMessageContent(request);

        // Assert
        content.GetArrayLength().Should().Be(2);
        content[0].GetProperty("type").GetString().Should().Be("text");
        content[0].GetProperty("text").GetString().Should().Be("Describe this page.");
        content[1].GetProperty("type").GetString().Should().Be("image");
    }

    [Fact]
    public void MapRequest_SystemMessageWithContentParts_SerializesSystemText()
    {
        // Arrange — a system message carrying only ContentParts must not produce a null/empty
        // system field; its text parts are concatenated and image parts are skipped.
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages =
            [
                new ChatMessage
                {
                    Role = EChatRole.System,
                    ContentParts =
                    [
                        new ContentPart { Type = "text", Text = "You are helpful." },
                        new ContentPart
                        {
                            Type = "image_url",
                            Image = ImageContent.FromUrl("https://test.example.com/img.png")
                        }
                    ]
                },
                new ChatMessage { Role = EChatRole.User, Content = "Hi" }
            ]
        };

        // Act
        var payload = MessagesApiProtocol.MapRequest(request);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        // Assert
        doc.RootElement.GetProperty("system").GetString().Should().Be("You are helpful.");
    }

    [Fact]
    public void ApplyProtocolConfiguration_SetsAnthropicVersionHeader()
    {
        // Arrange
        var options = new MessagesApiOptions
        {
            ProtocolConfiguration = new Dictionary<string, string>
            {
                ["anthropicVersion"] = "2023-06-01",
                ["apiKeyHeaderName"] = "x-api-key"
            }
        };

        // Act
        MessagesApiProtocol.ApplyProtocolConfiguration(options);

        // Assert
        options.DefaultHeaders.Should().ContainKey("anthropic-version")
            .WhoseValue.Should().Be("2023-06-01");
    }

    [Fact]
    public void ApplyProtocolConfiguration_SetsCustomAuthHeaderName()
    {
        // Arrange
        var options = new MessagesApiOptions
        {
            ProtocolConfiguration = new Dictionary<string, string>
            {
                ["apiKeyHeaderName"] = "x-api-key"
            }
        };

        // Act
        MessagesApiProtocol.ApplyProtocolConfiguration(options);

        // Assert
        options.CustomAuthHeaderName.Should().Be("x-api-key");
    }

    [Fact]
    public void ApplyProtocolConfiguration_NullDictionary_NoOp()
    {
        // Arrange
        var options = new MessagesApiOptions { ProtocolConfiguration = null };

        // Act
        var act = () => MessagesApiProtocol.ApplyProtocolConfiguration(options);

        // Assert
        act.Should().NotThrow();
        options.CustomAuthHeaderName.Should().BeNull();
        options.DefaultHeaders.Should().BeEmpty();
    }

    [Fact]
    public void ParseModels_UsesIdWhenDisplayNamePropertyIsAbsent()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""{"data":[{"id":"claude-test"}]}""");

        // Act
        var models = MessagesApiProtocol.ParseModels(doc.RootElement, "test-provider");

        // Assert
        models.Should().ContainSingle()
            .Which.DisplayName.Should().Be("claude-test");
    }

    [Fact]
    public void ParseModels_PrefersDisplayNameOverId()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""{"data":[{"id":"claude-test","display_name":"Claude Test"}]}""");

        // Act
        var models = MessagesApiProtocol.ParseModels(doc.RootElement, "test-provider");

        // Assert
        var model = models.Single();
        model.Id.Should().Be("claude-test");
        model.DisplayName.Should().Be("Claude Test");
        model.ProviderId.Should().Be("test-provider");
    }

    [Theory]
    [InlineData("""{"data":[{"id":"claude-test","display_name":""}]}""")]
    [InlineData("""{"data":[{"id":"claude-test","display_name":null}]}""")]
    public void ParseModels_KeepsDisplayNameWhenPropertyIsPresentButEmpty(string json)
    {
        // Arrange — presence, not emptiness, drives the fallback (parity with the OpenAI-compatible protocol)
        using var doc = JsonDocument.Parse(json);

        // Act
        var models = MessagesApiProtocol.ParseModels(doc.RootElement, "test-provider");

        // Assert
        models.Single().DisplayName.Should().BeEmpty();
    }

    [Fact]
    public void ParseModels_ReturnsEmptyList_WhenDataPropertyIsMissing()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""{"models":[{"id":"claude-test"}]}""");

        // Act
        var models = MessagesApiProtocol.ParseModels(doc.RootElement, "test-provider");

        // Assert
        models.Should().BeEmpty();
    }

    [Fact]
    public void MapRequest_WithTools_SerializesToolsArray()
    {
        // Arrange
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"city":{"type":"string"}}}""").RootElement;
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Weather?" }],
            Tools = [new ToolDefinition { Name = "get_weather", Description = "Get weather", Parameters = schema }]
        };

        // Act
        var payload = MessagesApiProtocol.MapRequest(request);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        // Assert
        var tools = doc.RootElement.GetProperty("tools");
        tools.GetArrayLength().Should().Be(1);
        var tool = tools[0];
        tool.GetProperty("name").GetString().Should().Be("get_weather");
        tool.GetProperty("description").GetString().Should().Be("Get weather");
        tool.GetProperty("input_schema").GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public void MapRequest_AssistantWithToolCalls_SerializesToolUseBlocks()
    {
        // Arrange
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages =
            [
                new ChatMessage { Role = EChatRole.User, Content = "Weather?" },
                new ChatMessage
                {
                    Role = EChatRole.Assistant,
                    Content = "I'll check the weather.",
                    ToolCalls = [new ToolCall { Id = "toolu_01ABC", Name = "get_weather", Arguments = """{"city":"Berlin"}""" }]
                }
            ]
        };

        // Act
        var payload = MessagesApiProtocol.MapRequest(request);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        // Assert
        var messages = doc.RootElement.GetProperty("messages");
        messages.GetArrayLength().Should().Be(2);
        var assistantMsg = messages[1];
        assistantMsg.GetProperty("role").GetString().Should().Be("assistant");
        var content = assistantMsg.GetProperty("content");
        content.GetArrayLength().Should().Be(2);
        content[0].GetProperty("type").GetString().Should().Be("text");
        content[0].GetProperty("text").GetString().Should().Be("I'll check the weather.");
        content[1].GetProperty("type").GetString().Should().Be("tool_use");
        content[1].GetProperty("id").GetString().Should().Be("toolu_01ABC");
        content[1].GetProperty("name").GetString().Should().Be("get_weather");
        content[1].GetProperty("input").GetProperty("city").GetString().Should().Be("Berlin");
    }

    [Fact]
    public void MapRequest_ToolResultMessage_SerializesAsUserWithToolResultBlock()
    {
        // Arrange
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages =
            [
                new ChatMessage { Role = EChatRole.User, Content = "Weather?" },
                new ChatMessage
                {
                    Role = EChatRole.Tool,
                    ToolCallId = "toolu_01ABC",
                    Content = """{"temperature":18}"""
                }
            ]
        };

        // Act
        var payload = MessagesApiProtocol.MapRequest(request);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        // Assert
        var messages = doc.RootElement.GetProperty("messages");
        var toolMsg = messages[1];
        toolMsg.GetProperty("role").GetString().Should().Be("user");
        var content = toolMsg.GetProperty("content");
        content.GetArrayLength().Should().Be(1);
        content[0].GetProperty("type").GetString().Should().Be("tool_result");
        content[0].GetProperty("tool_use_id").GetString().Should().Be("toolu_01ABC");
        content[0].GetProperty("content").GetString().Should().Be("""{"temperature":18}""");
    }

    [Fact]
    public void ParseResponse_WithToolUseBlocks_PopulatesToolCalls()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""
        {
            "id": "msg_01ABC",
            "model": "claude-test",
            "content": [
                {"type": "text", "text": "I'll use the tool."},
                {"type": "tool_use", "id": "toolu_01XYZ", "name": "get_weather", "input": {"city": "Berlin"}}
            ],
            "stop_reason": "tool_use",
            "usage": {"input_tokens": 10, "output_tokens": 20}
        }
        """);

        // Act
        var response = MessagesApiProtocol.ParseResponse(doc.RootElement);

        // Assert
        response.Content.Should().Be("I'll use the tool.");
        response.ToolCalls.Should().HaveCount(1);
        response.ToolCalls![0].Id.Should().Be("toolu_01XYZ");
        response.ToolCalls![0].Name.Should().Be("get_weather");
        response.ToolCalls![0].Arguments.Should().Contain("Berlin");
        response.FinishReason.Should().Be("tool_use");
    }
}
