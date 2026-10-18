using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// The feature's central claim: an OpenAI-compatible definition with no <c>decisions</c> entry and no
/// read-out configuration resolves as <see cref="IDecisionProvider"/>, reports the read-out source,
/// and answers Choice / Noul / Score from a chat response carrying logprobs — rendering the built-in
/// prompt rather than a test-supplied template.
/// </summary>
public class ProviderDecisionCapabilityTests
{
    private const string ProviderId = "readout-host";

    private static readonly string ChoicePrompt = """
        State: A billing question.

        Classify the ticket.

        A. refund: The customer wants a refund.
        B. technical: The customer needs technical help.
        C. other: Anything else.

        Answer:
        """.ReplaceLineEndings("\n");

    private static readonly string NoulPrompt = """
        State: A billing question.

        Is the customer angry?

        yes: The customer is angry.
        no: The customer is calm.

        Answer yes or no:
        """.ReplaceLineEndings("\n");

    private static readonly string ScorePrompt = """
        State: A billing question.

        How satisfied is the customer?

        A. negative
        B. neutral
        C. positive

        Answer:
        """.ReplaceLineEndings("\n");

    // One canned distribution covering the lettered options and the yes/no tokens, so the same host
    // response can answer any of the three question kinds.
    private const string LogprobsResponseJson = """
        {
          "id": "chatcmpl-readout-1",
          "model": "model-under-test",
          "choices": [
            {
              "message": { "role": "assistant", "content": "A" },
              "logprobs": {
                "content": [
                  {
                    "token": "A",
                    "logprob": -0.5108256237659907,
                    "top_logprobs": [
                      { "token": "A", "logprob": -0.5108256237659907 },
                      { "token": "B", "logprob": -1.2039728043259361 },
                      { "token": "C", "logprob": -2.302585092994046 },
                      { "token": "yes", "logprob": -0.22314355131420976 },
                      { "token": "no", "logprob": -1.6094379124341003 }
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
        string id = ProviderId,
        EProviderProtocol protocol = EProviderProtocol.OpenAICompatible) =>
        new()
        {
            Id = id,
            DisplayName = "Readout host",
            BaseUrl = "https://test.example.com/v1/",
            Protocol = protocol
        };

    private static ServiceProvider BuildProvider(
        CapturingHttpMessageHandler handler,
        ProviderDefinition definition,
        Action<AIProviderOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(handler));
        services.AddAiProviders(b =>
        {
            b.Add(definition);

            Action<AIProviderOptions> settings = o =>
            {
                o.ApiKey = "fake-api-key";
                configure?.Invoke(o);
            };

            if (definition.Protocol == EProviderProtocol.HybridGateway)
                b.Configure<HybridGatewayProviderOptions>(definition.Id, settings);
            else
                b.Configure<OpenAICompatibleProviderOptions>(definition.Id, settings);
        });

        return services.BuildServiceProvider();
    }

    private static DecisionQuestion ChoiceQuestion() => new()
    {
        Kind = EDecisionQuestionKind.Choice,
        Instructions = "Classify the ticket.",
        Criteria = new Dictionary<string, string>
        {
            ["refund"] = "The customer wants a refund.",
            ["technical"] = "The customer needs technical help.",
            ["other"] = "Anything else."
        }
    };

    private static DecisionQuestion NoulQuestion() => new()
    {
        Kind = EDecisionQuestionKind.Noul,
        Instructions = "Is the customer angry?",
        Criteria = new Dictionary<string, string>
        {
            ["true"] = "The customer is angry.",
            ["false"] = "The customer is calm."
        }
    };

    private static DecisionQuestion ScoreQuestion() => new()
    {
        Kind = EDecisionQuestionKind.Score,
        Instructions = "How satisfied is the customer?",
        Scale = ["negative", "neutral", "positive"]
    };

    private static DecisionRequest Request(params (string Name, DecisionQuestion Question)[] questions) => new()
    {
        Model = "model-under-test",
        State = "A billing question.",
        Questions = questions.ToDictionary(q => q.Name, q => q.Question)
    };

    [Fact]
    public void DefinitionWithoutDecisionsEntry_ResolvesAsReadoutProvider()
    {
        // Arrange — no endpoints block, no protocolConfiguration, no read-out configuration
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);
        using var provider = BuildProvider(handler, Definition());

        // Act
        var resolved = provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Assert
        resolved.Should().BeAssignableTo<IDecisionProvider>();
        ((IDecisionProvider)resolved).SupportsDecisions.Should().BeTrue();
        ((IDecisionProvider)resolved).DecisionSource.Should().Be(EDecisionSource.Readout);
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task ReadoutAnswersChoice_WithBuiltInPrompt()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);
        using var provider = BuildProvider(handler, Definition());
        var decision = (IDecisionProvider)provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Act
        var response = await decision.DecideAsync(Request(("intent", ChoiceQuestion())));

        // Assert
        var choice = response.Answers["intent"].Choice!;
        choice.Selected.Should().Be("refund");
        choice.Confidence.Should().BeApproximately(0.6, 1e-9);
        choice.Probabilities["technical"].Should().BeApproximately(0.3, 1e-9);
        choice.Probabilities["other"].Should().BeApproximately(0.1, 1e-9);

        handler.LastRequest!.RequestUri!.AbsoluteUri
            .Should().Be("https://test.example.com/v1/chat/completions");
        using var body = JsonDocument.Parse(handler.CapturedBody!);
        body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()
            .Should().Be(ChoicePrompt);
        body.RootElement.GetProperty("logprobs").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("top_logprobs").GetInt32().Should().Be(20);
        body.RootElement.GetProperty("temperature").GetDouble().Should().Be(0d);
    }

    [Fact]
    public async Task ReadoutAnswersNoul_WithBuiltInPrompt()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);
        using var provider = BuildProvider(handler, Definition());
        var decision = (IDecisionProvider)provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Act
        var response = await decision.DecideAsync(Request(("angry", NoulQuestion())));

        // Assert
        response.Answers["angry"].Noul!.ProbabilityOfYes.Should().BeApproximately(0.8, 1e-9);
        using var body = JsonDocument.Parse(handler.CapturedBody!);
        body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()
            .Should().Be(NoulPrompt);
    }

    [Fact]
    public async Task ReadoutAnswersScore_WithBuiltInPrompt()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);
        using var provider = BuildProvider(handler, Definition());
        var decision = (IDecisionProvider)provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Act
        var response = await decision.DecideAsync(Request(("satisfaction", ScoreQuestion())));

        // Assert
        var score = response.Answers["satisfaction"].Score!;
        score.Score.Should().BeApproximately(0.5, 1e-9);
        score.Confidence.Should().BeApproximately(0.6, 1e-9);
        score.Legend.Should().Equal("negative", "neutral", "positive");
        using var body = JsonDocument.Parse(handler.CapturedBody!);
        body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()
            .Should().Be(ScorePrompt);
    }

    [Fact]
    public async Task ConsumerSuppliedHybridGatewayDefinition_AnswersWithNoLibraryChange()
    {
        // Arrange — a definition the library has never heard of; capability comes from the base class
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);
        using var provider = BuildProvider(
            handler, Definition("consumer-unknown-host", EProviderProtocol.HybridGateway));
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("consumer-unknown-host");

        // Act
        var response = await ((IDecisionProvider)resolved)
            .DecideAsync(Request(("intent", ChoiceQuestion())));

        // Assert
        resolved.Should().BeAssignableTo<IDecisionProvider>();
        response.Answers["intent"].Choice!.Selected.Should().Be("refund");
        handler.LastRequest!.RequestUri!.AbsoluteUri
            .Should().Be("https://test.example.com/v1/chat/completions");
    }

    [Fact]
    public async Task ReadoutDisabled_ReportsFalseAndRefuses()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);
        using var provider = BuildProvider(
            handler, Definition(), o => o.DecisionReadoutEnabled = false);
        var decision = (IDecisionProvider)provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Act
        var act = () => decision.DecideAsync(Request(("intent", ChoiceQuestion())));

        // Assert
        decision.SupportsDecisions.Should().BeFalse();
        (await act.Should().ThrowAsync<AiException>())
            .Which.Code.Should().Be(AiErrorCodes.DecisionNotSupported);
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task ChoiceAboveTopLogprobs_ThrowsConfigurationErrorBeforeRequest()
    {
        // Arrange — three declared options cannot be read through a top_logprobs of two
        using var handler = new CapturingHttpMessageHandler(LogprobsResponseJson);
        using var provider = BuildProvider(
            handler, Definition(), o => o.DecisionReadoutTopLogprobs = 2);
        var decision = (IDecisionProvider)provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Act
        var act = () => decision.DecideAsync(Request(("intent", ChoiceQuestion())));

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.ConfigurationError);
        exception.Message.Should().Contain(ProviderId).And.Contain("2");
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task ResponseWithoutLogprobs_ThrowsDecisionReadoutUnavailable()
    {
        // Arrange — a host whose chat API exposes no logprobs
        using var handler = new CapturingHttpMessageHandler(
            """{ "choices": [ { "message": { "content": "A" } } ] }""");
        using var provider = BuildProvider(handler, Definition());
        var decision = (IDecisionProvider)provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Act
        var act = () => decision.DecideAsync(Request(("intent", ChoiceQuestion())));

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.DecisionReadoutUnavailable);
        exception.Message.Should().Contain(ProviderId);
    }

    [Fact]
    public async Task ResponseWithoutAnyDeclaredOption_ThrowsDecisionReadoutUnreadable()
    {
        // Arrange — the answer position names a token that is not one of the declared options
        using var handler = new CapturingHttpMessageHandler("""
            {
              "choices": [
                {
                  "message": { "content": "Z" },
                  "logprobs": { "content": [ { "token": "Z", "logprob": -0.1, "top_logprobs": [] } ] }
                }
              ]
            }
            """);
        using var provider = BuildProvider(handler, Definition());
        var decision = (IDecisionProvider)provider.GetRequiredKeyedService<IAIProvider>(ProviderId);

        // Act
        var act = () => decision.DecideAsync(Request(("intent", ChoiceQuestion())));

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.DecisionReadoutUnreadable);
        exception.Message.Should().Contain(ProviderId);
    }
}
