using System.Text.Json;

using AIProviderConnect.Constants;
using AIProviderConnect.Models;
using AIProviderConnect.Protocols;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Protocols;

/// <summary>
/// Hermetic tests for the shared wire-protocol parsing helpers introduced by Refactor 25.
/// </summary>
public class ProtocolParsingHelpersTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static AIModel MapModel(JsonElement element, string providerId) => new()
    {
        Id = ProtocolParsingHelpers.SafeGetString(element, "id"),
        DisplayName = ProtocolParsingHelpers.SafeGetString(element, "display_name"),
        ProviderId = providerId
    };

    [Theory]
    [InlineData("""{"name":"gpt-test"}""", "name", "gpt-test")]
    [InlineData("""{"other":"value"}""", "name", "")]
    [InlineData("""{"name":null}""", "name", "")]
    public void SafeGetString_ReturnsValueOrEmpty(string json, string propertyName, string expected)
    {
        // Arrange
        var element = Parse(json);

        // Act
        var result = ProtocolParsingHelpers.SafeGetString(element, propertyName);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void ApiKeyHeaderNameKey_IsTheProtocolConfigurationKey()
    {
        // Act / Assert
        ProtocolParsingHelpers.ApiKeyHeaderNameKey.Should().Be("apiKeyHeaderName");
    }

    [Fact]
    public void TryExtractApiKeyHeaderName_ReturnsTrueAndValue_WhenKeyIsPresent()
    {
        // Arrange
        var configuration = new Dictionary<string, string> { ["apiKeyHeaderName"] = "x-custom-key" };

        // Act
        var result = ProtocolParsingHelpers.TryExtractApiKeyHeaderName(configuration, out var headerName);

        // Assert
        result.Should().BeTrue();
        headerName.Should().Be("x-custom-key");
    }

    [Fact]
    public void TryExtractApiKeyHeaderName_ReturnsFalse_WhenConfigurationIsNull()
    {
        // Act
        var result = ProtocolParsingHelpers.TryExtractApiKeyHeaderName(null, out var headerName);

        // Assert
        result.Should().BeFalse();
        headerName.Should().BeEmpty();
    }

    [Fact]
    public void TryExtractApiKeyHeaderName_ReturnsFalse_WhenKeyIsAbsent()
    {
        // Arrange
        var configuration = new Dictionary<string, string> { ["apiVersion"] = "2026-03-10" };

        // Act
        var result = ProtocolParsingHelpers.TryExtractApiKeyHeaderName(configuration, out var headerName);

        // Assert
        result.Should().BeFalse();
        headerName.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryExtractApiKeyHeaderName_ReturnsFalse_WhenValueIsBlank(string raw)
    {
        // Arrange
        var configuration = new Dictionary<string, string> { ["apiKeyHeaderName"] = raw };

        // Act
        var result = ProtocolParsingHelpers.TryExtractApiKeyHeaderName(configuration, out var headerName);

        // Assert
        result.Should().BeFalse();
        headerName.Should().BeEmpty();
    }

    [Fact]
    public void MapContentParts_DispatchesTextAndBothImageAliases()
    {
        // Arrange
        var image = ImageContent.FromUrl("https://test.example.com/image.png");
        var parts = new List<ContentPart>
        {
            new() { Type = ContentPartTypes.Text, Text = "hello" },
            new() { Type = ContentPartTypes.ImageUrl, Image = image },
            new() { Type = ContentPartTypes.Image, Image = image }
        };

        // Act
        var result = ProtocolParsingHelpers.MapContentParts(
            parts,
            imageContent => $"image:{imageContent?.Url}",
            text => $"text:{text}");

        // Assert
        result.Should().Equal(
            "text:hello",
            "image:https://test.example.com/image.png",
            "image:https://test.example.com/image.png");
    }

    [Fact]
    public void MapContentParts_MapsNullTextToEmptyString()
    {
        // Arrange
        var parts = new List<ContentPart>
        {
            new() { Type = ContentPartTypes.Text, Text = null },
            new() { Type = "audio" }
        };

        // Act
        var result = ProtocolParsingHelpers.MapContentParts(
            parts,
            _ => "image",
            text => $"text:{text}");

        // Assert
        result.Should().Equal("text:", "text:");
    }

    [Fact]
    public void MapContentParts_PassesNullImage_ForImagePartWithoutImage()
    {
        // Arrange
        var parts = new List<ContentPart> { new() { Type = ContentPartTypes.ImageUrl } };

        // Act
        var result = ProtocolParsingHelpers.MapContentParts(
            parts,
            imageContent => imageContent is null ? "image:<none>" : "image:<set>",
            _ => "text");

        // Assert
        result.Should().Equal("image:<none>");
    }

    [Fact]
    public void MapContentParts_ReturnsEmptyArray_ForEmptyPartList()
    {
        // Act
        var result = ProtocolParsingHelpers.MapContentParts(
            new List<ContentPart>(),
            _ => "image",
            _ => "text");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseModelArray_AppliesMapperToEveryElementOfNestedArray()
    {
        // Arrange
        var json = Parse("""{"data":[{"id":"a"},{"id":"b"}]}""");

        // Act
        var result = ProtocolParsingHelpers.ParseModelArray(json, "data", "test-provider", x => MapModel(x, "test-provider"));

        // Assert
        result.Select(m => m.Id).Should().Equal("a", "b");
        result.Should().OnlyContain(m => m.ProviderId == "test-provider");
    }

    [Fact]
    public void ParseModelArray_ReadsRootElement_WhenRootPropertyIsNull()
    {
        // Arrange
        var json = Parse("""[{"id":"a"}]""");

        // Act
        var result = ProtocolParsingHelpers.ParseModelArray(json, null, "test-provider", x => MapModel(x, "test-provider"));

        // Assert
        result.Select(m => m.Id).Should().Equal("a");
    }

    [Fact]
    public void ParseModelArray_ReturnsEmpty_WhenRootPropertyIsAbsent()
    {
        // Arrange
        var json = Parse("""{"models":[{"id":"a"}]}""");

        // Act
        var result = ProtocolParsingHelpers.ParseModelArray(json, "data", "test-provider", x => MapModel(x, "test-provider"));

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseModelArray_ReturnsEmpty_WhenRootPropertyIsNotAnArray()
    {
        // Arrange
        var json = Parse("""{"data":"unexpected"}""");

        // Act
        var result = ProtocolParsingHelpers.ParseModelArray(json, "data", "test-provider", x => MapModel(x, "test-provider"));

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseModelArray_ReturnsEmpty_WhenRootElementIsNotAnArray()
    {
        // Arrange
        var json = Parse("""{"id":"a"}""");

        // Act
        var result = ProtocolParsingHelpers.ParseModelArray(json, null, "test-provider", x => MapModel(x, "test-provider"));

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"data":[{"id":""}]}""")]
    [InlineData("""{"data":[{"id":"   "}]}""")]
    [InlineData("""{"data":[{"id":null}]}""")]
    [InlineData("""{"data":[{}]}""")]
    public void ParseModelArray_FiltersElementsWithBlankOrMissingId(string json)
    {
        // Arrange
        var root = Parse(json);

        // Act
        var result = ProtocolParsingHelpers.ParseModelArray(root, "data", "test-provider", x => MapModel(x, "test-provider"));

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseModelArray_KeepsMapperProducedOrder_DroppingOnlyBlankIds()
    {
        // Arrange
        var json = Parse("""{"data":[{"id":"a"},{"id":""},{"id":"c"}]}""");

        // Act
        var result = ProtocolParsingHelpers.ParseModelArray(json, "data", "test-provider", x => MapModel(x, "test-provider"));

        // Assert
        result.Select(m => m.Id).Should().Equal("a", "c");
    }
}
