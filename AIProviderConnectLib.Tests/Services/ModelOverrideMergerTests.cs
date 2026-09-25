using AIProviderConnect.Models;
using AIProviderConnect.Services;

using FluentAssertions;

namespace AIProviderConnect.Tests.Services;

public class ModelOverrideMergerTests
{
    private static AIModel Live(
        string id,
        string displayName = "Live Name",
        decimal? promptPrice = null,
        decimal? completionPrice = null,
        string? description = null,
        int? contextWindow = null) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            PromptPrice = promptPrice,
            CompletionPrice = completionPrice,
            Description = description,
            ContextWindow = contextWindow,
            ProviderId = "test-provider"
        };

    [Fact]
    public void Merge_EmptyOverrides_ReturnsLiveListUnchanged()
    {
        // Arrange
        var live = new List<AIModel> { Live("m1") };

        // Act
        var result = ModelOverrideMerger.Merge(live, [], "test-provider");

        // Assert
        result.Should().BeSameAs(live, "no overrides means the live list is returned as-is");
    }

    [Fact]
    public void Merge_MatchedOverride_PatchesOnlySetFields()
    {
        // Arrange
        var live = new List<AIModel>
        {
            Live("gpt-4o", displayName: "GPT-4o", description: "live description", contextWindow: 128000)
        };
        var overrides = new List<ModelOverride>
        {
            new() { Id = "gpt-4o", PromptPrice = 2.50m, CompletionPrice = 10.00m }
        };

        // Act
        var result = ModelOverrideMerger.Merge(live, overrides, "test-provider");

        // Assert
        var model = result.Should().ContainSingle().Subject;
        model.PromptPrice.Should().Be(2.50m);
        model.CompletionPrice.Should().Be(10.00m);
        model.DisplayName.Should().Be("GPT-4o", "unset override fields keep the live value");
        model.Description.Should().Be("live description");
        model.ContextWindow.Should().Be(128000);
    }

    [Fact]
    public void Merge_DoesNotMutateTheLiveInstance()
    {
        // Arrange
        var liveModel = Live("m1", promptPrice: 1.0m);
        var live = new List<AIModel> { liveModel };
        var overrides = new List<ModelOverride> { new() { Id = "m1", PromptPrice = 9.0m } };

        // Act
        var result = ModelOverrideMerger.Merge(live, overrides, "test-provider");

        // Assert
        liveModel.PromptPrice.Should().Be(1.0m, "the merge must copy, not mutate the shared instance");
        result[0].Should().NotBeSameAs(liveModel);
        result[0].PromptPrice.Should().Be(9.0m);
    }

    [Fact]
    public void Merge_UnmatchedOverride_AppendsWithDisplayNameFallbackToId()
    {
        // Arrange
        var live = new List<AIModel> { Live("m1") };
        var overrides = new List<ModelOverride> { new() { Id = "brand-new" } };

        // Act
        var result = ModelOverrideMerger.Merge(live, overrides, "test-provider");

        // Assert
        result.Should().HaveCount(2);
        var appended = result[1];
        appended.Id.Should().Be("brand-new");
        appended.DisplayName.Should().Be("brand-new", "DisplayName falls back to Id when unset");
        appended.ProviderId.Should().Be("test-provider");
    }

    [Fact]
    public void Merge_HiddenOverride_RemovesLiveModel()
    {
        // Arrange
        var live = new List<AIModel> { Live("keep"), Live("drop") };
        var overrides = new List<ModelOverride> { new() { Id = "drop", Hidden = true } };

        // Act
        var result = ModelOverrideMerger.Merge(live, overrides, "test-provider");

        // Assert
        result.Should().ContainSingle().Which.Id.Should().Be("keep");
    }

    [Fact]
    public void Merge_EmptyBaseList_YieldsOnlyOverrideDerivedModels()
    {
        // Arrange
        var overrides = new List<ModelOverride>
        {
            new() { Id = "a", DisplayName = "Model A" },
            new() { Id = "b", PromptPrice = 3.0m }
        };

        // Act
        var result = ModelOverrideMerger.Merge([], overrides, "test-provider");

        // Assert
        result.Should().HaveCount(2);
        result.Select(m => m.Id).Should().Equal("a", "b");
        result[1].PromptPrice.Should().Be(3.0m);
    }

    [Fact]
    public void Merge_PreservesBaseOrder_AndAppendsNewModelsLast()
    {
        // Arrange
        var live = new List<AIModel> { Live("first"), Live("second"), Live("third") };
        var overrides = new List<ModelOverride>
        {
            new() { Id = "second", PromptPrice = 5.0m },
            new() { Id = "new-one" }
        };

        // Act
        var result = ModelOverrideMerger.Merge(live, overrides, "test-provider");

        // Assert
        result.Select(m => m.Id).Should().Equal("first", "second", "third", "new-one");
        result[1].PromptPrice.Should().Be(5.0m);
    }

    [Fact]
    public void Merge_MatchesIds_CaseInsensitively()
    {
        // Arrange
        var live = new List<AIModel> { Live("GPT-4o", promptPrice: 1.0m) };
        var overrides = new List<ModelOverride> { new() { Id = "gpt-4o", PromptPrice = 2.0m } };

        // Act
        var result = ModelOverrideMerger.Merge(live, overrides, "test-provider");

        // Assert
        result.Should().ContainSingle().Which.PromptPrice.Should().Be(2.0m);
    }

    [Fact]
    public void Merge_HiddenThenReadded_LaterOverrideWins()
    {
        // Arrange — a later override can re-materialize a model hidden by an earlier one.
        var live = new List<AIModel> { Live("m1") };
        var overrides = new List<ModelOverride>
        {
            new() { Id = "m1", Hidden = true },
            new() { Id = "m1", DisplayName = "Reborn" }
        };

        // Act
        var result = ModelOverrideMerger.Merge(live, overrides, "test-provider");

        // Assert
        result.Should().ContainSingle().Which.DisplayName.Should().Be("Reborn");
    }
}
