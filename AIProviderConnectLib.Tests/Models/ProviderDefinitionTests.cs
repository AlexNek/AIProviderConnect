using System.Text.Json;

using AIProviderConnect.Models;

using FluentAssertions;

namespace AIProviderConnect.Tests.Models;

public class ProviderDefinitionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public void SerializeAndDeserialize_RoundTrip_PreservesAllFields()
    {
        // Arrange
        var original = new ProviderDefinition
        {
            Id = "test-provider",
            DisplayName = "Test Provider",
            Protocol = EProviderProtocol.OpenAICompatible,
            BaseUrl = "https://api.test-provider.com/",
            ChatEndpoint = "v1/chat",
            ModelsEndpoint = "v1/models",
            Category = "TestCategory",
            HasModelDiscoveryApi = true
        };

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProviderDefinition>(json, JsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(original.Id);
        deserialized.DisplayName.Should().Be(original.DisplayName);
        deserialized.Protocol.Should().Be(original.Protocol);
        deserialized.BaseUrl.Should().Be(original.BaseUrl);
        deserialized.ChatEndpoint.Should().Be(original.ChatEndpoint);
        deserialized.ModelsEndpoint.Should().Be(original.ModelsEndpoint);
        deserialized.Category.Should().Be(original.Category);
        deserialized.HasModelDiscoveryApi.Should().Be(original.HasModelDiscoveryApi);
    }

    [Fact]
    public void Deserialize_CaseInsensitive_MatchesFields()
    {
        // Arrange
        var json = """
        {
            "id": "case-test",
            "displayname": "Case Test",
            "PROTOCOL": "OpenAICompatible",
            "baseurl": "https://api.case-test.com/",
            "chatendpoint": "v1/chat",
            "modelsendpoint": "v1/models",
            "category": "TestCategory"
        }
        """;

        // Act
        var provider = JsonSerializer.Deserialize<ProviderDefinition>(json, JsonOptions);

        // Assert
        provider.Should().NotBeNull();
        provider!.Id.Should().Be("case-test");
        provider.DisplayName.Should().Be("Case Test");
        provider.Protocol.Should().Be(EProviderProtocol.OpenAICompatible);
        provider.ChatEndpoint.Should().Be("v1/chat");
        provider.ModelsEndpoint.Should().Be("v1/models");
    }

    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        // Arrange & Act
        var provider = new ProviderDefinition
        {
            Id = "test",
            DisplayName = "Test",
            Protocol = EProviderProtocol.OpenAICompatible,
            BaseUrl = "https://api.test.com/"
        };

        // Assert
        provider.ChatEndpoint.Should().Be("chat/completions");
        provider.ModelsEndpoint.Should().Be("models");
        provider.Category.Should().BeNull();
        provider.HasModelDiscoveryApi.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_WithUnknownFields_IgnoresThem()
    {
        // Arrange
        var json = """
        {
            "id": "unknown-fields",
            "displayName": "Unknown Fields Test",
            "protocol": "OpenAICompatible",
            "baseUrl": "https://api.test.com/",
            "unknownField1": "value1",
            "unknownField2": 123,
            "unknownField3": true
        }
        """;

        // Act
        var provider = JsonSerializer.Deserialize<ProviderDefinition>(json, JsonOptions);

        // Assert
        provider.Should().NotBeNull();
        provider!.Id.Should().Be("unknown-fields");
        provider.BaseUrl.Should().Be("https://api.test.com/");
    }

    [Fact]
    public void Serialize_ProducesValidJson()
    {
        // Arrange
        var provider = new ProviderDefinition
        {
            Id = "serialize-test",
            DisplayName = "Serialize Test",
            Protocol = EProviderProtocol.OpenAICompatible,
            BaseUrl = "https://api.test.com/",
            ChatEndpoint = "v1/chat",
            ModelsEndpoint = "v1/models"
        };

        // Act
        var json = JsonSerializer.Serialize(provider, JsonOptions);

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        json.Should().Contain("\"chatEndpoint\"");
        json.Should().Contain("\"modelsEndpoint\"");
        json.Should().Contain("v1/chat");
        json.Should().Contain("v1/models");

        // Verify it's valid JSON by deserializing
        var deserialized = JsonSerializer.Deserialize<ProviderDefinition>(json, JsonOptions);
        deserialized.Should().NotBeNull();
        deserialized!.ChatEndpoint.Should().Be("v1/chat");
        deserialized.ModelsEndpoint.Should().Be("v1/models");
    }

    [Theory]
    [InlineData("OpenAICompatible", EProviderProtocol.OpenAICompatible)]
    [InlineData("AnthropicCompatible", EProviderProtocol.MessagesApi)]
    [InlineData("GeminiCompatible", EProviderProtocol.KeyQuery)]
    [InlineData("GitHubModelsCompatible", EProviderProtocol.Catalog)]
    [InlineData("HybridGateway", EProviderProtocol.HybridGateway)]
    [InlineData("Native", EProviderProtocol.Native)]
    public void Protocol_DeserializesAllValidProtocols(string jsonProtocol, EProviderProtocol expected)
    {
        // Arrange
        var json = $$"""
        {
            "id": "protocol-test",
            "displayName": "Protocol Test",
            "protocol": "{{jsonProtocol}}",
            "baseUrl": "https://api.test.com/"
        }
        """;

        // Act
        var provider = JsonSerializer.Deserialize<ProviderDefinition>(json, JsonOptions);

        // Assert
        provider.Should().NotBeNull();
        provider!.Protocol.Should().Be(expected);
    }

    [Fact]
    public void HasModelDiscoveryApi_DefaultsToFalse()
    {
        // Arrange & Act
        var provider = new ProviderDefinition
        {
            Id = "discovery-test",
            DisplayName = "Discovery Test",
            Protocol = EProviderProtocol.OpenAICompatible,
            BaseUrl = "https://api.test.com/"
        };

        // Assert
        provider.HasModelDiscoveryApi.Should().BeFalse();
    }

    [Fact]
    public void HasModelDiscoveryApi_CanBeSetToTrue()
    {
        // Arrange & Act
        var provider = new ProviderDefinition
        {
            Id = "discovery-true-test",
            DisplayName = "Discovery True Test",
            Protocol = EProviderProtocol.OpenAICompatible,
            BaseUrl = "https://api.test.com/",
            HasModelDiscoveryApi = true
        };

        // Assert
        provider.HasModelDiscoveryApi.Should().BeTrue();
    }

    [Fact]
    public void MessagesEndpoint_DefaultsToMessages()
    {
        var provider = new ProviderDefinition
        {
            Id = "test",
            DisplayName = "Test",
            Protocol = EProviderProtocol.MessagesApi,
            BaseUrl = "https://api.test.com/"
        };

        provider.MessagesEndpoint.Should().Be("messages");
    }

    [Fact]
    public void MessagesEndpoint_DeserializesFromJson()
    {
        var json = """
        {
            "id": "anthropic",
            "displayName": "Anthropic",
            "protocol": "AnthropicCompatible",
            "baseUrl": "https://api.anthropic.com/v1/",
            "messagesEndpoint": "messages",
            "modelsEndpoint": "models"
        }
        """;

        var provider = JsonSerializer.Deserialize<ProviderDefinition>(json, JsonOptions);

        provider.Should().NotBeNull();
        provider!.MessagesEndpoint.Should().Be("messages");
        provider.ModelsEndpoint.Should().Be("models");
    }

    [Fact]
    public void Protocol_RoundTrips_ThroughSerialization()
    {
        // Arrange — every protocol value must survive a serialize→deserialize cycle
        foreach (var protocol in Enum.GetValues<EProviderProtocol>())
        {
            var original = new ProviderDefinition
            {
                Id = "round-trip",
                DisplayName = "Round Trip",
                Protocol = protocol,
                BaseUrl = "https://api.test.com/"
            };

            // Act
            var json = JsonSerializer.Serialize(original, JsonOptions);
            var deserialized = JsonSerializer.Deserialize<ProviderDefinition>(json, JsonOptions);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized!.Protocol.Should().Be(protocol, "protocol {0} must survive round-trip", protocol);
        }
    }

    [Fact]
    public void Deserialize_UnknownProtocol_ThrowsJsonException()
    {
        var json = """
        {
            "id": "bad-protocol",
            "displayName": "Bad",
            "protocol": "totally-bogus",
            "baseUrl": "https://api.test.com/"
        }
        """;

        var act = () => JsonSerializer.Deserialize<ProviderDefinition>(json, JsonOptions);
        act.Should().Throw<System.Text.Json.JsonException>();
    }
}
