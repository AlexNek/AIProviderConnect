using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;

using FluentAssertions;

using Xunit;

namespace AIProviderConnect.Tests.DependencyInjection;

/// <summary>
/// One case per rejected shape of rule 13, plus a valid block passing, plus the
/// decisions-override class-selection lookup.
/// </summary>
public class EndpointConfigurationTests
{
    private const string ProviderId = "endpoint-config-test";

    private static ProviderDefinition Definition(
        string protocol = "OpenAICompatible",
        Dictionary<string, EndpointDefinition>? endpoints = null) =>
        new()
        {
            Id = ProviderId,
            DisplayName = "Endpoint Config Test",
            BaseUrl = "https://test.example.com/api/v1/",
            Protocol = ProviderProtocolMapper.FromJson(protocol),
            Endpoints = endpoints
        };

    private static Dictionary<string, EndpointDefinition> Endpoints(
        string key,
        EndpointDefinition entry) =>
        new(StringComparer.OrdinalIgnoreCase) { [key] = entry };

    [Fact]
    public void Validate_ValidBlock_Passes()
    {
        // Arrange
        var definition = Definition(endpoints: Endpoints(
            "decisions", new EndpointDefinition
            {
                Path = "alpha/decisions",
                BaseUrl = "https://test.example.com/api/",
                Protocol = EProviderProtocol.Decision
            }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_NoEndpointsBlock_Passes()
    {
        // Arrange
        var definition = Definition();

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_UnknownOperationKey_ThrowsConfigurationError()
    {
        // Arrange
        var definition = Definition(endpoints: Endpoints(
            "translations", new EndpointDefinition { Path = "translations" }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().Throw<AiException>()
            .Which.Code.Should().Be(AiErrorCodes.ConfigurationError);
        act.Should().Throw<AiException>()
            .Which.Message.Should().Contain(ProviderId).And.Contain("translations");
    }

    [Fact]
    public void Validate_EntryChangingNothing_ThrowsConfigurationError()
    {
        // Arrange
        var definition = Definition(endpoints: Endpoints(
            "chat", new EndpointDefinition()));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().Throw<AiException>()
            .Which.Message.Should().Contain("chat");
    }

    [Fact]
    public void Validate_EmptyPath_ThrowsConfigurationError()
    {
        // Arrange
        var definition = Definition(endpoints: Endpoints(
            "chat", new EndpointDefinition { Path = "  " }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().Throw<AiException>()
            .Which.Message.Should().Contain("chat");
    }

    [Fact]
    public void Validate_SchemedPath_ThrowsConfigurationError()
    {
        // Arrange — a surface on another root uses the baseUrl override, not an absolute path
        var definition = Definition(endpoints: Endpoints(
            "decisions", new EndpointDefinition
            {
                Path = "https://test.example.com/api/alpha/decisions",
                Protocol = EProviderProtocol.Decision
            }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().Throw<AiException>()
            .Which.Message.Should().Contain("relative path");
    }

    [Fact]
    public void Validate_NonHttpsBaseUrl_ThrowsConfigurationError()
    {
        // Arrange
        var definition = Definition(endpoints: Endpoints(
            "decisions", new EndpointDefinition
            {
                Path = "alpha/decisions",
                BaseUrl = "http://test.example.com/api/"
            }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().Throw<AiException>()
            .Which.Message.Should().Contain("baseUrl");
    }

    [Fact]
    public void Validate_RelativeBaseUrl_ThrowsConfigurationError()
    {
        // Arrange
        var definition = Definition(endpoints: Endpoints(
            "decisions", new EndpointDefinition
            {
                Path = "alpha/decisions",
                BaseUrl = "api/"
            }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().Throw<AiException>()
            .Which.Code.Should().Be(AiErrorCodes.ConfigurationError);
    }

    [Fact]
    public void Validate_KeyQueryChatWithoutModelPlaceholder_ThrowsConfigurationError()
    {
        // Arrange — KeyQuery entry protocol on the chat operation
        var definition = Definition(endpoints: Endpoints(
            "chat", new EndpointDefinition
            {
                Path = "chat",
                Protocol = EProviderProtocol.KeyQuery
            }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().Throw<AiException>()
            .Which.Message.Should().Contain("{model}");
    }

    [Fact]
    public void Validate_KeyQueryPrimaryChatWithoutModelPlaceholder_ThrowsConfigurationError()
    {
        // Arrange — entry protocol null, inherited from the KeyQuery primary
        var definition = Definition(
            protocol: "geminicompatible",
            endpoints: Endpoints("chat", new EndpointDefinition { Path = "chat" }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().Throw<AiException>()
            .Which.Message.Should().Contain("{model}");
    }

    [Fact]
    public void Validate_DecisionOverrideOnMessagesApiPrimary_ThrowsConfigurationError()
    {
        // Arrange
        var definition = Definition(
            protocol: "anthropiccompatible",
            endpoints: Endpoints(
                "decisions", new EndpointDefinition
                {
                    Path = "decisions",
                    Protocol = EProviderProtocol.Decision
                }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().Throw<AiException>()
            .Which.Code.Should().Be(AiErrorCodes.ConfigurationError);
    }

    [Fact]
    public void Validate_DecisionOverrideOnHybridGatewayPrimary_Passes()
    {
        // Arrange
        var definition = Definition(
            protocol: "hybridgateway",
            endpoints: Endpoints(
                "decisions", new EndpointDefinition
                {
                    Path = "alpha/decisions",
                    Protocol = EProviderProtocol.Decision
                }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void TryGetDecisionsOverride_DecisionProtocol_ReturnsTrueWithEntry()
    {
        // Arrange
        var entry = new EndpointDefinition
        {
            Path = "alpha/decisions",
            Protocol = EProviderProtocol.Decision
        };
        var definition = Definition(endpoints: Endpoints("decisions", entry));

        // Act
        var result = EndpointConfiguration.TryGetDecisionsOverride(definition, out var resolved);

        // Assert
        result.Should().BeTrue();
        resolved.Should().BeSameAs(entry);
    }

    [Fact]
    public void TryGetDecisionsOverride_WithoutProtocolOverride_ReturnsFalse()
    {
        // Arrange — a decisions entry without the protocol override leaves selection untouched
        var definition = Definition(endpoints: Endpoints(
            "decisions", new EndpointDefinition { Path = "custom/decisions" }));

        // Act
        var result = EndpointConfiguration.TryGetDecisionsOverride(definition, out var resolved);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TryGetDecisionsOverride_NoBlock_ReturnsFalse()
    {
        // Arrange
        var definition = Definition();

        // Act
        var result = EndpointConfiguration.TryGetDecisionsOverride(definition, out var resolved);

        // Assert
        result.Should().BeFalse();
        resolved.Should().BeNull();
    }

    [Fact]
    public void Validate_OperationKeyCaseInsensitive_IsAccepted()
    {
        // Arrange
        var definition = Definition(endpoints: Endpoints(
            "Decisions", new EndpointDefinition
            {
                Path = "alpha/decisions",
                Protocol = EProviderProtocol.Decision
            }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ValidAdditionalQueryParameter_Passes()
    {
        // Arrange
        var definition = Definition(endpoints: Endpoints(
            "models", new EndpointDefinition
            {
                Path = "models",
                AdditionalQueryParameter = "output_modalities=all"
            }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_AdditionalQueryParameterOnly_Passes()
    {
        // Arrange — an entry with only additionalQueryParameter is valid: "keep the default
        // path, add a widening query parameter"
        var definition = Definition(endpoints: Endpoints(
            "models", new EndpointDefinition
            {
                AdditionalQueryParameter = "output_modalities=all"
            }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_AdditionalQueryParameterStartingWithQuestionMark_ThrowsConfigurationError()
    {
        // Arrange
        var definition = Definition(endpoints: Endpoints(
            "models", new EndpointDefinition
            {
                Path = "models",
                AdditionalQueryParameter = "?output_modalities=all"
            }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().Throw<AiException>()
            .Which.Code.Should().Be(AiErrorCodes.ConfigurationError);
        act.Should().Throw<AiException>()
            .Which.Message.Should().Contain("additionalQueryParameter");
    }

    [Fact]
    public void Validate_AdditionalQueryParameterContainingHash_ThrowsConfigurationError()
    {
        // Arrange
        var definition = Definition(endpoints: Endpoints(
            "models", new EndpointDefinition
            {
                Path = "models",
                AdditionalQueryParameter = "output_modalities=all#fragment"
            }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().Throw<AiException>()
            .Which.Message.Should().Contain("additionalQueryParameter");
    }

    [Fact]
    public void Validate_AdditionalQueryParameterContainingSpace_ThrowsConfigurationError()
    {
        // Arrange
        var definition = Definition(endpoints: Endpoints(
            "models", new EndpointDefinition
            {
                Path = "models",
                AdditionalQueryParameter = "output_modalities=all value"
            }));

        // Act
        var act = () => EndpointConfiguration.Validate(ProviderId, definition);

        // Assert
        act.Should().Throw<AiException>()
            .Which.Message.Should().Contain("additionalQueryParameter");
    }
}
