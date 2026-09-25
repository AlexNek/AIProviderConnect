using AiCleverness.Models.DecisionTree;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using ScraperTool.Models;
using ScraperTool.Services.UrlResearch.DependencyInjection;

namespace ScraperTool.Tests;

public class DecisionTreeFailoverConfigurationTests
{
    [Fact]
    public void AddUrlResearchDecisionTree_BothModelsConfigured_EnablesFailover()
    {
        // Arrange
        var services = new ServiceCollection();
        var settings = new AppSettings
        {
            PrimaryModel = "primary-model",
            FallbackModel = "fallback-model"
        };

        // Act
        services.AddUrlResearchDecisionTree(settings);
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<DecisionTreeExecutionOptions>();

        // Assert
        options.EnableModelFailover.Should().BeTrue();
        options.Model.Should().Be("primary-model");
        options.ModelFallbackChain.Should().ContainSingle().Which.Should().Be("fallback-model");
    }

    [Fact]
    public void AddUrlResearchDecisionTree_OnlyPrimaryModel_DisablesFailover()
    {
        // Arrange
        var services = new ServiceCollection();
        var settings = new AppSettings
        {
            PrimaryModel = "primary-model",
            FallbackModel = ""
        };

        // Act
        services.AddUrlResearchDecisionTree(settings);
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<DecisionTreeExecutionOptions>();

        // Assert
        options.EnableModelFailover.Should().BeFalse();
    }

    [Fact]
    public void AddUrlResearchDecisionTree_OnlyFallbackModel_DisablesFailover()
    {
        // Arrange
        var services = new ServiceCollection();
        var settings = new AppSettings
        {
            PrimaryModel = "",
            FallbackModel = "fallback-model"
        };

        // Act
        services.AddUrlResearchDecisionTree(settings);
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<DecisionTreeExecutionOptions>();

        // Assert
        options.EnableModelFailover.Should().BeFalse();
    }

    [Fact]
    public void AddUrlResearchDecisionTree_NoModels_DisablesFailover()
    {
        // Arrange
        var services = new ServiceCollection();
        var settings = new AppSettings
        {
            PrimaryModel = "",
            FallbackModel = ""
        };

        // Act
        services.AddUrlResearchDecisionTree(settings);
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<DecisionTreeExecutionOptions>();

        // Assert
        options.EnableModelFailover.Should().BeFalse();
    }
}
