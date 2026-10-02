using AIProviderConnect.Abstractions;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Models;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnect.Tests.DependencyInjection;

/// <summary>
/// Verifies that a consumer-supplied Decision-protocol definition resolves a keyed
/// <see cref="IAIProvider"/> that is also <see cref="IDecisionProvider"/>, and that it stays
/// <see cref="IDecisionProvider"/> when model overrides are registered for the same id.
/// </summary>
public class DecisionProviderRegistrationTests
{
    private static ProviderDefinition Definition(string id = "openrouter-decisions") =>
        new()
        {
            Id = id,
            DisplayName = "Decisions provider",
            BaseUrl = "https://test.example.com/api/",
            Protocol = EProviderProtocol.Decision
        };

    [Fact]
    public void ConsumerSuppliedDecisionDefinition_ResolvesIDecisionProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(b => b.Add(Definition()));

        // Act
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("openrouter-decisions");

        // Assert
        resolved.Should().BeAssignableTo<IDecisionProvider>();
        ((IDecisionProvider)resolved).SupportsDecisions.Should().BeTrue();
    }

    [Fact]
    public void DecisionProviderWithModelOverrides_StaysIDecisionProvider()
    {
        // Arrange — overriding models must not hide the decision capability behind a decorator.
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(b => b
            .Add(Definition())
            .OverrideModels("openrouter-decisions",
                [new ModelOverride { Id = "typesafe/jev-1.13", Capabilities = EModelCapability.Decision }]));

        // Act
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("openrouter-decisions");

        // Assert
        resolved.Should().BeAssignableTo<IDecisionProvider>();
        ((IDecisionProvider)resolved).SupportsDecisions.Should().BeTrue();
    }
}
