using AIProviderConnect.Models;

using FluentAssertions;

using ScraperTool.Models;

namespace ScraperTool.Tests;

public class ModelSelectionItemTests
{
    private static AIModel MakeModel(string? modality = null, EModelCapability? capabilities = null) => new()
    {
        Id = "test-model",
        DisplayName = "Test Model",
        Modality = modality,
        Capabilities = capabilities
    };

    [Fact]
    public void FromAIModel_MapsNullCapabilities_ToNotReported()
    {
        // Arrange
        var model = MakeModel(capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.Capabilities.Should().BeNull();
        item.CapabilitiesText.Should().Be("not reported");
    }

    [Fact]
    public void FromAIModel_MapsReportedNone_ToNoneString()
    {
        // Arrange
        var model = MakeModel(capabilities: EModelCapability.None);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.Capabilities.Should().Be(EModelCapability.None);
        item.CapabilitiesText.Should().Be("None");
    }

    [Fact]
    public void FromAIModel_MapsReportedFlags_ToFlagNames()
    {
        // Arrange
        var model = MakeModel(capabilities: EModelCapability.ToolCalling | EModelCapability.Embedding);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.Capabilities.Should().Be(EModelCapability.ToolCalling | EModelCapability.Embedding);
        item.CapabilitiesText.Should().Be("ToolCalling, Embedding");
    }

    [Theory]
    [InlineData("text->text", "text->text")] // pure text — no icons, falls back to raw string
    [InlineData("text+image->text", "🖼")]
    [InlineData("text->image", "🖼")]
    public void ParseModalities_DropsTextToken(string modality, string expectedIcons)
    {
        // Arrange
        var model = MakeModel(modality: modality);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.Modalities.Should().Be(expectedIcons);
    }

    [Theory]
    [InlineData("text->embeddings", "🔢")]
    [InlineData("text->embedding", "🔢")]
    [InlineData("text->decisions", "🎯")]
    [InlineData("text->decision", "🎯")]
    [InlineData("text->rerank", "📊")]
    [InlineData("text->speech", "🔊")]
    [InlineData("text->transcription", "🔊")]
    public void ParseModalities_RecognisesNewTokens(string modality, string expectedIcons)
    {
        // Arrange
        var model = MakeModel(modality: modality);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.Modalities.Should().Be(expectedIcons);
    }

    [Fact]
    public void ParseModalities_CombinesMultipleNewTokens()
    {
        // Arrange
        var model = MakeModel(modality: "text->embeddings+decisions");

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.Modalities.Should().Be("🔢 🎯");
    }

    [Fact]
    public void ParseModalities_FallsBackToRawString_WhenNoRecognisedTokens()
    {
        // Arrange
        var model = MakeModel(modality: "text->unknown");

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.Modalities.Should().Be("text->unknown");
    }

    [Fact]
    public void ParseModalities_PreservesExistingTokens()
    {
        // Arrange
        var model = MakeModel(modality: "text+image+audio->text+video");

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.Modalities.Should().Be("🖼 🔊 🎬");
    }
}
