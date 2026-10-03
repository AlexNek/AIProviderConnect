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

    [Fact]
    public void ParseModalities_SplitsTextInAndOut()
    {
        // Arrange
        var model = MakeModel(modality: "text->text");

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.ModalitiesIn.Should().Be("\uD83D\uDCDD");
        item.ModalitiesOut.Should().Be("\uD83D\uDCDD");
    }

    [Fact]
    public void ParseModalities_SplitsImageInTextOut()
    {
        // Arrange
        var model = MakeModel(modality: "text+image->text");

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.ModalitiesIn.Should().Be("\uD83D\uDCDD \uD83D\uDDBC");
        item.ModalitiesOut.Should().Be("\uD83D\uDCDD");
    }

    [Fact]
    public void ParseModalities_SplitsTextInImageOut()
    {
        // Arrange
        var model = MakeModel(modality: "text->image");

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.ModalitiesIn.Should().Be("\uD83D\uDCDD");
        item.ModalitiesOut.Should().Be("\uD83D\uDDBC");
    }

    [Theory]
    [InlineData("text->embeddings", "\uD83D\uDCDD", "\uD83D\uDD22")]
    [InlineData("text->embedding", "\uD83D\uDCDD", "\uD83D\uDD22")]
    [InlineData("text->decisions", "\uD83D\uDCDD", "\uD83C\uDFAF")]
    [InlineData("text->decision", "\uD83D\uDCDD", "\uD83C\uDFAF")]
    [InlineData("text->rerank", "\uD83D\uDCDD", "\uD83D\uDCCA")]
    [InlineData("text->speech", "\uD83D\uDCDD", "\uD83D\uDD0A")]
    [InlineData("text->transcription", "\uD83D\uDCDD", "\uD83D\uDD0A")]
    public void ParseModalities_RecognisesNewTokens(string modality, string expectedIn, string expectedOut)
    {
        // Arrange
        var model = MakeModel(modality: modality);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.ModalitiesIn.Should().Be(expectedIn);
        item.ModalitiesOut.Should().Be(expectedOut);
    }

    [Fact]
    public void ParseModalities_CombinesMultipleNewTokens()
    {
        // Arrange
        var model = MakeModel(modality: "text->embeddings+decisions");

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.ModalitiesIn.Should().Be("\uD83D\uDCDD");
        item.ModalitiesOut.Should().Be("\uD83D\uDD22 \uD83C\uDFAF");
    }

    [Fact]
    public void ParseModalities_FallsBackToRawString_WhenNoRecognisedTokens()
    {
        // Arrange
        var model = MakeModel(modality: "text->unknown");

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.ModalitiesIn.Should().Be("\uD83D\uDCDD");
        item.ModalitiesOut.Should().Be("text->unknown");
    }

    [Fact]
    public void ParseModalities_SplitsExistingTokens()
    {
        // Arrange
        var model = MakeModel(modality: "text+image+audio->text+video");

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.ModalitiesIn.Should().Be("\uD83D\uDCDD \uD83D\uDDBC \uD83D\uDD0A");
        item.ModalitiesOut.Should().Be("\uD83D\uDCDD \uD83C\uDFAC");
    }

    [Fact]
    public void DeriveCapabilities_DerivesImageRecognitionFromImageIn()
    {
        // Arrange
        var model = MakeModel(modality: "text+image->text", capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.Capabilities.Should().BeNull();
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.ImageRecognition);
    }

    [Fact]
    public void DeriveCapabilities_DerivesImageGenerationFromImageOut()
    {
        // Arrange
        var model = MakeModel(modality: "text->image", capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.ImageGeneration);
    }

    [Fact]
    public void DeriveCapabilities_DerivesEmbeddingFromEmbeddingsOut()
    {
        // Arrange
        var model = MakeModel(modality: "text->embeddings", capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.Embedding);
    }

    [Fact]
    public void DeriveCapabilities_DerivesDecisionFromDecisionsOut()
    {
        // Arrange
        var model = MakeModel(modality: "text->decisions", capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.Decision);
    }

    [Fact]
    public void DeriveCapabilities_DerivesRerankerFromRerankOut()
    {
        // Arrange
        var model = MakeModel(modality: "text->rerank", capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.Reranker);
    }

    [Fact]
    public void DeriveCapabilities_DerivesAudioCapabilities()
    {
        // Arrange
        var model = MakeModel(modality: "audio->text+speech", capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.AudioRecognition);
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.TextToSpeech);
    }

    [Fact]
    public void DeriveCapabilities_DerivesVideoCapabilities()
    {
        // Arrange
        var model = MakeModel(modality: "video->text+video", capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.VideoRecognition);
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.VideoGeneration);
    }

    [Fact]
    public void DeriveCapabilities_ReportedCapabilitiesTakePrecedence()
    {
        // Arrange
        var model = MakeModel(modality: "text+image->text",
            capabilities: EModelCapability.ToolCalling);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.Capabilities.Should().Be(EModelCapability.ToolCalling);
        item.EffectiveCapabilities.Should().Be(EModelCapability.ToolCalling);
        item.EffectiveCapabilities.Should().NotHaveFlag(EModelCapability.ImageRecognition);
    }

    [Fact]
    public void DeriveCapabilities_NoneReportedWithNoModality_ReturnsNone()
    {
        // Arrange
        var model = MakeModel(modality: null, capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.Capabilities.Should().BeNull();
        item.EffectiveCapabilities.Should().Be(EModelCapability.None);
    }

    [Fact]
    public void DeriveCapabilities_TextOnlyModality_DerivesTextGeneration()
    {
        // Arrange
        var model = MakeModel(modality: "text->text", capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.EffectiveCapabilities.Should().Be(EModelCapability.TextGeneration);
    }

    [Fact]
    public void DeriveCapabilities_CombinesMultipleFlags()
    {
        // Arrange
        var model = MakeModel(modality: "text+image+audio->text+embeddings+decisions", capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.TextGeneration);
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.ImageRecognition);
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.AudioRecognition);
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.Embedding);
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.Decision);
    }

    [Fact]
    public void DeriveCapabilities_ExcludesDecision_ForRespanOwner()
    {
        // Arrange
        var model = new AIModel
        {
            Id = "respan/span-01",
            DisplayName = "Span-01",
            Modality = "text->decisions",
            OwnedBy = "respan",
        };

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert — output side has no "text" token, so TextGeneration is not derived;
        // Decision is also excluded by the respan owner rule.
        item.EffectiveCapabilities.Should().Be(EModelCapability.None);
    }

    [Fact]
    public void DeriveCapabilities_ExcludesDecision_ForSpanNamePrefix()
    {
        // Arrange
        var model = new AIModel
        {
            Id = "someowner/span-01-lite",
            DisplayName = "Span-01 Lite",
            Modality = "text->decisions",
        };

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.EffectiveCapabilities.Should().NotHaveFlag(EModelCapability.Decision);
    }

    [Fact]
    public void DeriveCapabilities_DoesNotExcludeDecision_ForOtherOwners()
    {
        // Arrange
        var model = new AIModel
        {
            Id = "typesafe/jev-latest",
            DisplayName = "Jev Latest",
            Modality = "text->decisions",
            OwnedBy = "typesafe",
        };

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.EffectiveCapabilities.Should().HaveFlag(EModelCapability.Decision);
    }

    [Fact]
    public void CapabilitiesText_ShowsDerivedFlags_WhenProviderReportsNothing()
    {
        // Arrange
        var model = MakeModel(modality: "text+image->text", capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.Capabilities.Should().BeNull();
        item.CapabilitiesText.Should().Be("TextGeneration, ImageRecognition (derived)");
    }

    [Fact]
    public void CapabilitiesText_ShowsNotReported_WhenNothingDerivable()
    {
        // Arrange
        var model = MakeModel(modality: null, capabilities: null);

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert
        item.CapabilitiesText.Should().Be("not reported");
    }

    [Fact]
    public void CapabilitiesText_ShowsRawModality_WhenDerivationYieldsNothingButModalityExists()
    {
        // Arrange — span model excluded from Decision derivation; text on input side
        // does not derive TextGeneration (output-only rule).
        var model = new AIModel
        {
            Id = "respan/span-01",
            DisplayName = "Span-01",
            Modality = "text->decisions",
            OwnedBy = "respan",
        };

        // Act
        var item = ModelSelectionItem.FromAIModel(model);

        // Assert — raw modality shown so user sees what the provider reported.
        item.CapabilitiesText.Should().Be("text->decisions");
    }
}
