using System.Text.Json;

using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Protocols;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Protocols;

/// <summary>
/// Hermetic tests for the embeddings wire-protocol methods
/// (<see cref="OpenAICompatibleWireProtocol.MapEmbeddingsRequest"/> and
/// <see cref="OpenAICompatibleWireProtocol.ParseEmbeddingsResponse"/>).
/// </summary>
public class OpenAICompatibleWireProtocolEmbeddingsTests
{
    // --- MapEmbeddingsRequest ---

    [Fact]
    public void MapEmbeddingsRequest_ProducesModelAndInputKeys()
    {
        var request = new EmbeddingRequest { Model = "text-embedding-3-small", Input = ["hello", "world"] };

        var mapped = OpenAICompatibleWireProtocol.MapEmbeddingsRequest(request);

        mapped.Should().ContainKey("model").WhoseValue.Should().Be("text-embedding-3-small");
        mapped.Should().ContainKey("input");
    }

    // --- ParseEmbeddingsResponse: happy path ---

    [Fact]
    public void ParseEmbeddingsResponse_HappyPath_ReturnsDataSortedByIndex()
    {
        // Arrange — provider returns vectors out of order
        var json = JsonDocument.Parse("""
            {
              "model": "text-embedding-3-small",
              "data": [
                { "index": 1, "embedding": [0.2, 0.3] },
                { "index": 0, "embedding": [0.1, 0.4] }
              ],
              "usage": { "prompt_tokens": 5 }
            }
            """).RootElement;

        // Act
        var response = OpenAICompatibleWireProtocol.ParseEmbeddingsResponse(json);

        // Assert — re-sorted by index
        response.Data.Should().HaveCount(2);
        response.Data[0].Index.Should().Be(0);
        response.Data[0].Embedding.Should().Equal(0.1f, 0.4f);
        response.Data[1].Index.Should().Be(1);
        response.Data[1].Embedding.Should().Equal(0.2f, 0.3f);
        response.Model.Should().Be("text-embedding-3-small");
        response.Usage.PromptTokens.Should().Be(5);
    }

    [Fact]
    public void ParseEmbeddingsResponse_MissingModelAndUsage_DefaultsToEmptyAndZero()
    {
        // Arrange — minimal valid response: no model, no usage
        var json = JsonDocument.Parse("""
            { "data": [{ "index": 0, "embedding": [0.5] }] }
            """).RootElement;

        // Act
        var response = OpenAICompatibleWireProtocol.ParseEmbeddingsResponse(json);

        // Assert
        response.Data.Should().HaveCount(1);
        response.Model.Should().BeEmpty();
        response.Usage.PromptTokens.Should().Be(0);
    }

    // --- ParseEmbeddingsResponse: missing data[] ---

    [Fact]
    public void ParseEmbeddingsResponse_MissingDataArray_ThrowsEmbeddingFailed()
    {
        var json = JsonDocument.Parse("""
            { "model": "text-embedding-3-small" }
            """).RootElement;

        var act = () => OpenAICompatibleWireProtocol.ParseEmbeddingsResponse(json);

        var ex = act.Should().Throw<AiException>().Which;
        ex.Code.Should().Be(AiErrorCodes.EmbeddingFailed);
    }

    [Fact]
    public void ParseEmbeddingsResponse_DataNotArray_ThrowsEmbeddingFailed()
    {
        var json = JsonDocument.Parse("""
            { "data": {} }
            """).RootElement;

        var act = () => OpenAICompatibleWireProtocol.ParseEmbeddingsResponse(json);

        var ex = act.Should().Throw<AiException>().Which;
        ex.Code.Should().Be(AiErrorCodes.EmbeddingFailed);
    }

    // --- ParseEmbeddingsResponse: invalid entries ---

    [Fact]
    public void ParseEmbeddingsResponse_MissingIndex_ThrowsEmbeddingFailed()
    {
        var json = JsonDocument.Parse("""
            { "data": [{ "embedding": [0.1] }] }
            """).RootElement;

        var act = () => OpenAICompatibleWireProtocol.ParseEmbeddingsResponse(json);

        var ex = act.Should().Throw<AiException>().Which;
        ex.Code.Should().Be(AiErrorCodes.EmbeddingFailed);
    }

    [Fact]
    public void ParseEmbeddingsResponse_NegativeIndex_ThrowsEmbeddingFailed()
    {
        var json = JsonDocument.Parse("""
            { "data": [{ "index": -1, "embedding": [0.1] }] }
            """).RootElement;

        var act = () => OpenAICompatibleWireProtocol.ParseEmbeddingsResponse(json);

        var ex = act.Should().Throw<AiException>().Which;
        ex.Code.Should().Be(AiErrorCodes.EmbeddingFailed);
    }

    [Fact]
    public void ParseEmbeddingsResponse_DuplicateIndex_ThrowsEmbeddingFailed()
    {
        var json = JsonDocument.Parse("""
            {
              "data": [
                { "index": 0, "embedding": [0.1] },
                { "index": 0, "embedding": [0.2] }
              ]
            }
            """).RootElement;

        var act = () => OpenAICompatibleWireProtocol.ParseEmbeddingsResponse(json);

        var ex = act.Should().Throw<AiException>().Which;
        ex.Code.Should().Be(AiErrorCodes.EmbeddingFailed);
    }

    [Fact]
    public void ParseEmbeddingsResponse_EmptyEmbeddingArray_ThrowsEmbeddingFailed()
    {
        var json = JsonDocument.Parse("""
            { "data": [{ "index": 0, "embedding": [] }] }
            """).RootElement;

        var act = () => OpenAICompatibleWireProtocol.ParseEmbeddingsResponse(json);

        var ex = act.Should().Throw<AiException>().Which;
        ex.Code.Should().Be(AiErrorCodes.EmbeddingFailed);
    }

    [Fact]
    public void ParseEmbeddingsResponse_MissingEmbeddingField_ThrowsEmbeddingFailed()
    {
        var json = JsonDocument.Parse("""
            { "data": [{ "index": 0 }] }
            """).RootElement;

        var act = () => OpenAICompatibleWireProtocol.ParseEmbeddingsResponse(json);

        var ex = act.Should().Throw<AiException>().Which;
        ex.Code.Should().Be(AiErrorCodes.EmbeddingFailed);
    }
}
