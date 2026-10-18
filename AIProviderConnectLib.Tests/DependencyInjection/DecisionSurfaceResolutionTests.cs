using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnect.Tests.DependencyInjection;

/// <summary>
/// Class selection for the <c>decisions</c> endpoint entry: a <c>decision</c> pin still selects the
/// native class, a <c>decisionReadout</c> pin is accepted on a chat-capable root and rejected on a
/// <c>Decision</c> root, and a definition with no entry is decision-capable through the base class.
/// The ordering test locks <see cref="AIProviderRegistrationBuilder.Configure{TOptions}"/> after
/// definition seeding, which is how a consumer value wins over a JSON key.
/// </summary>
public class DecisionSurfaceResolutionTests
{
    private const string ProviderId = "surface-host";

    private const string LogprobsResponseJson = """
        {
          "id": "chatcmpl-surface-1",
          "model": "model-under-test",
          "choices": [
            {
              "message": { "role": "assistant", "content": "a" },
              "logprobs": {
                "content": [
                  {
                    "token": "a",
                    "logprob": -0.5108256237659907,
                    "top_logprobs": [
                      { "token": "a", "logprob": -0.5108256237659907 },
                      { "token": "b", "logprob": -1.2039728043259361 }
                    ]
                  }
                ]
              }
            }
          ],
          "usage": { "prompt_tokens": 10, "completion_tokens": 1, "total_tokens": 11 }
        }
        """;

    private static ProviderDefinition Definition(
        EProviderProtocol protocol,
        EProviderProtocol? decisionsProtocol = null,
        IReadOnlyDictionary<string, string>? protocolConfiguration = null) =>
        new()
        {
            Id = ProviderId,
            DisplayName = "Surface host",
            BaseUrl = "https://test.example.com/v1/",
            Protocol = protocol,
            Endpoints = decisionsProtocol is null
                ? null
                : new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["decisions"] = new EndpointDefinition { Protocol = decisionsProtocol }
                },
            ProtocolConfiguration = protocolConfiguration
        };

    private static ServiceProvider Build(
        CapturingHttpMessageHandler handler,
        ProviderDefinition definition,
        Action<AIProviderOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(handler));
        services.AddAiProviders(b =>
        {
            b.Add(definition);

            if (configure is not null)
                b.Configure<OpenAICompatibleProviderOptions>(definition.Id, configure);
        });

        return services.BuildServiceProvider();
    }

    private static DecisionRequest Request() => new()
    {
        Model = "model-under-test",
        State = "A billing question.",
        Questions = new Dictionary<string, DecisionQuestion>
        {
            ["intent"] = new()
            {
                Kind = EDecisionQuestionKind.Choice,
                Criteria = new Dictionary<string, string>
                {
                    ["refund"] = "The customer wants a refund.",
                    ["technical"] = "The customer needs technical help."
                }
            }
        }
    };

    [Fact]
    public void DecisionEntry_StillSelectsNativeClass()
    {
        // Arrange — the native pin is unchanged by the universal capability
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);
        using var provider = Build(
            handler,
            Definition(EProviderProtocol.OpenAICompatible, EProviderProtocol.Decision),
            o => o.ApiKey = "fake-api-key");

        // Act
        var resolved = provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Assert
        resolved.Should().BeOfType<OpenAICompatibleDecisionProvider>();
        ((IDecisionProvider)resolved).DecisionSource.Should().Be(EDecisionSource.Native);
    }

    [Fact]
    public void DecisionReadoutEntry_PinsReadoutOnChatCapableRoot()
    {
        // Arrange — the pin picks the read-out where a native surface also exists, without
        // changing the class: the chat implementation still serves the provider
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);
        using var provider = Build(
            handler,
            Definition(EProviderProtocol.OpenAICompatible, EProviderProtocol.DecisionReadout),
            o => o.ApiKey = "fake-api-key");

        // Act
        var resolved = provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Assert
        resolved.Should().BeOfType<OpenAICompatibleProvider>();
        ((IDecisionProvider)resolved).SupportsDecisions.Should().BeTrue();
        ((IDecisionProvider)resolved).DecisionSource.Should().Be(EDecisionSource.Readout);
    }

    [Fact]
    public void DecisionReadoutEntry_OnDecisionRoot_IsRejected()
    {
        // Arrange — a Decision-rooted provider has no chat surface to read out of
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);

        // Act
        var act = () => Build(
            handler, Definition(EProviderProtocol.Decision, EProviderProtocol.DecisionReadout));

        // Assert
        var exception = act.Should().Throw<AiException>().Which;
        exception.Code.Should().Be(AiErrorCodes.ConfigurationError);
        exception.Message.Should().Contain("no chat surface");
    }

    [Fact]
    public void NoDecisionsEntry_ProducesDecisionCapableProvider()
    {
        // Arrange — a plain definition declares nothing about decisions
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);
        using var provider = Build(
            handler, Definition(EProviderProtocol.OpenAICompatible), o => o.ApiKey = "fake-api-key");

        // Act
        var resolved = provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Assert
        resolved.Should().BeOfType<OpenAICompatibleProvider>();
        ((IDecisionProvider)resolved).SupportsDecisions.Should().BeTrue();
        ((IDecisionProvider)resolved).DecisionSource.Should().Be(EDecisionSource.Readout);
    }

    [Fact]
    public async Task ReadoutConfiguredOnlyThroughTypedOptions_RegistersAndAnswers()
    {
        // Arrange — no protocolConfiguration keys at all; the typed member is the only override
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);
        using var provider = Build(
            handler,
            Definition(EProviderProtocol.OpenAICompatible),
            o =>
            {
                o.ApiKey = "fake-api-key";
                o.DecisionReadoutOptionTokenStyle = "lowercaseLetter";
            });
        var decision = (IDecisionProvider)provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Act
        var response = await decision.DecideAsync(Request());

        // Assert
        response.Answers["intent"].Choice!.Selected.Should().Be("refund");
        using var body = JsonDocument.Parse(handler.CapturedBody!);
        var prompt = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        prompt.Should().Contain("a. refund: The customer wants a refund.");
        prompt.Should().Contain("b. technical: The customer needs technical help.");
    }

    [Fact]
    public async Task ConsumerConfigure_WinsOverDefinitionSeededReadoutKey()
    {
        // Arrange — the definition seeds a template and the consumer overrides it; RegisterCore
        // applies consumer callbacks after seeding, and this test locks that ordering
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);
        using var provider = Build(
            handler,
            Definition(
                EProviderProtocol.OpenAICompatible,
                protocolConfiguration: new Dictionary<string, string>
                {
                    ["decisionReadoutPromptTemplate"] = "seeded {state}"
                }),
            o =>
            {
                o.ApiKey = "fake-api-key";
                o.DecisionReadoutPromptTemplate = "consumer {state}";
            });
        var decision = (IDecisionProvider)provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Act
        await decision.DecideAsync(Request());

        // Assert
        using var body = JsonDocument.Parse(handler.CapturedBody!);
        body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()
            .Should().Be("consumer A billing question.");
    }
}
