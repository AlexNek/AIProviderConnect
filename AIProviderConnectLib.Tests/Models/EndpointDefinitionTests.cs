using System.Text.Json;

using AIProviderConnect.Models;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Models;

public class EndpointDefinitionTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Deserialize_MapsJsonPropertyNames()
    {
        // Arrange
        var json = """{ "path": "alpha/decisions", "baseUrl": "https://test.example.com/api/", "protocol": "decision" }""";

        // Act
        var entry = JsonSerializer.Deserialize<EndpointDefinition>(json, Options);

        // Assert
        entry.Should().NotBeNull();
        entry!.Path.Should().Be("alpha/decisions");
        entry.BaseUrl.Should().Be("https://test.example.com/api/");
        entry.Protocol.Should().Be(EProviderProtocol.Decision);
    }

    [Fact]
    public void Deserialize_FieldsIndependentlyAbsent_AllNull()
    {
        // Arrange
        var json = "{}";

        // Act
        var entry = JsonSerializer.Deserialize<EndpointDefinition>(json, Options);

        // Assert
        entry.Should().NotBeNull();
        entry!.Path.Should().BeNull();
        entry.BaseUrl.Should().BeNull();
        entry.Protocol.Should().BeNull();
    }

    [Fact]
    public void Serialize_UnsetMembers_Omitted()
    {
        // Arrange
        var entry = new EndpointDefinition { Path = "models" };

        // Act
        var json = JsonSerializer.Serialize(entry, Options);

        // Assert
        json.Should().Be("""{"path":"models"}""");
    }

    [Fact]
    public void Serialize_AllMembersSet_WritesAll()
    {
        // Arrange
        var entry = new EndpointDefinition
        {
            Path = "alpha/decisions",
            BaseUrl = "https://test.example.com/api/",
            Protocol = EProviderProtocol.Decision
        };

        // Act
        var json = JsonSerializer.Serialize(entry, Options);

        // Assert
        json.Should().Be("""{"baseUrl":"https://test.example.com/api/","path":"alpha/decisions","protocol":"decision"}""");
    }

    [Fact]
    public void Deserialize_UnknownProtocolToken_ThrowsJsonException()
    {
        // Arrange
        var json = """{ "protocol": "not-a-protocol" }""";

        // Act
        var act = () => JsonSerializer.Deserialize<EndpointDefinition>(json, Options);

        // Assert
        act.Should().Throw<JsonException>();
    }
}
