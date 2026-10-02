using System.Text.Json;

using AIProviderConnect.Models;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Models;

public class EProviderProtocolNullableJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new EProviderProtocolNullableJsonConverter() }
    };

    [Theory]
    [InlineData("openaicompatible", EProviderProtocol.OpenAICompatible)]
    [InlineData("OpenAICompatible", EProviderProtocol.OpenAICompatible)]
    [InlineData("anthropiccompatible", EProviderProtocol.MessagesApi)]
    [InlineData("geminicompatible", EProviderProtocol.KeyQuery)]
    [InlineData("githubmodelscompatible", EProviderProtocol.Catalog)]
    [InlineData("hybridgateway", EProviderProtocol.HybridGateway)]
    [InlineData("decision", EProviderProtocol.Decision)]
    [InlineData("native", EProviderProtocol.Native)]
    public void Read_AliasVocabularyCaseInsensitive_MapsToEnum(string token, EProviderProtocol expected)
    {
        // Arrange
        var json = $$"""{ "protocol": "{{token}}" }""";

        // Act
        var result = JsonSerializer.Deserialize<NullableProtocolHolder>(json, Options);

        // Assert
        result!.Protocol.Should().Be(expected);
    }

    [Theory]
    [InlineData("""{ "protocol": null }""")]
    [InlineData("{}")]
    public void Read_NullOrAbsent_MapsToNull(string json)
    {
        // Act
        var result = JsonSerializer.Deserialize<NullableProtocolHolder>(json, Options);

        // Assert
        result!.Protocol.Should().BeNull();
    }

    [Fact]
    public void Write_Null_WritesNullToken()
    {
        // Arrange
        var holder = new NullableProtocolHolder();

        // Act
        var json = JsonSerializer.Serialize(holder, Options);

        // Assert
        json.Should().Be("""{"protocol":null}""");
    }

    [Fact]
    public void Read_UnknownToken_ThrowsJsonException()
    {
        // Arrange
        var json = """{ "protocol": "MessagesApi" }""";

        // Act
        var act = () => JsonSerializer.Deserialize<NullableProtocolHolder>(json, Options);

        // Assert
        act.Should().Throw<JsonException>();
    }

    /// <summary>
    /// Minimal DTO for deserialization tests.
    /// </summary>
    private sealed class NullableProtocolHolder
    {
        [System.Text.Json.Serialization.JsonPropertyName("protocol")]
        public EProviderProtocol? Protocol { get; set; }
    }
}
