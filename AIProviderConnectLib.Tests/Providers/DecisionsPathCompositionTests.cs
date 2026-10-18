using AIProviderConnect.Abstractions;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// Path composition for a <c>Decision</c>-rooted definition whose decisions surface is pinned by the
/// <c>endpoints</c> block alone: the relative path is resolved against the definition's own base URL,
/// with no <c>/v1/</c> prefix assumed and no <c>protocolConfiguration</c> key required.
/// </summary>
public class DecisionsPathCompositionTests
{
    private const string ProviderId = "path-host";

    private const string DecisionResponseJson = """
        {
          "id": "resp-1",
          "model": "model-under-test",
          "provider": "Local",
          "answers": {
            "priority": { "type": "noul", "noul": 0.6 }
          }
        }
        """;

    private static ProviderDefinition Definition() => new()
    {
        Id = ProviderId,
        DisplayName = "Path host",
        BaseUrl = "https://test.example.com/",
        Protocol = EProviderProtocol.Decision,
        Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["decisions"] = new EndpointDefinition { Path = "api/decide" }
        }
    };

    private static DecisionRequest Request() => new()
    {
        Model = "model-under-test",
        State = "A billing question.",
        Questions = new Dictionary<string, DecisionQuestion>
        {
            ["priority"] = new() { Kind = EDecisionQuestionKind.Noul }
        }
    };

    [Fact]
    public async Task EndpointPath_ResolvesAgainstDefinitionRoot_WithoutVersionPrefix()
    {
        // Arrange — the base URL carries no /v1/ segment and the definition declares no
        // protocolConfiguration: the endpoints block alone supplies the surface
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(handler));
        services.AddAiProviders(b =>
        {
            b.Add(Definition());
            b.Configure<DecisionProviderOptions>(ProviderId, o => o.ApiKey = "fake-api-key");
        });
        using var provider = services.BuildServiceProvider();
        var decision = (IDecisionProvider)provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Act
        var response = await decision.DecideAsync(Request());

        // Assert
        handler.LastRequest!.RequestUri!.AbsoluteUri
            .Should().Be("https://test.example.com/api/decide");
        response.Answers["priority"].Noul!.ProbabilityOfYes.Should().Be(0.6);
    }
}
