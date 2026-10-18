using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// The D9 escape hatch: a consumer registers a <see cref="EProviderProtocol.Native"/> definition with
/// its own provider class through the public builder, seeds the options with the now-public
/// <c>SeedFromDefinition</c>, and answers decisions with the public wire maps. Nothing here reaches an
/// <c>internal</c> member, which is what makes the ladder usable without a library release.
/// </summary>
public class AuthoredDecisionProviderTests
{
    private const string ProviderId = "authored-decisions";

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
        DisplayName = "Authored decisions provider",
        BaseUrl = "https://test.example.com/",
        Protocol = EProviderProtocol.Native
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
    public async Task AuthoredNativeProvider_IsDecisionCapableAndAnswersThroughPublicApi()
    {
        // Arrange — the Native definition is skipped by the registration loop; the consumer's own
        // factory builds the class and seeds its options from the definition
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var definition = Definition();
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(handler));
        services.AddAiProviders(b => b
            .Add(definition)
            .AddProvider<AuthoredDecisionProvider>(ProviderId, sp =>
            {
                var options = new DecisionProviderOptions { ApiKey = "fake-api-key" };
                AIProviderServiceCollectionExtensions.SeedFromDefinition(options, definition);

                return new AuthoredDecisionProvider(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<IProviderCatalog>(),
                    options,
                    ProviderId,
                    "api/decide");
            }));
        using var provider = services.BuildServiceProvider();

        // Act
        var resolved = provider.GetRequiredKeyedService<IAIProvider>(ProviderId);
        var decision = (IDecisionProvider)resolved;
        var response = await decision.DecideAsync(Request());

        // Assert — capability discovery finds the authored class, and the seeded base URL is used
        resolved.Should().BeAssignableTo<IDecisionProvider>();
        decision.SupportsDecisions.Should().BeTrue();
        decision.DecisionSource.Should().Be(EDecisionSource.Native);
        handler.LastRequest!.RequestUri!.AbsoluteUri
            .Should().Be("https://test.example.com/api/decide");
        using var body = JsonDocument.Parse(handler.CapturedBody!);
        body.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().Equal("model", "state", "questions");
        response.Answers["priority"].Noul!.ProbabilityOfYes.Should().Be(0.6);
    }
}
