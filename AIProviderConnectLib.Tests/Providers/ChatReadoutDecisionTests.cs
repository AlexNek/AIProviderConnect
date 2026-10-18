using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// The single read-out implementation: prompt render, option-token styles, softmax renormalisation,
/// per-kind answer mapping, one call per question, failure and cancellation behaviour, and usage
/// aggregation. Everything is exercised through a scripted <see cref="IAIProvider"/>, so no HTTP and
/// no protocol is involved — the read-out must not branch on either.
/// </summary>
public class ChatReadoutDecisionTests
{
    private const double Ln06 = -0.5108256237659907;
    private const double Ln03 = -1.2039728043259361;
    private const double Ln01 = -2.302585092994046;
    private const double Ln02 = -1.6094379124341003;
    private const double Ln07 = -0.35667494393873245;

    private static readonly string ChoicePrompt = """
        State: A billing question.

        Classify the ticket.

        A. refund: The customer wants a refund.
        B. technical: The customer needs technical help.

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

    private static DecisionQuestion ChoiceQuestion() => new()
    {
        Kind = EDecisionQuestionKind.Choice,
        Instructions = "Classify the ticket.",
        Criteria = new Dictionary<string, string>
        {
            ["refund"] = "The customer wants a refund.",
            ["technical"] = "The customer needs technical help."
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

    private static ChatCompletionResponse Response(
        string token,
        double logprob,
        (string Token, double Logprob)[]? tops = null,
        string id = "resp-1",
        string model = "model-under-test",
        UsageInfo? usage = null) =>
        new()
        {
            Id = id,
            Model = model,
            Usage = usage ?? new UsageInfo(),
            Logprobs = new ChatContentLogprobs
            {
                Content =
                [
                    new ChatTokenLogprob(
                        token,
                        logprob,
                        (tops ?? []).Select(t => new ChatTopLogprob(t.Token, t.Logprob)).ToList())
                ]
            }
        };

    private static Task<DecisionResponse> RunAsync(
        ScriptedChatProvider provider,
        AIProviderOptions options,
        DecisionRequest request,
        CancellationToken cancellationToken = default) =>
        new ChatReadoutDecision(provider, options, "readout-host", NullLogger.Instance)
            .ExecuteAsync(request, cancellationToken);

    private static AIProviderOptions Options() => new OpenAICompatibleProviderOptions();

    [Fact]
    public async Task BuiltInPrompt_Choice_IsLetteredInDeclarationOrder()
    {
        // Arrange
        var provider = new ScriptedChatProvider().Enqueue(
            Response("A", Ln06, [("A", Ln06), ("B", Ln03)]));

        // Act
        await RunAsync(provider, Options(), Request(("intent", ChoiceQuestion())));

        // Assert
        provider.Requests.Should().HaveCount(1);
        provider.Requests[0].Messages[0].Content.Should().Be(ChoicePrompt);
        provider.Requests[0].Logprobs.Should().BeTrue();
        provider.Requests[0].TopLogprobs.Should().Be(20);
        provider.Requests[0].Temperature.Should().Be(0);
        provider.Requests[0].Model.Should().Be("model-under-test");
    }

    [Fact]
    public async Task BuiltInPrompt_Noul_DescribesTrueAndFalseUnderYesNoTokens()
    {
        // Arrange
        var provider = new ScriptedChatProvider().Enqueue(
            Response("yes", Ln06, [("yes", Ln06), ("no", Ln02)]));

        // Act
        await RunAsync(provider, Options(), Request(("angry", NoulQuestion())));

        // Assert
        provider.Requests[0].Messages[0].Content.Should().Be(NoulPrompt);
    }

    [Fact]
    public async Task BuiltInPrompt_Score_ListsLevelsInOrder()
    {
        // Arrange
        var provider = new ScriptedChatProvider().Enqueue(
            Response("A", Ln06, [("A", Ln06), ("B", Ln03)]));

        // Act
        await RunAsync(provider, Options(), Request(("satisfaction", ScoreQuestion())));

        // Assert
        provider.Requests[0].Messages[0].Content.Should().Be(ScorePrompt);
    }

    [Fact]
    public async Task DeclaredTemplate_ReplacesPlaceholders()
    {
        // Arrange
        var provider = new ScriptedChatProvider().Enqueue(
            Response("A", Ln06, [("A", Ln06), ("B", Ln03)]));
        var options = new OpenAICompatibleProviderOptions
        {
            DecisionReadoutPromptTemplate = "S={state}|I={instructions}|O={options}"
        };

        // Act
        await RunAsync(provider, options, Request(("intent", ChoiceQuestion())));

        // Assert
        provider.Requests[0].Messages[0].Content.Should().Be(
            "S=A billing question.|I=Classify the ticket.|" +
            "O=A. refund: The customer wants a refund.\nB. technical: The customer needs technical help.");
    }

    [Fact]
    public async Task LowercaseLetterStyle_RendersAndMatchesLowercaseTokens()
    {
        // Arrange — the host publishes "a." / "b." and the response echoes "b."
        var provider = new ScriptedChatProvider().Enqueue(
            Response("b.", Ln07, [("a", Ln03), ("b.", Ln07)]));
        var options = new OpenAICompatibleProviderOptions
        {
            DecisionReadoutOptionTokenStyle = "lowercaseLetter"
        };

        // Act
        var response = await RunAsync(provider, options, Request(("intent", ChoiceQuestion())));

        // Assert
        provider.Requests[0].Messages[0].Content.Should().Contain("a. refund");
        provider.Requests[0].Messages[0].Content.Should().Contain("b. technical");
        response.Answers["intent"].Choice!.Selected.Should().Be("technical");
        response.Answers["intent"].Choice!.Confidence.Should().BeApproximately(0.7, 1e-9);
    }

    [Fact]
    public async Task LevelIndexStyle_RendersIndexesAndMatchesIndexTokens()
    {
        // Arrange
        var provider = new ScriptedChatProvider().Enqueue(
            Response("1", Ln07, [("0", Ln03), ("1", Ln07)]));
        var options = new OpenAICompatibleProviderOptions
        {
            DecisionReadoutOptionTokenStyle = "levelIndex"
        };

        // Act
        var response = await RunAsync(provider, options, Request(("intent", ChoiceQuestion())));

        // Assert
        provider.Requests[0].Messages[0].Content.Should().Contain("0. refund");
        provider.Requests[0].Messages[0].Content.Should().Contain("1. technical");
        response.Answers["intent"].Choice!.Selected.Should().Be("technical");
    }

    [Fact]
    public async Task Softmax_RenormalisesOverDeclaredOptionsOnly()
    {
        // Arrange — X carries a higher logprob but is not a declared option and must not count
        var provider = new ScriptedChatProvider().Enqueue(
            Response("A", Ln06, [("A", Ln06), ("B", Ln03), ("X", -0.1)]));

        // Act
        var response = await RunAsync(provider, Options(), Request(("intent", ChoiceQuestion())));

        // Assert
        var choice = response.Answers["intent"].Choice!;
        choice.Probabilities["refund"].Should().BeApproximately(2d / 3d, 1e-9);
        choice.Probabilities["technical"].Should().BeApproximately(1d / 3d, 1e-9);
        choice.Selected.Should().Be("refund");
        choice.Confidence.Should().BeApproximately(2d / 3d, 1e-9);
    }

    [Fact]
    public async Task TokenVariantsOfOneOption_CombineTheirProbabilities()
    {
        // Arrange — "A" and "A." are distinct tokens of the same option, so their probabilities
        // add: (0.6 + 0.3) / (0.6 + 0.3 + 0.7), not max-only 0.6 / (0.6 + 0.7)
        var provider = new ScriptedChatProvider().Enqueue(
            Response("A", Ln06, [("A.", Ln03), ("B", Ln07)]));

        // Act
        var response = await RunAsync(provider, Options(), Request(("intent", ChoiceQuestion())));

        // Assert
        var choice = response.Answers["intent"].Choice!;
        choice.Probabilities["refund"].Should().BeApproximately(0.9 / 1.6, 1e-9);
        choice.Probabilities["technical"].Should().BeApproximately(0.7 / 1.6, 1e-9);
        choice.Selected.Should().Be("refund");
        choice.Confidence.Should().BeApproximately(0.9 / 1.6, 1e-9);
    }

    [Fact]
    public async Task ChosenTokenRepeatedInTopLogprobs_IsCountedOnce()
    {
        // Arrange — the chosen token is also listed among the top logprobs, the common host shape;
        // its mass must enter the combined sum once, not twice
        var provider = new ScriptedChatProvider().Enqueue(
            Response("A", Ln06, [("A", Ln06), ("B", Ln07)]));

        // Act
        var response = await RunAsync(provider, Options(), Request(("intent", ChoiceQuestion())));

        // Assert — double-counting would give 2·0.6 / (2·0.6 + 0.7)
        var choice = response.Answers["intent"].Choice!;
        choice.Probabilities["refund"].Should().BeApproximately(0.6 / 1.3, 1e-9);
        choice.Probabilities["technical"].Should().BeApproximately(0.7 / 1.3, 1e-9);
    }

    [Fact]
    public async Task MissingDeclaredOption_ContributesZero()
    {
        // Arrange — "gamma" never appears in the reported distribution
        var question = new DecisionQuestion
        {
            Kind = EDecisionQuestionKind.Choice,
            Criteria = new Dictionary<string, string>
            {
                ["alpha"] = string.Empty,
                ["beta"] = string.Empty,
                ["gamma"] = string.Empty
            }
        };
        var provider = new ScriptedChatProvider().Enqueue(
            Response("A", Ln06, [("A", Ln06), ("B", Ln03)]));

        // Act
        var response = await RunAsync(provider, Options(), Request(("intent", question)));

        // Assert
        var choice = response.Answers["intent"].Choice!;
        choice.Probabilities["gamma"].Should().Be(0d);
        choice.Probabilities["alpha"].Should().BeApproximately(2d / 3d, 1e-9);
        choice.Probabilities["beta"].Should().BeApproximately(1d / 3d, 1e-9);
    }

    [Fact]
    public async Task Noul_IsYesOverYesPlusNo()
    {
        // Arrange
        var provider = new ScriptedChatProvider().Enqueue(
            Response("yes", Ln06, [("yes", Ln06), ("no", Ln02)]));

        // Act
        var response = await RunAsync(provider, Options(), Request(("angry", NoulQuestion())));

        // Assert
        response.Answers["angry"].Noul!.ProbabilityOfYes.Should().BeApproximately(0.75, 1e-9);
    }

    [Fact]
    public async Task Score_IsIndexWeightedExpectationWithLegendOrder()
    {
        // Arrange
        var provider = new ScriptedChatProvider().Enqueue(
            Response("C", Ln07, [("A", Ln01), ("B", Ln02), ("C", Ln07)]));

        // Act
        var response = await RunAsync(provider, Options(), Request(("satisfaction", ScoreQuestion())));

        // Assert
        var score = response.Answers["satisfaction"].Score!;
        score.Score.Should().BeApproximately(1.6, 1e-9);
        score.Confidence.Should().BeApproximately(0.7, 1e-9);
        score.Legend.Should().Equal("negative", "neutral", "positive");
        score.LevelProbabilities.Keys.Should().Equal("0", "1", "2");
        score.LevelProbabilities["0"].Should().BeApproximately(0.1, 1e-9);
        score.LevelProbabilities["1"].Should().BeApproximately(0.2, 1e-9);
        score.LevelProbabilities["2"].Should().BeApproximately(0.7, 1e-9);
    }

    [Fact]
    public async Task OneChatCallPerQuestion_InEnumerationOrder()
    {
        // Arrange
        var provider = new ScriptedChatProvider()
            .Enqueue(Response("A", Ln06, [("A", Ln06), ("B", Ln03)],
                id: "resp-1", model: "model-one",
                usage: new UsageInfo { PromptTokens = 10, CompletionTokens = 2, TotalTokens = 12 }))
            .Enqueue(Response("yes", Ln06, [("yes", Ln06), ("no", Ln02)],
                id: "resp-2", model: "model-two",
                usage: new UsageInfo { PromptTokens = 5, CompletionTokens = 1, TotalTokens = 6 }));

        // Act
        var response = await RunAsync(
            provider, Options(),
            Request(("intent", ChoiceQuestion()), ("angry", NoulQuestion())));

        // Assert
        provider.Requests.Should().HaveCount(2);
        provider.Requests[0].Messages[0].Content.Should().Be(ChoicePrompt);
        provider.Requests[1].Messages[0].Content.Should().Be(NoulPrompt);
        response.Id.Should().Be("resp-1");
        response.Model.Should().Be("model-one");
        response.Provider.Should().BeEmpty();
        response.Usage.PromptTokens.Should().Be(15);
        response.Usage.CompletionTokens.Should().Be(3);
        response.Usage.TotalTokens.Should().Be(18);
        response.Usage.Cost.Should().BeNull();
        response.Answers.Should().HaveCount(2);
    }

    [Fact]
    public async Task FirstFailure_AbortsWithoutPartialAnswers()
    {
        // Arrange — the second question fails; the third must never be sent
        var provider = new ScriptedChatProvider()
            .Enqueue(Response("A", Ln06, [("A", Ln06), ("B", Ln03)]))
            .EnqueueFailure(new AiException(AiErrorCodes.NoServer, "host failed"));

        // Act
        var act = () => RunAsync(
            provider, Options(),
            Request(
                ("first", ChoiceQuestion()),
                ("second", ChoiceQuestion()),
                ("third", ChoiceQuestion())));

        // Assert
        (await act.Should().ThrowAsync<AiException>())
            .Which.Code.Should().Be(AiErrorCodes.NoServer);
        provider.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task CancelledToken_IsHonouredBeforeAnyCall()
    {
        // Arrange
        var provider = new ScriptedChatProvider().Enqueue(
            Response("A", Ln06, [("A", Ln06), ("B", Ln03)]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var act = () => RunAsync(
            provider, Options(), Request(("intent", ChoiceQuestion())), cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResponseWithoutLogprobs_ThrowsDecisionReadoutUnavailable()
    {
        // Arrange — a host whose chat API carries no logprobs block
        var provider = new ScriptedChatProvider().Enqueue(
            new ChatCompletionResponse { Id = "r1", Model = "m1", Content = "A" });

        // Act
        var act = () => RunAsync(provider, Options(), Request(("intent", ChoiceQuestion())));

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.DecisionReadoutUnavailable);
        exception.Message.Should().Contain("readout-host");
    }

    [Fact]
    public async Task ResponseWithoutAnyDeclaredOption_ThrowsDecisionReadoutUnreadable()
    {
        // Arrange — the answer position reports only an unrelated token
        var provider = new ScriptedChatProvider().Enqueue(
            Response("Z", -0.1, [("Z", -0.1)]));

        // Act
        var act = () => RunAsync(provider, Options(), Request(("intent", ChoiceQuestion())));

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.DecisionReadoutUnreadable);
        exception.Message.Should().Contain("readout-host");
    }

    [Fact]
    public async Task ChoiceAboveTopLogprobs_ThrowsConfigurationErrorBeforeAnyCall()
    {
        // Arrange
        var options = new OpenAICompatibleProviderOptions { DecisionReadoutTopLogprobs = 2 };
        var question = new DecisionQuestion
        {
            Kind = EDecisionQuestionKind.Choice,
            Criteria = new Dictionary<string, string>
            {
                ["alpha"] = string.Empty,
                ["beta"] = string.Empty,
                ["gamma"] = string.Empty
            }
        };
        var provider = new ScriptedChatProvider();

        // Act
        var act = () => RunAsync(provider, options, Request(("intent", question)));

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.ConfigurationError);
        exception.Message.Should().Contain("readout-host").And.Contain("2");
        provider.Requests.Should().BeEmpty();
    }
}
