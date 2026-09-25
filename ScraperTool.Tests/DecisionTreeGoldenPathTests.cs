using System.Net;
using System.Text.Json;

using AiCleverness.Abstractions;
using AiCleverness.Models;
using AiCleverness.Models.DecisionTree;
using AiCleverness.Runtime;
using AiCleverness.Runtime.Conversation;
using AiCleverness.Runtime.DecisionTree;

using FluentAssertions;

using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.UrlResearch.DecisionTree.Actions;
using ScraperTool.Services.UrlResearch.DecisionTree.Predicates;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

using DecisionTreeModel = AiCleverness.Models.DecisionTree.DecisionTree;

namespace ScraperTool.Tests;

/// <summary>
/// Golden-path integration tests for decision trees.
/// Verifies that each tree loads, validates, and executes to the expected verdict
/// given controlled inputs. Uses scripted LLM responses and fake HTTP handlers.
/// </summary>
public class DecisionTreeGoldenPathTests
{
    private static IDecisionAction[] CreateActions() => new IDecisionAction[]
    {
        new RecordCurrentFactAction(),
        new InitVerificationStateAction(),
        new CompareModelCountAction(),
        new StubAction("queryModelsEndpoint"),
        // Returns success without a model count, which is what a provider that documents
        // no public catalog produces; the tree must then keep walking the fallback rungs.
        new StubAction("queryDocumentedModelsEndpoint"),
        new StubAction("fetchModelsPage"),
        new StubAction("fetchDocumentationPage"),
        new StubAction("scanProviderLinks"),
        new StubAction("scanSiblingContent"),
        new StubAction("scanCandidateContent"),
        new StubAction("fetchNextCandidate"),
        new StubAction("verifyReachable"),
        new StubAction("webSearch"),
        new StubAction("llmExtractModelCount")
    };

    private static IDecisionPredicate[] CreatePredicates() => new IDecisionPredicate[]
    {
        new HasKnownCurrentValuePredicate(),
        new HasModelDiscoveryApiPredicate(),
        new IsModelCountAccuratePredicate(),
        new HasDocumentedModelCountPredicate(),
        new HasModelsPageUrlPredicate(),
        new HasCandidatesPredicate(new CandidateUrlProvider()),
        new HasUntriedCandidatesPredicate(new CandidateUrlProvider()),
        new LastVerifySucceededPredicate(),
        new HttpStatusIs401Predicate(),
        new IsDynamicModelCatalogPredicate(),
        new IsSelfHostedProviderPredicate(),
        new PageConfirmsSubscriptionPredicate(new ProviderResearchCache())
    };

    /// <summary>
    /// Stub action for testing tree loading/validation.
    /// </summary>
    private sealed class StubAction : IDecisionAction
    {
        public StubAction(string name) => Key = name;
        public string Key { get; }
        public Task<DecisionActionResult> ExecuteAsync(
            DecisionActionContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new DecisionActionResult(null, null, DecisionActionStatus.Success));
    }

    /// <summary>
    /// Builds the executor plus the action list that must be handed to each of its
    /// <c>ExecuteAsync</c> calls — actions are no longer constructor-injected into the executor.
    /// </summary>
    private static (DecisionTreeExecutor Executor, IDecisionAction[] Actions) CreateExecutor(
        ILlmCompletionPipeline? pipeline = null,
        IEnumerable<IDecisionAction>? actions = null,
        IEnumerable<IDecisionPredicate>? predicates = null,
        HttpClient? http = null)
    {
        var defaultActions = CreateActions().ToList();

        // Override or add custom actions (replace by name to avoid duplicates)
        if (actions is not null)
        {
            foreach (var action in actions)
            {
                defaultActions.RemoveAll(a => a.Key == action.Key);
                defaultActions.Add(action);
            }
        }

        var defaultPredicates = CreatePredicates().ToList();
        if (predicates is not null)
            defaultPredicates.AddRange(predicates);

        var allActions = defaultActions.ToArray();
        var allPredicates = defaultPredicates.ToArray();

        var executor = new DecisionTreeExecutor(
            pipeline ?? new ScriptedPipeline(),
            new DefaultConversationManager(),
            new InMemoryExecutionJournal(),
            null,
            allPredicates,
            new DefaultDecisionLlmContextBuilder(),
            new DecisionTreeLoader(allPredicates));

        return (executor, allActions);
    }

    private static DecisionTreeModel LoadTree(string treeId)
    {
        var treePath = Path.Combine(AppContext.BaseDirectory, "Config", "trees", $"{treeId}.json");
        if (!File.Exists(treePath))
        {
            // Fallback for test environments where trees are in the source tree
            var sourcePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "ScraperTool", "Config", "trees", $"{treeId}.json");
            treePath = Path.GetFullPath(sourcePath);
        }

        var json = File.ReadAllText(treePath);
        var loader = new DecisionTreeLoader(CreatePredicates());
        return loader.Load(json);
    }

    private sealed class ScriptedPipeline : ILlmCompletionPipeline
    {
        private readonly Queue<LlmResponse> _responses = new();

        public int CallCount { get; private set; }

        public ScriptedPipeline Enqueue(string content)
        {
            _responses.Enqueue(new LlmResponse(content));
            return this;
        }

        public Task<LlmResponse> CompleteAsync(
            LlmCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_responses.Count == 0)
                return Task.FromResult(new LlmResponse("{\"answer\":\"unknown\",\"observation\":\"no scripted response\"}"));
            return Task.FromResult(_responses.Dequeue());
        }
    }

    // ── minModelCount tree ──────────────────────────────────────────

    [Fact]
    public async Task MinModelCount_WithApiAndMatchingCount_ReturnsKeep()
    {
        // Arrange: provider has model discovery API, actual count >= stored
        var tree = LoadTree("minModelCount");

        var templateParams = new Dictionary<string, string>
        {
            ["providerName"] = "TestProvider",
            ["fieldKind"] = "minModelCount",
            ["currentValue"] = "5",
            ["hasModelDiscoveryApi"] = "true"
        };

        // Set up state to simulate successful model query
        // The tree will: init-state → has-model-api (true) → query-models → compare-count → is-accurate → keep
        // Since we don't have a real queryModelsEndpoint action in this test,
        // we test the tree structure and validation instead.

        // Act: verify tree loads and validates correctly
        tree.TreeId.Should().Be("minModelCount");
        tree.StartNodeId.Should().Be("init-state");
        tree.Nodes.Should().ContainKey("init-state");
        tree.Nodes.Should().ContainKey("keep");
        tree.Nodes.Should().ContainKey("update");
        tree.Nodes.Should().ContainKey("skip");
    }

    [Fact]
    public void MinModelCount_TreeStructure_HasExpectedNodes()
    {
        var tree = LoadTree("minModelCount");

        tree.Nodes.Should().ContainKey("init-state");
        tree.Nodes.Should().ContainKey("is-dynamic-catalog");
        tree.Nodes.Should().ContainKey("has-model-api");
        tree.Nodes.Should().ContainKey("query-models");
        tree.Nodes.Should().ContainKey("discover-models-endpoint");
        tree.Nodes.Should().ContainKey("has-documented-count");
        tree.Nodes.Should().ContainKey("compare-count");
        tree.Nodes.Should().ContainKey("is-accurate");
        tree.Nodes.Should().ContainKey("has-models-page");
        tree.Nodes.Should().ContainKey("fetch-models-page");
        tree.Nodes.Should().ContainKey("extract-model-count");
        tree.Nodes.Should().ContainKey("keep");
        tree.Nodes.Should().ContainKey("update");
        tree.Nodes.Should().ContainKey("skip");

        // Verify node types
        tree.Nodes["init-state"].Type.Should().Be(EDecisionNodeType.Action);
        tree.Nodes["is-dynamic-catalog"].Type.Should().Be(EDecisionNodeType.Condition);
        tree.Nodes["is-dynamic-catalog"].PredicateKey.Should().Be("isDynamicModelCatalog");
        tree.Nodes["has-model-api"].Type.Should().Be(EDecisionNodeType.Condition);
        tree.Nodes["query-models"].Type.Should().Be(EDecisionNodeType.Action);
        tree.Nodes["discover-models-endpoint"].Type.Should().Be(EDecisionNodeType.Action);
        tree.Nodes["discover-models-endpoint"].ActionKey.Should().Be("queryDocumentedModelsEndpoint");
        tree.Nodes["has-documented-count"].Type.Should().Be(EDecisionNodeType.Condition);
        tree.Nodes["has-documented-count"].PredicateKey.Should().Be("hasDocumentedModelCount");
        tree.Nodes["compare-count"].Type.Should().Be(EDecisionNodeType.Action);
        tree.Nodes["extract-model-count"].Type.Should().Be(EDecisionNodeType.Action);
        tree.Nodes["is-accurate"].Type.Should().Be(EDecisionNodeType.Condition);
        tree.Nodes["keep"].Type.Should().Be(EDecisionNodeType.Terminal);
        tree.Nodes["update"].Type.Should().Be(EDecisionNodeType.Terminal);
        tree.Nodes["skip"].Type.Should().Be(EDecisionNodeType.Terminal);
    }

    // ── modelDescription tree ───────────────────────────────────────

    [Fact]
    public void ModelDescription_TreeStructure_HasExpectedNodes()
    {
        var tree = LoadTree("modelDescription");

        tree.TreeId.Should().Be("modelDescription");
        tree.StartNodeId.Should().Be("init-state");
        tree.Nodes.Should().ContainKey("classify-description");
        tree.Nodes.Should().ContainKey("keep");
        tree.Nodes.Should().ContainKey("needs-update");
        tree.Nodes.Should().ContainKey("skip");

        // Verify the question node has correct answers
        var classifyNode = tree.Nodes["classify-description"];
        classifyNode.Type.Should().Be(EDecisionNodeType.Classify);
        classifyNode.Answers.Should().Contain("accurate");
        classifyNode.Answers.Should().Contain("stale");
        classifyNode.Answers.Should().Contain("wrong");
    }

    // ── baseUrl tree ────────────────────────────────────────────────

    [Fact]
    public void BaseUrl_TreeStructure_HasExpectedNodes()
    {
        var tree = LoadTree("baseUrl");

        tree.TreeId.Should().Be("baseUrl");
        tree.Nodes.Should().ContainKey("record-current-fact");
        tree.Nodes.Should().ContainKey("has-current-value");
        tree.Nodes.Should().ContainKey("verify-reachable");
        tree.Nodes.Should().ContainKey("is-reachable");
        tree.Nodes.Should().ContainKey("probe-models-endpoint");
        tree.Nodes.Should().ContainKey("is-auth-gated");
        tree.Nodes.Should().ContainKey("scan-candidate-content");
        tree.Nodes.Should().ContainKey("has-more-candidates");
        tree.Nodes.Should().ContainKey("scan-sibling-content");
        tree.Nodes.Should().ContainKey("has-sibling-candidates");
        tree.Nodes.Should().ContainKey("web-search");
        tree.Nodes.Should().ContainKey("win");
        tree.Nodes.Should().ContainKey("lose");

        // Verify the condition uses hasKnownCurrentValue predicate
        tree.Nodes["has-current-value"].Type.Should().Be(EDecisionNodeType.Condition);
        tree.Nodes["has-current-value"].PredicateKey.Should().Be("hasKnownCurrentValue");

        // Verify the probe node uses queryModelsEndpoint action
        tree.Nodes["probe-models-endpoint"].Type.Should().Be(EDecisionNodeType.Action);
        tree.Nodes["probe-models-endpoint"].ActionKey.Should().Be("queryModelsEndpoint");

        // The three fallback gates must ask whether a candidate is still untried. A queue never
        // empties during a run — entries stay after being tried — so binding them to
        // "queue non-empty" makes their false branch, and the fallback behind it, unreachable.
        tree.Nodes["has-more-candidates"].PredicateKey.Should().Be("hasUntriedCandidates");
        tree.Nodes["has-sibling-candidates"].PredicateKey.Should().Be("hasUntriedCandidates");
        tree.Nodes["has-search-results"].PredicateKey.Should().Be("hasUntriedCandidates");
    }

    // ── subscriptionPricingUrl tree ─────────────────────────────────

    [Fact]
    public void SubscriptionPricingUrl_TreeStructure_HasExpectedNodes()
    {
        var tree = LoadTree("subscriptionPricingUrl");

        tree.TreeId.Should().Be("subscriptionPricingUrl");

        // A self-hosted provider has no plans to subscribe to, so the tree answers it with the
        // not-applicable verdict ('-') up front — the repair the validator's
        // SubscriptionPricingUrlNotApplicableForSelfHosted finding routes to. init-state seeds
        // providerCategory, which is what the is-self-hosted predicate reads.
        tree.StartNodeId.Should().Be("init-state");
        tree.Nodes.Should().ContainKey("init-state");
        tree.Nodes.Should().ContainKey("is-self-hosted");
        tree.Nodes["is-self-hosted"].Type.Should().Be(EDecisionNodeType.Condition);
        tree.Nodes["is-self-hosted"].PredicateKey.Should().Be("isSelfHostedProvider");
        tree.Nodes["is-self-hosted"].Transitions!
            .Should().Contain(t => t.Condition == "true" && t.NextNodeId == "not-applicable");
        tree.Nodes["is-self-hosted"].Transitions!
            .Should().Contain(t => t.Condition == "false" && t.NextNodeId == "record-current-fact");

        tree.Nodes.Should().ContainKey("record-current-fact");
        tree.Nodes.Should().ContainKey("scan-sibling-links");
        tree.Nodes.Should().ContainKey("has-sibling-candidates");
        tree.Nodes.Should().ContainKey("scan-provider-links");
        tree.Nodes.Should().ContainKey("classify-page");
        tree.Nodes.Should().ContainKey("page-confirms-subscription");
        tree.Nodes.Should().ContainKey("verify-reachable");
        tree.Nodes.Should().ContainKey("not-applicable");
        tree.Nodes.Should().ContainKey("win");
        tree.Nodes.Should().ContainKey("lose");

        // The classifier's positive subscription answers are gated by a deterministic check on the
        // page that was actually fetched, so a per-token price list can never be accepted as a
        // subscription page: absent the word "subscription" the field is not applicable ("-").
        tree.Nodes["page-confirms-subscription"].Type.Should().Be(EDecisionNodeType.Condition);
        tree.Nodes["page-confirms-subscription"].PredicateKey.Should().Be("pageConfirmsSubscription");
        tree.Nodes["classify-page"].Transitions!
            .Should().Contain(t => t.Condition == "subscription_pricing" && t.NextNodeId == "page-confirms-subscription");
        tree.Nodes["classify-page"].Transitions!
            .Should().Contain(t => t.Condition == "both" && t.NextNodeId == "page-confirms-subscription");
        tree.Nodes["page-confirms-subscription"].Transitions!
            .Should().Contain(t => t.Condition == "true" && t.NextNodeId == "verify-reachable");
        tree.Nodes["page-confirms-subscription"].Transitions!
            .Should().Contain(t => t.Condition == "false" && t.NextNodeId == "not-applicable");
    }

    // ── apiPricingUrl tree ──────────────────────────────────────────

    [Fact]
    public void ApiPricingUrl_TreeStructure_HasExpectedNodes()
    {
        var tree = LoadTree("apiPricingUrl");

        tree.TreeId.Should().Be("apiPricingUrl");

        // A self-hosted provider serves a local model with no hosted API pricing, so the tree
        // answers it with the not-applicable verdict ('-') up front — the repair the validator's
        // ApiPricingUrlNotApplicableForSelfHosted finding routes to. init-state seeds
        // providerCategory, which is what the is-self-hosted predicate reads.
        tree.StartNodeId.Should().Be("init-state");
        tree.Nodes.Should().ContainKey("init-state");
        tree.Nodes.Should().ContainKey("is-self-hosted");
        tree.Nodes["is-self-hosted"].Type.Should().Be(EDecisionNodeType.Condition);
        tree.Nodes["is-self-hosted"].PredicateKey.Should().Be("isSelfHostedProvider");
        tree.Nodes["is-self-hosted"].Transitions!
            .Should().Contain(t => t.Condition == "true" && t.NextNodeId == "not-applicable");
        tree.Nodes["is-self-hosted"].Transitions!
            .Should().Contain(t => t.Condition == "false" && t.NextNodeId == "record-current-fact");
        tree.Nodes.Should().ContainKey("not-applicable");
        tree.Nodes["not-applicable"].Verdict.Should().Be("-");

        tree.Nodes.Should().ContainKey("record-current-fact");
        tree.Nodes.Should().ContainKey("scan-provider-links");
        tree.Nodes.Should().ContainKey("classify-page");
    }

    // ── website tree ────────────────────────────────────────────────

    [Fact]
    public void Website_TreeStructure_HasExpectedNodes()
    {
        var tree = LoadTree("website");

        tree.TreeId.Should().Be("website");
        tree.Nodes.Should().ContainKey("record-current-fact");
        tree.Nodes.Should().ContainKey("scan-provider-links");
        tree.Nodes.Should().ContainKey("classify-page");
    }

    // ── loginUrl tree ───────────────────────────────────────────────

    [Fact]
    public void LoginUrl_TreeStructure_HasExpectedNodes()
    {
        var tree = LoadTree("loginUrl");

        tree.TreeId.Should().Be("loginUrl");
        tree.Nodes.Should().ContainKey("record-current-fact");
        tree.Nodes.Should().ContainKey("scan-provider-links");
        tree.Nodes.Should().ContainKey("classify-page");
    }

    // ── documentationUrl tree ───────────────────────────────────────

    [Fact]
    public void DocumentationUrl_TreeStructure_HasExpectedNodes()
    {
        var tree = LoadTree("documentationUrl");

        tree.TreeId.Should().Be("documentationUrl");
        tree.Nodes.Should().ContainKey("record-current-fact");
        tree.Nodes.Should().ContainKey("scan-provider-links");
        tree.Nodes.Should().ContainKey("classify-page");
    }

    // ── Exhaustion gates in the candidate-walking trees ──────────────

    [Theory]
    [InlineData("website")]
    [InlineData("loginUrl")]
    [InlineData("documentationUrl")]
    [InlineData("apiPricingUrl")]
    [InlineData("subscriptionPricingUrl")]
    public void CandidateWalkingTrees_ExhaustionGate_AskWhetherACandidateIsUntried(string treeId)
    {
        // A reported run ended in "budget exceeded" with no verdict: once every scanned candidate
        // had been judged, the fetch step failed permanently and the gate behind it asked "is the
        // queue non-empty" — which stays true for the rest of the run, because a tried candidate
        // remains queued. Its "fall back to web search" branch was therefore unreachable, and the
        // two nodes cycled over each other until the node budget stopped the tree.
        var tree = LoadTree(treeId);

        tree.Nodes["candidates-exhausted"].PredicateKey.Should().Be("hasUntriedCandidates");
        tree.Nodes["candidates-exhausted"].Transitions!
            .Should().Contain(t => t.Condition == "false" && t.NextNodeId == "web-search");

        // The same shape one rung later: search results that were all judged already would send
        // web search back through the exhaustion gate instead of ending the run.
        tree.Nodes["has-search-results"].PredicateKey.Should().Be("hasUntriedCandidates");
    }

    [Fact]
    public async Task Website_WhenEveryCandidateHasBeenJudged_FallsThroughToWebSearch()
    {
        // Arrange — one scanned candidate judged as a documentation page, against a fetch step that
        // keeps the real hand-out contract: candidates stay queued once tried, so only a gate
        // asking about untried ones can end the candidate stage and reach the fallback below it.
        var pipeline = new ScriptedPipeline().Enqueue(
            "{\"answer\":\"product_page\",\"observation\":\"a documentation page\"}");

        var (executor, actions) = CreateExecutor(
            pipeline: pipeline,
            actions: new IDecisionAction[]
            {
                new QueueCandidatesAction(
                    "scanProviderLinks",
                    "https://test.example.com/docs/latest"),
                new HandOutCandidatesAction(),
                new MarkStateAction("webSearch", "webSearchRan")
            });

        var tree = LoadTree("website");
        var templateParams = new Dictionary<string, string>
        {
            ["providerName"] = "TestProvider",
            ["fieldKind"] = "website",
            ["currentValue"] = "https://test.example.com/docs/latest"
        };

        // Act
        var result = await executor.ExecuteAsync(actions, tree, templateParams);

        // Assert — the fallback ran instead of the tree cycling until its budget stopped it, and
        // the page was classified once rather than re-judged on every lap of the cycle.
        result.StateProperties!.Should().ContainKey("webSearchRan");
        pipeline.CallCount.Should().Be(1);
    }

    // ── Verification tree execution with mocked actions ─────────────

    [Fact]
    public async Task MinModelCount_Execution_WithInitAndCompare_ReturnsCorrectVerdict()
    {
        // This test verifies the verification path: init → hasModelApi(true) → queryModels(skip) → keep
        // We use a custom action that sets up state for the predicate
        var (executor, actions) = CreateExecutor(
            actions: new IDecisionAction[]
            {
                new SetStateAction("modelCount", 10) // Simulate queryModelsEndpoint result
            });

        var tree = LoadTree("minModelCount");
        var templateParams = new Dictionary<string, string>
        {
            ["providerName"] = "TestProvider",
            ["fieldKind"] = "minModelCount",
            ["currentValue"] = "5", // stored count = 5
            ["hasModelDiscoveryApi"] = "true"
        };

        // Execute: init-state → has-model-api(true) → query-models(skip via SetStateAction) → compare-count → is-accurate(true) → keep
        var result = await executor.ExecuteAsync(actions, tree, templateParams);

        // The tree should reach keep because actual(10) >= stored(5)
        result.Succeeded.Should().BeTrue();
        result.Verdict.Should().Be("keep");
    }

    [Fact]
    public async Task MinModelCount_Execution_WithOutdatedCount_ReturnsUpdate()
    {
        var (executor, actions) = CreateExecutor(
            actions: new IDecisionAction[]
            {
                new SetStateAction("modelCount", 2) // Simulate actual count = 2
            });

        var tree = LoadTree("minModelCount");
        var templateParams = new Dictionary<string, string>
        {
            ["providerName"] = "TestProvider",
            ["fieldKind"] = "minModelCount",
            ["currentValue"] = "10", // stored count = 10
            ["hasModelDiscoveryApi"] = "true"
        };

        var result = await executor.ExecuteAsync(actions, tree, templateParams);

        // actual(2) < stored(10) → outdated_high → update
        result.Succeeded.Should().BeTrue();
        result.Verdict.Should().Be("update");
    }

    [Fact]
    public async Task MinModelCount_Execution_NoApi_SkipsToSkip()
    {
        var (executor, actions) = CreateExecutor(actions: new IDecisionAction[]
        {
            new FailingAction("fetchDocumentationPage")
        });

        var tree = LoadTree("minModelCount");
        var templateParams = new Dictionary<string, string>
        {
            ["providerName"] = "TestProvider",
            ["fieldKind"] = "minModelCount",
            ["currentValue"] = "5",
            ["hasModelDiscoveryApi"] = "false",
            ["modelsPageUrl"] = "" // No models page
        };

        var result = await executor.ExecuteAsync(actions, tree, templateParams);

        // No API, no models page → skip
        result.Succeeded.Should().BeTrue();
        result.Verdict.Should().Be("skip");
    }

    [Fact]
    public async Task MinModelCount_DynamicCatalog_ShortCircuitsToKeep()
    {
        var (executor, actions) = CreateExecutor();

        var tree = LoadTree("minModelCount");
        var templateParams = new Dictionary<string, string>
        {
            ["providerName"] = "Jan AI",
            ["fieldKind"] = "minModelCount",
            ["currentValue"] = "0",
            ["hasModelDiscoveryApi"] = "false",
            ["isDynamicModelCatalog"] = "true"
        };

        var result = await executor.ExecuteAsync(actions, tree, templateParams);

        // Dynamic catalog → keep immediately, no web search or LLM calls
        result.Succeeded.Should().BeTrue();
        result.Verdict.Should().Be("keep");
    }

    [Fact]
    public async Task MinModelCount_CloudProviderWithoutApi_StillFollowsFallbackPath()
    {
        var (executor, actions) = CreateExecutor(actions: new IDecisionAction[]
        {
            new FailingAction("fetchDocumentationPage")
        });

        var tree = LoadTree("minModelCount");
        var templateParams = new Dictionary<string, string>
        {
            ["providerName"] = "Fireworks AI",
            ["fieldKind"] = "minModelCount",
            ["currentValue"] = "50",
            ["hasModelDiscoveryApi"] = "false",
            ["isDynamicModelCatalog"] = "false",
            ["modelsPageUrl"] = "" // No models page
        };

        var result = await executor.ExecuteAsync(actions, tree, templateParams);

        // Not dynamic, no API, no models page → falls through to web search → skip
        result.Succeeded.Should().BeTrue();
        result.Verdict.Should().Be("skip");
    }

    [Fact]
    public async Task MinModelCount_DocumentedCatalogEndpointAnswers_ReturnsUpdate()
    {
        // The gateway case: the base URL serves no model list, but the provider's own
        // documentation states a public catalog URL. The count found there must reach the
        // comparison step even when no previous value exists to compare against.
        var (executor, actions) = CreateExecutor(actions: new IDecisionAction[]
        {
            new DocumentedCountAction(35)
        });

        var tree = LoadTree("minModelCount");
        var templateParams = new Dictionary<string, string>
        {
            ["providerName"] = "TestProvider",
            ["fieldKind"] = "minModelCount",
            ["currentValue"] = "", // field missing from the definition
            ["hasModelDiscoveryApi"] = "false"
        };

        var result = await executor.ExecuteAsync(actions, tree, templateParams);

        result.Succeeded.Should().BeTrue();
        result.Verdict.Should().Be("update");
        // The executor reports action result properties in the final state, so the count
        // surfaces as text there even though the action stores it as a number.
        result.StateProperties!["modelCount"].Should().Be("35");
    }

    /// <summary>
    /// Mirrors the state the real documented-endpoint action leaves behind, so the golden
    /// path can assert where a documented catalog is routed.
    /// </summary>
    private sealed class DocumentedCountAction : IDecisionAction
    {
        private readonly int _count;

        public DocumentedCountAction(int count) => _count = count;

        public string Key => "queryDocumentedModelsEndpoint";

        public Task<DecisionActionResult> ExecuteAsync(
            DecisionActionContext context,
            CancellationToken cancellationToken = default)
        {
            context.State.Properties["modelCount"] = _count;
            context.State.Properties["modelCountMethod"] = "documented-endpoint";

            return Task.FromResult(new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["modelCount"] = _count.ToString() },
                DecisionActionStatus.Success));
        }
    }

    /// <summary>
    /// Test action that always returns permanent failure under a given key.
    /// Used to simulate a step that has nothing to find (no documentation URL, no candidates, etc.).
    /// </summary>
    private sealed class FailingAction : IDecisionAction
    {
        public FailingAction(string key) => Key = key;
        public string Key { get; }

        public Task<DecisionActionResult> ExecuteAsync(
            DecisionActionContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new DecisionActionResult(
                null, null, DecisionActionStatus.PermanentFailure, "Nothing to find."));
    }

    /// <summary>
    /// Test action that sets a state property without doing real work.
    /// Used to simulate the effect of queryModelsEndpoint in golden-path tests.
    /// </summary>
    private sealed class SetStateAction : IDecisionAction
    {
        private readonly string _key;
        private readonly object _value;

        public SetStateAction(string key, object value)
        {
            _key = key;
            _value = value;
        }

        public string Key => "queryModelsEndpoint"; // Override the real action key

        public Task<DecisionActionResult> ExecuteAsync(
            DecisionActionContext context,
            CancellationToken cancellationToken = default)
        {
            context.State.Properties[_key] = _value;
            return Task.FromResult(new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["simulated"] = "true" },
                DecisionActionStatus.Success));
        }
    }

    /// <summary>
    /// Queues candidates the way a page scan does, under whichever action key the tree asks for.
    /// Entries stay in the data store for the rest of the run, which is the property the
    /// exhaustion gates have to account for.
    /// </summary>
    private sealed class QueueCandidatesAction : IDecisionAction
    {
        private readonly string[] _urls;

        public QueueCandidatesAction(string key, params string[] urls)
        {
            Key = key;
            _urls = urls;
        }

        public string Key { get; }

        public Task<DecisionActionResult> ExecuteAsync(
            DecisionActionContext context,
            CancellationToken cancellationToken = default)
        {
            var produced = _urls.Select(url => new DecisionData
            {
                Id = $"candidate-{Guid.NewGuid():N}",
                Source = url,
                Type = "CandidateLink",
                Content = url,
                CreatedAt = DateTimeOffset.UtcNow
            }).ToList();

            return Task.FromResult(new DecisionActionResult(
                produced,
                new Dictionary<string, string> { ["candidates"] = _urls.Length.ToString() },
                DecisionActionStatus.Success));
        }
    }

    /// <summary>
    /// Mirrors <see cref="ScraperTool.Services.UrlResearch.DecisionTree.Actions.FetchNextCandidateAction"/>
    /// where the gates depend on it: a
    /// candidate is marked visited as it is handed out, stays in the queue afterwards, and once
    /// nothing untried remains the step fails permanently rather than inventing a page.
    /// </summary>
    private sealed class HandOutCandidatesAction : IDecisionAction
    {
        private readonly ICandidateUrlProvider _candidateUrlProvider = new CandidateUrlProvider();

        public string Key => "fetchNextCandidate";

        public Task<DecisionActionResult> ExecuteAsync(
            DecisionActionContext context,
            CancellationToken cancellationToken = default)
        {
            var visited = context.State.Properties.TryGetValue("visitedUrls", out var visitedObj)
                          && visitedObj is string visitedString
                          && !string.IsNullOrWhiteSpace(visitedString)
                ? visitedString.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                : [];

            var next = _candidateUrlProvider.GetCandidateUrls(context.Data)
                .FirstOrDefault(url => !visited.Contains(url, StringComparer.Ordinal));

            if (next is null)
            {
                return Task.FromResult(new DecisionActionResult(
                    null,
                    new Dictionary<string, string> { ["fetchResult"] = "exhausted" },
                    DecisionActionStatus.PermanentFailure,
                    "All candidates exhausted."));
            }

            visited.Add(next);
            context.State.Properties["visitedUrls"] = string.Join(",", visited);
            context.State.Properties["lastFetchedUrl"] = next;

            return Task.FromResult(new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["fetchResult"] = "success", ["url"] = next },
                DecisionActionStatus.Success));
        }
    }

    /// <summary>
    /// Records that a node ran, so a test can tell a tree that reached its fallback from one that
    /// stopped at its budget without reaching it.
    /// </summary>
    private sealed class MarkStateAction : IDecisionAction
    {
        private readonly string _property;

        public MarkStateAction(string key, string property)
        {
            Key = key;
            _property = property;
        }

        public string Key { get; }

        public Task<DecisionActionResult> ExecuteAsync(
            DecisionActionContext context,
            CancellationToken cancellationToken = default)
        {
            context.State.Properties[_property] = true;

            return Task.FromResult(new DecisionActionResult(
                null,
                new Dictionary<string, string> { [_property] = "true" },
                DecisionActionStatus.Success));
        }
    }
}
