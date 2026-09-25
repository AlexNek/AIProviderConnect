using System.Text.Json;

using AIProviderConnect.Models;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Models;

public class EProviderProtocolJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new EProviderProtocolJsonConverter() }
    };

    [Fact]
    public void Read_NullProtocolValue_ThrowsJsonException()
    {
        // Arrange — JSON null for the protocol property
        var json = """{ "protocol": null }""";

        // Act
        var act = () => JsonSerializer.Deserialize<ProtocolHolder>(json, Options);

        // Assert — must throw JsonException, not silently default to OpenAICompatible
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_ValidProtocolValue_DeserializesCorrectly()
    {
        // Arrange
        var json = """{ "protocol": "anthropiccompatible" }""";

        // Act
        var result = JsonSerializer.Deserialize<ProtocolHolder>(json, Options);

        // Assert
        result!.Protocol.Should().Be(EProviderProtocol.MessagesApi);
    }

    /// <summary>
    /// Minimal DTO for deserialization tests.
    /// </summary>
    private sealed class ProtocolHolder
    {
        public EProviderProtocol Protocol { get; set; }
    }
}
