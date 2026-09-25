using AiCleverness.Abstractions;
using AiCleverness.Models;
using AiCleverness.Models.DecisionTree;
using AiCleverness.Runtime;
using AiCleverness.Runtime.Conversation;
using AiCleverness.Runtime.DecisionTree;

using AIProviderConnect.Models;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using ScraperTool.Services;
using ScraperTool.Services.UrlResearch;
using ScraperTool.Services.UrlResearch.Abstractions;
using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.UrlResearch.DecisionTree.Adapters;
using ScraperTool.Services.UrlResearch.DecisionTree.TemplateResolution;
using ScraperTool.Services.UrlResearch.Models;

namespace ScraperTool.Tests;

public class DecisionTreeResearchServiceTests
{
    private static DecisionTreeResult CreateResult(
        DecisionTreeOutcome outcome,
        string? verdict = null,
        IReadOnlyList<DecisionClassification>? classifications = null,
        string? error = null)
    {
        return new DecisionTreeResult(
            "exec-1",
            outcome == DecisionTreeOutcome.Terminal,
            verdict,
            outcome,
            classifications ?? Array.Empty<DecisionClassification>(),
            new ResourceUsage(),
            error);
    }

    [Fact]
    public void BuildReason_TerminalWithSkip_ProducesHumanReadableSummary()
    {
        var result = CreateResult(
            DecisionTreeOutcome.Terminal,
            verdict: "skip",
            classifications: new[]
            {
                new DecisionClassification("classify-1", "unknown", null, null, DateTimeOffset.UtcNow)
            });

        var reason = DecisionTreeResearchService.BuildReason(result);

        reason.Should().Contain("completed");
        reason.Should().Contain("verdict=skip");
        reason.Should().Contain("classification returned unknown");
        reason.Should().NotContain("Tree execution:");
        reason.Should().NotContain("ActionFailed");
    }

    [Fact]
    public void BuildReason_ActionFailed_LabelsAsFallback()
    {
        var result = CreateResult(DecisionTreeOutcome.ActionFailed, verdict: "skip");

        var reason = DecisionTreeResearchService.BuildReason(result);

        reason.Should().Contain("completed with fallback");
        reason.Should().Contain("verdict=skip");
    }

    [Fact]
    public void BuildReason_BudgetExhausted_LabelsCorrectly()
    {
        var result = CreateResult(DecisionTreeOutcome.BudgetExhausted);

        var reason = DecisionTreeResearchService.BuildReason(result);

        reason.Should().Contain("budget exceeded");
    }

    [Fact]
    public void BuildReason_Cancelled_LabelsCorrectly()
    {
        var result = CreateResult(DecisionTreeOutcome.Cancelled);

        var reason = DecisionTreeResearchService.BuildReason(result);

        reason.Should().Contain("cancelled");
    }

    [Fact]
    public void BuildReason_Unknown_LabelsAsInconclusive()
    {
        var result = CreateResult(DecisionTreeOutcome.Unknown);

        var reason = DecisionTreeResearchService.BuildReason(result);

        reason.Should().Contain("inconclusive");
    }

    [Fact]
    public void BuildReason_WithClassifications_ShowsLastAnswer()
    {
        var result = CreateResult(
            DecisionTreeOutcome.Terminal,
            verdict: "winner",
            classifications: new[]
            {
                new DecisionClassification("c-1", "api", null, null, DateTimeOffset.UtcNow),
                new DecisionClassification("c-2", "15", null, null, DateTimeOffset.UtcNow)
            });

        var reason = DecisionTreeResearchService.BuildReason(result);

        reason.Should().Contain("classification returned 15");
        reason.Should().NotContain("2 classification(s)");
    }

    [Fact]
    public void BuildReason_NoVerdict_OmitsVerdictSegment()
    {
        var result = CreateResult(DecisionTreeOutcome.Terminal, verdict: null);

        var reason = DecisionTreeResearchService.BuildReason(result);

        reason.Should().Contain("completed");
        reason.Should().NotContain("verdict=");
    }

    // ── Winner relevance gate ───────────────────────────────────────
    //
    // The tree answers with a page it could classify, not with the address that best serves the
    // field. A page documenting one endpoint lists that endpoint's token prices and classifies as
    // api_pricing, so the tree proposed it over the stored pricing page — the comparison these
    // tests cover is what stops that proposal reaching the user.

    private const string StoredPricingUrl = "https://www.test.example.com/pricing";

    private const string DocumentationEndpointUrl = "https://www.test.example.com/docs/v3/llms/chat-completions";

    [Fact]
    public async Task ResearchAsync_WinnerAddressSaysNothingAboutTheField_KeepsTheStoredValue()
    {
        var service = CreateResearchService(winnerUrl: DocumentationEndpointUrl);
        var context = CreateResearchContext(ValidationIssueCodes.PricingContentInvalid, StoredPricingUrl);

        var result = await service.ResearchAsync(context);

        result.SuggestedValue.Should().Be(StoredPricingUrl);
        result.Reason.Should().Contain("verdict=winner");
        result.Reason.Should().Contain("winner rejected as less relevant than the stored value");
        result.Steps.Should().Contain(step => step.Contains("Winner rejected"));
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ResearchAsync_WinnerAddressNamesTheField_ProposesTheWinner()
    {
        const string winnerUrl = "https://docs.test.example.com/developers/pricing";
        var service = CreateResearchService(winnerUrl);
        var context = CreateResearchContext(ValidationIssueCodes.PricingContentInvalid, StoredPricingUrl);

        var result = await service.ResearchAsync(context);

        result.SuggestedValue.Should().Be(winnerUrl);
        result.Reason.Should().NotContain("winner rejected");
    }

    [Fact]
    public async Task ResearchAsync_StoredAddressDidNotAnswer_ProposesTheWinner()
    {
        // A stored address that returned nothing has nothing to be compared against: any page
        // that answers is an improvement on it, however little its address says about the field.
        var service = CreateResearchService(winnerUrl: DocumentationEndpointUrl);
        var context = CreateResearchContext(ValidationIssueCodes.UrlNotFound, StoredPricingUrl);

        var result = await service.ResearchAsync(context);

        result.SuggestedValue.Should().Be(DocumentationEndpointUrl);
    }

    [Fact]
    public async Task ResearchAsync_WinnerIsTheRedirectTheValidatorFollowed_ProposesTheWinner()
    {
        // The validator watched the stored address answer somewhere else. That target is the
        // strongest signal the run holds, so the comparison gets no vote on it.
        var service = CreateResearchService(winnerUrl: DocumentationEndpointUrl);
        var context = CreateResearchContext(
            ValidationIssueCodes.PricingUrlRedirected,
            StoredPricingUrl,
            DocumentationEndpointUrl);

        var result = await service.ResearchAsync(context);

        result.SuggestedValue.Should().Be(DocumentationEndpointUrl);
    }

    /// <summary>
    /// Builds the service over the real <c>apiPricingUrl</c> tree with stub actions and predicates,
    /// so the run walks init → scan → fetch → classify → verify → win exactly as it does in
    /// production and the winner the stubs name is the one the gate has to judge.
    /// </summary>
    private static DecisionTreeResearchService CreateResearchService(string winnerUrl)
    {
        var actions = new IDecisionAction[]
        {
            new StubAction("initVerificationState"),
            new StubAction("recordCurrentFact"),
            new StubAction("scanProviderLinks"),
            new StubAction(
                "fetchNextCandidate",
                new Dictionary<string, string>
                {
                    ["lastFetchedUrl"] = winnerUrl,
                    ["lastFetchedContent"] = "Prices per token are listed on this page."
                }),
            new StubAction(
                "verifyReachable",
                new Dictionary<string, string>
                {
                    ["verifiedWinnerUrl"] = winnerUrl,
                    ["lastVerifySucceeded"] = "true"
                }),
            new StubAction("webSearch")
        };

        var predicates = new IDecisionPredicate[]
        {
            new StubPredicate("isSelfHostedProvider", false),
            new StubPredicate("hasCandidates", true),
            new StubPredicate("hasUntriedCandidates", true),
            new StubPredicate("lastVerifySucceeded", true)
        };

        // One response per classification the tree could ask for: the first answer sends the run
        // straight to its terminal node, the rest keep a longer walk from falling back to
        // "unknown" and cycling.
        var pipeline = new ScriptedPipeline();
        for (var call = 0; call < 5; call++)
        {
            pipeline.Enqueue(
                "{\"answer\":\"api_pricing\",\"observation\":\"the page lists token prices\"}");
        }

        var executor = new DecisionTreeExecutor(
            pipeline,
            new DefaultConversationManager(),
            new InMemoryExecutionJournal(),
            null,
            predicates,
            new DefaultDecisionLlmContextBuilder(),
            new DecisionTreeLoader(predicates));

        var profileStore = new Mock<IFieldResearchProfileStore>();
        profileStore
            .Setup(store => store.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldResearchProfile
            {
                FieldName = "apiPricingUrl",
                RelevanceTerms = ["price", "billing", "plans", "costs"],
                SiblingFields = ["documentationUrl"]
            });

        return new DecisionTreeResearchService(
            executor,
            actions,
            new DecisionTreeLoader(predicates),
            new DecisionTreeTemplateResolver(),
            NullLogger<DecisionTreeResearchService>.Instance,
            new ExecutionSessionTracker(),
            profileStore.Object);
    }

    private static ResearchContext CreateResearchContext(
        string issueCode,
        string currentValue,
        string? redirectTargetUrl = null) => new()
        {
            Field = "apiPricingUrl",
            CurrentValue = currentValue,
            ProviderId = "testprovider",
            RedirectTargetUrl = redirectTargetUrl,
            Provider = new ProviderDefinition
            {
                Id = "testprovider",
                DisplayName = "TestProvider",
                BaseUrl = "https://api.test.example.com/v1",
                Protocol = EProviderProtocol.OpenAICompatible
            },
            Research = new ProviderResearchMetadata
            {
                Website = "https://www.test.example.com",
                DocumentationUrl = DocumentationEndpointUrl
            },
            Issue = new ValidationIssue("testprovider.json", issueCode, "the stored page shows no prices")
            {
                Field = "apiPricingUrl",
                CurrentValue = currentValue,
                RedirectTargetUrl = redirectTargetUrl
            }
        };

    private sealed class StubAction : IDecisionAction
    {
        private readonly Dictionary<string, string> _properties;

        public StubAction(string key, Dictionary<string, string>? properties = null)
        {
            Key = key;
            _properties = properties ?? [];
        }

        public string Key { get; }

        public Task<DecisionActionResult> ExecuteAsync(
            DecisionActionContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new DecisionActionResult(null, _properties, DecisionActionStatus.Success));
    }

    private sealed class StubPredicate : IDecisionPredicate
    {
        private readonly bool _answer;

        public StubPredicate(string key, bool answer)
        {
            Key = key;
            _answer = answer;
        }

        public string Key { get; }

        public bool Evaluate(DecisionPredicateContext context) => _answer;
    }

    private sealed class ScriptedPipeline : ILlmCompletionPipeline
    {
        private readonly Queue<LlmResponse> _responses = new();

        public ScriptedPipeline Enqueue(string content)
        {
            _responses.Enqueue(new LlmResponse(content));
            return this;
        }

        public Task<LlmResponse> CompleteAsync(
            LlmCompletionRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(
            _responses.Count > 0
                ? _responses.Dequeue()
                : new LlmResponse("{\"answer\":\"unknown\",\"observation\":\"no scripted response\"}"));
    }
}
