using System.Text.Json;

using AIProviderConnect.Models;
using AIProviderConnect.Protocols;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Protocols;

/// <summary>
/// The OpenAI-compatible logprob mapping: the request fields appear only when set, so an ordinary
/// chat body is unchanged, and the response block is parsed onto the flattened response record that
/// <c>ChatAsync</c> returns.
/// </summary>
public class OpenAICompatibleLogprobsTests
{
    private static ChatCompletionRequest Request(bool? logprobs = null, int? topLogprobs = null) => new()
    {
        Model = "test-model",
        Messages = [new ChatMessage { Role = EChatRole.User, Content = "hi" }],
        Logprobs = logprobs,
        TopLogprobs = topLogprobs
    };

    [Fact]
    public void MapRequest_LogprobsSet_EmitsBothFields()
    {
        // Act
        var payload = OpenAICompatibleWireProtocol.MapRequest(Request(logprobs: true, topLogprobs: 5), stream: false);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        // Assert
        doc.RootElement.GetProperty("logprobs").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("top_logprobs").GetInt32().Should().Be(5);
    }

    [Fact]
    public void MapRequest_LogprobsUnset_OmitsBothFields()
    {
        // Act
        var payload = OpenAICompatibleWireProtocol.MapRequest(Request(), stream: false);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        // Assert
        doc.RootElement.TryGetProperty("logprobs", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("top_logprobs", out _).Should().BeFalse();
    }

    [Fact]
    public void MapRequest_TopLogprobsWithoutLogprobs_EmitsOnlyThatField()
    {
        // Act
        var payload = OpenAICompatibleWireProtocol.MapRequest(Request(topLogprobs: 3), stream: false);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        // Assert
        doc.RootElement.TryGetProperty("logprobs", out _).Should().BeFalse();
        doc.RootElement.GetProperty("top_logprobs").GetInt32().Should().Be(3);
    }

    [Fact]
    public void ParseResponse_LogprobsBlock_IsParsed()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""
            {
              "id": "c1",
              "model": "m1",
              "choices": [
                {
                  "message": { "role": "assistant", "content": "A" },
                  "logprobs": {
                    "content": [
                      {
                        "token": "A",
                        "logprob": -0.5,
                        "top_logprobs": [
                          { "token": "A", "logprob": -0.5 },
                          { "token": "B", "logprob": -1.5 }
                        ]
                      }
                    ]
                  }
                }
              ]
            }
            """);

        // Act
        var response = OpenAICompatibleWireProtocol.ParseResponse(doc.RootElement);

        // Assert
        response.Logprobs.Should().NotBeNull();
        response.Logprobs!.Content.Should().HaveCount(1);
        response.Logprobs.Content[0].Token.Should().Be("A");
        response.Logprobs.Content[0].Logprob.Should().Be(-0.5);
        response.Logprobs.Content[0].TopLogprobs.Should().HaveCount(2);
        response.Logprobs.Content[0].TopLogprobs[1].Token.Should().Be("B");
        response.Logprobs.Content[0].TopLogprobs[1].Logprob.Should().Be(-1.5);
    }

    [Fact]
    public void ParseResponse_AbsentLogprobs_LeavesNull()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""
            { "choices": [ { "message": { "role": "assistant", "content": "A" } } ] }
            """);

        // Act
        var response = OpenAICompatibleWireProtocol.ParseResponse(doc.RootElement);

        // Assert
        response.Logprobs.Should().BeNull();
    }

    [Fact]
    public void ParseResponse_NullLogprobs_LeavesNull()
    {
        // Arrange
        using var doc = JsonDocument.Parse("""
            { "choices": [ { "logprobs": null, "message": { "content": "A" } } ] }
            """);

        // Act
        var response = OpenAICompatibleWireProtocol.ParseResponse(doc.RootElement);

        // Assert
        response.Logprobs.Should().BeNull();
    }

    [Fact]
    public void ParseResponse_MalformedEntries_AreSkipped()
    {
        // Arrange — one entry without a token, one without a numeric logprob, one valid
        using var doc = JsonDocument.Parse("""
            {
              "choices": [
                {
                  "logprobs": {
                    "content": [
                      { "logprob": -0.5 },
                      { "token": "B", "logprob": "not-a-number" },
                      { "token": "C", "logprob": -2.0 }
                    ]
                  },
                  "message": { "content": "C" }
                }
              ]
            }
            """);

        // Act
        var response = OpenAICompatibleWireProtocol.ParseResponse(doc.RootElement);

        // Assert
        response.Logprobs!.Content.Should().HaveCount(1);
        response.Logprobs.Content[0].Token.Should().Be("C");
        response.Logprobs.Content[0].TopLogprobs.Should().BeEmpty();
    }
}
