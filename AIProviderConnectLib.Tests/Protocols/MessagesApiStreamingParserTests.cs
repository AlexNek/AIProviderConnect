using System.Text.Json;

using AIProviderConnect.Models;
using AIProviderConnect.Protocols;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Protocols;

public class MessagesApiStreamingParserTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ContentBlockStart_ToolUse_RegistersNewBlock()
    {
        // Arrange
        var parser = new MessagesApiStreamingParser();
        var startJson = Parse(
            """{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_01ABC","name":"get_weather","input":{}}}""");

        // Act — content_block_start returns null (no output chunk)
        var result = parser.ParseStreamChunk(startJson);

        // Assert — the block is registered; subsequent input_json_delta will carry id/name
        result.Should().BeNull();
    }

    [Fact]
    public void InputJsonDelta_FirstFragment_EmitsIdAndName()
    {
        // Arrange
        var parser = new MessagesApiStreamingParser();
        parser.ParseStreamChunk(Parse(
            "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_01ABC\",\"name\":\"get_weather\",\"input\":{}}}"));

        // Act
        var deltaJson = Parse(
            "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"location\\\":\"}}");
        var chunk = parser.ParseStreamChunk(deltaJson);

        // Assert
        chunk.Should().NotBeNull();
        chunk!.ToolCalls.Should().HaveCount(1);
        var tc = chunk.ToolCalls![0];
        tc.Index.Should().Be(0);
        tc.Id.Should().Be("toolu_01ABC");
        tc.Name.Should().Be("get_weather");
        tc.ArgumentsFragment.Should().Be("{\"location\":");
    }

    [Fact]
    public void InputJsonDelta_SubsequentFragment_OmitsIdAndName()
    {
        // Arrange
        var parser = new MessagesApiStreamingParser();
        parser.ParseStreamChunk(Parse(
            "{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_01ABC\",\"name\":\"get_weather\",\"input\":{}}}"));
        parser.ParseStreamChunk(Parse(
            "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"location\\\":\"}}"));

        // Act
        var deltaJson = Parse(
            "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"\\\"San Francisco\\\"}\"}}");
        var chunk = parser.ParseStreamChunk(deltaJson);

        // Assert
        chunk.Should().NotBeNull();
        chunk!.ToolCalls.Should().HaveCount(1);
        var tc = chunk.ToolCalls![0];
        tc.Index.Should().Be(0);
        tc.Id.Should().BeNull();
        tc.Name.Should().BeNull();
        tc.ArgumentsFragment.Should().Be("\"San Francisco\"}");
    }

    [Fact]
    public void TextDelta_EmitsContentChunk()
    {
        // Arrange
        var parser = new MessagesApiStreamingParser();
        var deltaJson = Parse(
            """{"type":"content_block_delta","index":0,"delta":{"type":"text","text":"Hello world"}}""");

        // Act
        var chunk = parser.ParseStreamChunk(deltaJson);

        // Assert
        chunk.Should().NotBeNull();
        chunk!.Content.Should().Be("Hello world");
        chunk.ToolCalls.Should().BeNull();
    }

    [Fact]
    public void TextAndToolCallDeltas_CoexistInSameStream()
    {
        // Arrange
        var parser = new MessagesApiStreamingParser();

        // Act — text block at index 0, tool block at index 1
        parser.ParseStreamChunk(Parse(
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}"""));
        var textChunk = parser.ParseStreamChunk(Parse(
            """{"type":"content_block_delta","index":0,"delta":{"type":"text","text":"Let me check."}}"""));

        parser.ParseStreamChunk(Parse(
            """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_02","name":"search","input":{}}}"""));
        var toolChunk = parser.ParseStreamChunk(Parse(
            "{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"q\\\":\\\"test\\\"}\"}}"));

        // Assert
        textChunk.Should().NotBeNull();
        textChunk!.Content.Should().Be("Let me check.");

        toolChunk.Should().NotBeNull();
        toolChunk!.ToolCalls.Should().HaveCount(1);
        toolChunk.ToolCalls![0].Id.Should().Be("toolu_02");
        toolChunk.ToolCalls[0].Name.Should().Be("search");
        toolChunk.ToolCalls[0].ArgumentsFragment.Should().Be("{\"q\":\"test\"}");
    }

    [Fact]
    public void MessageStop_ProducesIsCompleted()
    {
        // Arrange
        var parser = new MessagesApiStreamingParser();
        var stopJson = Parse("""{"type":"message_stop"}""");

        // Act
        var chunk = parser.ParseStreamChunk(stopJson);

        // Assert
        chunk.Should().NotBeNull();
        chunk!.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void ContentBlockStop_CleansUpBlock()
    {
        // Arrange
        var parser = new MessagesApiStreamingParser();
        parser.ParseStreamChunk(Parse(
            """{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_01","name":"fn","input":{}}}"""));

        // Act
        var stopJson = Parse("""{"type":"content_block_stop","index":0}""");
        var result = parser.ParseStreamChunk(stopJson);

        // Assert — content_block_stop returns null, block is cleaned up
        result.Should().BeNull();
    }

    [Fact]
    public void UnknownEventType_ReturnsNull()
    {
        // Arrange
        var parser = new MessagesApiStreamingParser();
        var json = Parse("""{"type":"message_start"}""");

        // Act
        var result = parser.ParseStreamChunk(json);

        // Assert
        result.Should().BeNull();
    }
}
