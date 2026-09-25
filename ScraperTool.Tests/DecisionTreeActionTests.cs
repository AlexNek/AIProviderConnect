using System.Net;
using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using AiCleverness.Models.DecisionTree;

using FluentAssertions;

using Moq;

using ScraperTool.Models;
using ScraperTool.Services.UrlResearch;
using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.UrlResearch.DecisionTree.Actions;
using ScraperTool.Services.UrlResearch.DecisionTree.Formatting;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Tests;

public class DecisionTreeActionTests
{
    private static DecisionActionContext MakeActionContext(
        Dictionary<string, string>? templateParameters = null,
        Dictionary<string, object?>? properties = null)
    {
        var state = new DecisionState();
        if (properties is not null)
        {
            foreach (var (key, value) in properties)
            {
                state.Properties[key] = value;
            }
        }

        return new DecisionActionContext(
            "test-node",
            "test-exec",
            templateParameters ?? new Dictionary<string, string>(),
            state,
            new DataStore());
    }

    // ── RecordCurrentFactAction ─────────────────────────────────────

    [Fact]
    public async Task RecordCurrentFact_WithCurrentValue_StoresInStateAndProducesEvidence()
    {
        var action = new RecordCurrentFactAction();
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["currentValue"] = "https://test.example.com/pricing"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().HaveCount(1);
        result.ProducedData![0].Type.Should().Be("KnownFact");
        result.ProducedData[0].Content.Should().Be("https://test.example.com/pricing");
        ctx.State.Properties["knownCurrentValue"].Should().Be("https://test.example.com/pricing");
    }

    [Fact]
    public async Task RecordCurrentFact_WithoutCurrentValue_ReturnsSuccessWithNoData()
    {
        var action = new RecordCurrentFactAction();
        var ctx = MakeActionContext();

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().BeNull();
        ctx.State.Properties.Should().NotContainKey("knownCurrentValue");
    }

    [Fact]
    public async Task RecordCurrentFact_WithEmptyCurrentValue_ReturnsSuccessWithNoData()
    {
        var action = new RecordCurrentFactAction();
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["currentValue"] = ""
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().BeNull();
    }

    // ── QueryModelsEndpointAction ───────────────────────────────────

    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = new();

        public void EnqueueResponse(HttpStatusCode statusCode, string content)
        {
            _responses.Enqueue(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_responses.Count > 0)
                return Task.FromResult(_responses.Dequeue());
            throw new InvalidOperationException("No more responses queued");
        }
    }

    [Fact]
    public async Task QueryModelsEndpoint_Success_ParsesModelIds()
    {
        var handler = new FakeHttpMessageHandler();
        var json = JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new { id = "gpt-4" },
                new { id = "gpt-3.5-turbo" }
            }
        });
        handler.EnqueueResponse(HttpStatusCode.OK, json);

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var formatter = new StringListFormatter();
        var action = new QueryModelsEndpointAction(http, formatter);
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["baseUrl"] = "https://test.example.com",
            ["modelsEndpoint"] = "/v1/models"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().HaveCount(1);
        result.ProducedData![0].Type.Should().Be("ModelList");
        ctx.State.Properties["modelCount"].Should().Be(2);
        var modelIdsSummary = ctx.State.Properties["modelIds"] as string;
        modelIdsSummary.Should().NotBeNull();
        modelIdsSummary.Should().Contain("gpt-4");
        modelIdsSummary.Should().Contain("gpt-3.5-turbo");
    }

    [Fact]
    public async Task QueryModelsEndpoint_HttpError_ReturnsTransientFailure()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.InternalServerError, "error");

        var http = new HttpClient(handler);
        var formatter = new StringListFormatter();
        var action = new QueryModelsEndpointAction(http, formatter);
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["baseUrl"] = "https://test.example.com"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        ctx.State.Properties["lastHttpStatus"].Should().Be(500);
    }

    [Fact]
    public async Task QueryModelsEndpoint_Unauthorized_ReturnsPermanentFailure()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.Unauthorized, "unauthorized");

        var http = new HttpClient(handler);
        var formatter = new StringListFormatter();
        var action = new QueryModelsEndpointAction(http, formatter);
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["baseUrl"] = "https://test.example.com"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        ctx.State.Properties["lastHttpStatus"].Should().Be(401);
    }

    [Fact]
    public async Task QueryModelsEndpoint_NotFound_ReturnsPermanentFailure()
    {
        // A 404 from the probe is permanent — the endpoint definitively is not a
        // models API, so retrying the same URL only burns the node budget. This
        // previously returned TransientFailure and caused an endless retry loop.
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.NotFound, "not found");

        var http = new HttpClient(handler);
        var formatter = new StringListFormatter();
        var action = new QueryModelsEndpointAction(http, formatter);
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["baseUrl"] = "https://test.example.com"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        ctx.State.Properties["lastHttpStatus"].Should().Be(404);
    }

    [Fact]
    public async Task QueryModelsEndpoint_TooManyRequests_ReturnsTransientFailure()
    {
        // 429 is genuinely transient — a later retry may succeed.
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.TooManyRequests, "rate limited");

        var http = new HttpClient(handler);
        var formatter = new StringListFormatter();
        var action = new QueryModelsEndpointAction(http, formatter);
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["baseUrl"] = "https://test.example.com"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        ctx.State.Properties["lastHttpStatus"].Should().Be(429);
    }

    [Fact]
    public async Task QueryModelsEndpoint_MissingBaseUrl_ReturnsPermanentFailure()
    {
        var handler = new FakeHttpMessageHandler();
        var http = new HttpClient(handler);
        var formatter = new StringListFormatter();
        var action = new QueryModelsEndpointAction(http, formatter);
        var ctx = MakeActionContext();

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        result.Error.Should().Contain("baseUrl");
    }

    [Fact]
    public async Task QueryModelsEndpoint_NonJsonResponse_ReportsWhichUrlSaidWhat()
    {
        // A gateway answers a wrong models path with HTTP 200 and a plain-text error body.
        // The raw JsonException ("'<' is an invalid start of a value") named neither the
        // URL nor the body, so the run log could not be acted on.
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, "<!DOCTYPE html><html><body>Not JSON</body></html>");

        var http = new HttpClient(handler);
        var formatter = new StringListFormatter();
        var action = new QueryModelsEndpointAction(http, formatter);
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["baseUrl"] = "https://test.example.com"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        result.Properties!["queryResult"].Should().Be("not-json");
        result.Error.Should().Contain("https://test.example.com/v1/models");
        result.Error.Should().Contain("<!DOCTYPE html>");
        result.Error.Should().NotContain("invalid start of a value");
    }

    // ── FetchModelsPageAction ───────────────────────────────────────

    [Fact]
    public async Task FetchModelsPage_Success_StoresContentInState()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                "https://test.example.com/models",
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: "Model list: gpt-4, gpt-3.5",
                ErrorMessage: null,
                FinalUrl: "https://test.example.com/models"));

        var mockSelector = CreatePassthroughSelector();
        var action = new FetchModelsPageAction(
            mockFetcher.Object,
            new TextSummarizer(),
            mockSelector.Object);
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["modelsPageUrl"] = "https://test.example.com/models"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().HaveCount(1);
        result.ProducedData![0].Type.Should().Be("PageText");
        ctx.State.Properties["modelsPageContent"].Should().Be("Model list: gpt-4, gpt-3.5");
        ctx.State.Properties["modelsPageUrl"].Should().Be("https://test.example.com/models");
    }

    [Fact]
    public async Task FetchModelsPage_Failure_ReturnsTransientFailure()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                It.IsAny<string>(),
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: false,
                Content: string.Empty,
                ErrorMessage: "Network timeout",
                FinalUrl: "https://test.example.com/models"));

        var mockSelector = CreatePassthroughSelector();
        var action = new FetchModelsPageAction(
            mockFetcher.Object,
            new TextSummarizer(),
            mockSelector.Object);
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["modelsPageUrl"] = "https://test.example.com/models"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        result.Error.Should().Contain("Network timeout");
    }

    [Fact]
    public async Task FetchModelsPage_MissingUrl_ReturnsPermanentFailure()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        var mockSelector = CreatePassthroughSelector();
        var action = new FetchModelsPageAction(
            mockFetcher.Object,
            new TextSummarizer(),
            mockSelector.Object);
        var ctx = MakeActionContext();

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        result.Error.Should().Contain("modelsPageUrl");
    }

    // ── FetchDocumentationPageAction ────────────────────────────────

    [Fact]
    public async Task FetchDocumentationPage_Success_StoresContentInCacheAndState()
    {
        const string DocUrl = "https://test.example.com/docs/api/overview";
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                DocUrl, EContentFormat.Markdown,
                It.IsAny<int?>(), It.IsAny<ESanitizeLevel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: "# API Overview\nClaude Haiku, Sonnet, Opus",
                ErrorMessage: null,
                FinalUrl: DocUrl));

        var cache = new ProviderResearchCache();
        var action = new FetchDocumentationPageAction(mockFetcher.Object, cache);
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["siblingUrls"] = $"documentationUrl={DocUrl};apiPricingUrl=https://test.example.com/pricing"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["url"].Should().Be(DocUrl);
        ctx.State.Properties["lastFetchedUrl"].Should().Be(DocUrl);
        ctx.State.Properties["documentationPageAttempted"].Should().Be(true);
        cache.GetPageFetch(DocUrl).Should().NotBeNull();
        cache.GetPageFetch(DocUrl)!.Success.Should().BeTrue();
        result.ProducedData.Should().ContainSingle().Which.Type.Should().Be("PageText");
    }

    [Fact]
    public async Task FetchDocumentationPage_NoSiblingUrls_ReturnsPermanentFailure()
    {
        var action = new FetchDocumentationPageAction(
            new Mock<IWebContentFetcher>().Object,
            new ProviderResearchCache());
        var ctx = MakeActionContext();

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        result.Error.Should().Contain("sibling");
    }

    [Fact]
    public async Task FetchDocumentationPage_NoDocumentationUrlInSiblings_ReturnsPermanentFailure()
    {
        var action = new FetchDocumentationPageAction(
            new Mock<IWebContentFetcher>().Object,
            new ProviderResearchCache());
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["siblingUrls"] = "apiPricingUrl=https://test.example.com/pricing"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        result.Error.Should().Contain("documentationUrl");
    }

    [Fact]
    public async Task FetchDocumentationPage_AlreadyAttempted_ReturnsPermanentFailureWithoutRefetching()
    {
        const string DocUrl = "https://test.example.com/docs";
        var mockFetcher = new Mock<IWebContentFetcher>();
        var cache = new ProviderResearchCache();
        var action = new FetchDocumentationPageAction(mockFetcher.Object, cache);
        var ctx = MakeActionContext(
            new Dictionary<string, string> { ["siblingUrls"] = $"documentationUrl={DocUrl}" },
            new Dictionary<string, object?> { ["documentationPageAttempted"] = true });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(), It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FetchDocumentationPage_FetchFails_ReturnsTransientFailure()
    {
        const string DocUrl = "https://test.example.com/docs";
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                DocUrl, EContentFormat.Markdown,
                It.IsAny<int?>(), It.IsAny<ESanitizeLevel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: false, Content: string.Empty,
                ErrorMessage: "Network timeout", FinalUrl: DocUrl));

        var action = new FetchDocumentationPageAction(mockFetcher.Object, new ProviderResearchCache());
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["siblingUrls"] = $"documentationUrl={DocUrl}"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        result.Error.Should().Contain("Network timeout");
    }

    [Fact]
    public void ExtractDocumentationUrl_FindsDocumentationUrlAmongSiblings()
    {
        FetchDocumentationPageAction.ExtractDocumentationUrl(
            "apiPricingUrl=https://test.example.com/pricing;documentationUrl=https://test.example.com/docs;website=https://test.example.com")
            .Should().Be("https://test.example.com/docs");
    }

    [Fact]
    public void ExtractDocumentationUrl_ReturnsNullWhenMissing()
    {
        FetchDocumentationPageAction.ExtractDocumentationUrl(
            "apiPricingUrl=https://test.example.com/pricing;website=https://test.example.com")
            .Should().BeNull();
    }

    // ── InitVerificationStateAction ─────────────────────────────────

    [Fact]
    public async Task InitVerificationState_WithApiTrue_SetsBoolInState()
    {
        var action = new InitVerificationStateAction();
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["hasModelDiscoveryApi"] = "true",
            ["modelsPageUrl"] = "https://test.example.com/models"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["hasModelDiscoveryApi"].Should().Be(true);
        ctx.State.Properties["hasModelsPageUrl"].Should().Be(true);
    }

    [Fact]
    public async Task InitVerificationState_WithApiFalse_SetsFalseInState()
    {
        var action = new InitVerificationStateAction();
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["hasModelDiscoveryApi"] = "false"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["hasModelDiscoveryApi"].Should().Be(false);
        ctx.State.Properties["hasModelsPageUrl"].Should().Be(false);
    }

    [Fact]
    public async Task InitVerificationState_WithNoParameters_SetsAllFalse()
    {
        var action = new InitVerificationStateAction();
        var ctx = MakeActionContext();

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["hasModelDiscoveryApi"].Should().Be(false);
        ctx.State.Properties["hasModelsPageUrl"].Should().Be(false);
    }

    // ── CompareModelCountAction ─────────────────────────────────────

    [Fact]
    public async Task CompareModelCount_ActualGeStored_ReturnsMatches()
    {
        var action = new CompareModelCountAction();
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["currentValue"] = "5"
            },
            properties: new Dictionary<string, object?>
            {
                ["modelCount"] = 7
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["modelCountComparison"].Should().Be("matches");
        ctx.State.Properties["actualModelCount"].Should().Be(7);
        ctx.State.Properties["storedModelCount"].Should().Be(5);
        result.ProducedData.Should().HaveCount(1);
        result.ProducedData![0].Type.Should().Be("CountComparison");
    }

    [Fact]
    public async Task CompareModelCount_ActualLessThanStored_ReturnsOutdatedHigh()
    {
        var action = new CompareModelCountAction();
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["currentValue"] = "10"
            },
            properties: new Dictionary<string, object?>
            {
                ["modelCount"] = 3
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["modelCountComparison"].Should().Be("outdated_high");
    }

    [Fact]
    public async Task CompareModelCount_NoModelCountInState_ReturnsCannotVerify()
    {
        var action = new CompareModelCountAction();
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["currentValue"] = "5"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["modelCountComparison"].Should().Be("cannot_verify");
    }

    [Fact]
    public async Task CompareModelCount_NoStoredValue_ReturnsCannotVerify()
    {
        var action = new CompareModelCountAction();
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["modelCount"] = 5
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["modelCountComparison"].Should().Be("cannot_verify");
    }

    // ── LlmExtractModelCountAction ──────────────────────────────────

    [Fact]
    public async Task LlmExtractModelCount_NonJsonReply_DoesNotFabricateCount()
    {
        // A free-text reply used to be mined for its first digits, which produced
        // invented counts (e.g. 1) that were then suggested verbatim as minModelCount.
        var action = CreateLlmExtractAction("The list mentions 1 model family and several endpoints.");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://docs.test.example.com/models"
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = "https://docs.test.example.com/models",
            Type = "PageText",
            Content = "Reference documentation for the endpoint. No catalog listing here.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        result.Properties!["extractResult"].Should().Be("not-found");
        ctx.State.Properties.Should().NotContainKey("modelCount");
    }

    [Fact]
    public async Task LlmExtractModelCount_FencedJsonReply_ExtractsQuotedCount()
    {
        // The page states no count in a form the deterministic patterns recognise, so the
        // LLM path is reached — and its fenced JSON answer must still be parsed.
        var action = CreateLlmExtractAction(
            "```json\n{\"count\": 12, \"quote\": \"twelve (12) distinct entries\"}\n```");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://docs.test.example.com/models"
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = "https://docs.test.example.com/models",
            Type = "PageText",
            Content = "Our catalogue currently ships twelve (12) distinct entries.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["modelCount"].Should().Be(12);
        ctx.State.Properties["modelCountQuote"].Should().Be("twelve (12) distinct entries");
        result.Properties!["method"].Should().Be("llm");
    }

    [Fact]
    public async Task LlmExtractModelCount_UsesCurrentPage_NotAnEarlierCandidate()
    {
        // Evidence from a previously fetched candidate must not be attributed to the
        // page the tree is currently examining.
        var action = CreateLlmExtractAction("not a json reply");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://docs.test.example.com/current"
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = "https://docs.test.example.com/earlier",
            Type = "PageText",
            Content = "Earlier candidate offering 40 models to every visitor.",
            CreatedAt = DateTimeOffset.UtcNow
        });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-2",
            Source = "https://docs.test.example.com/current",
            Type = "PageText",
            Content = "Current candidate with no catalog information at all.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        ctx.State.Properties.Should().NotContainKey("modelCount");
    }

    [Fact]
    public async Task LlmExtractModelCount_ModelNameWithKSuffix_IsNotReadAsThousands()
    {
        // A model name such as "gpt-4-turbo-128k" or a "128k context window" note used to
        // be read as 128000 available models. No digit pattern is applied to page prose
        // any more, so text like this can only yield a count if the LLM quotes one.
        var action = CreateLlmExtractAction("not a json reply");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://docs.test.example.com/models"
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = "https://docs.test.example.com/models",
            Type = "PageText",
            Content = "Available: gpt-4-turbo-128k with a 128k context window.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        ctx.State.Properties.Should().NotContainKey("modelCount");
    }

    [Fact]
    public async Task LlmExtractModelCount_ThousandsSuffixInProse_IsReadByTheLlm()
    {
        // "12k" is a format a fixed pattern had to be taught; the reader takes it as written.
        var action = CreateLlmExtractAction(
            "{\"count\": 12000, \"quote\": \"12k models\"}");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://docs.test.example.com/catalog"
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = "https://docs.test.example.com/catalog",
            Type = "PageText",
            Content = "Browse 12k models in the catalog.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["modelCount"].Should().Be(12000);
        result.Properties!["method"].Should().Be("llm");
    }

    [Fact]
    public async Task LlmExtractModelCount_LlmReportsStalePage_DoesNotStoreCount()
    {
        // Freshness is the reader's judgement, not a date-pattern scan: the page states a
        // count, but the catalog is flagged as outdated, so nothing may be stored.
        var action = CreateLlmExtractAction(
            "{\"count\": 40, \"quote\": \"40 models\", \"stale\": true}");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://docs.test.example.com/archive"
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = "https://docs.test.example.com/archive",
            Type = "PageText",
            Content = "This archive lists 40 models.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        result.Properties!["extractResult"].Should().Be("data-stale");
        ctx.State.Properties.Should().NotContainKey("modelCount");
    }

    [Fact]
    public async Task LlmExtractModelCount_CountsCatalogLinks_FromTheWholeCachedPage()
    {
        // The bounded excerpt the classifier sees holds only 2 of the 30 model links.
        // The count must come from the whole page, not from the excerpt.
        const string pageUrl = "https://docs.test.example.com/api/docs/models/all";
        var fullMarkdown = string.Join(
            "\n",
            Enumerable.Range(1, 30).Select(i => $"- [Model {i}](/api/docs/models/model-{i})"));

        var cache = new ProviderResearchCache();
        cache.SetPageFetch(pageUrl, new PageFetchCacheEntry
        {
            Success = true,
            MarkdownContent = fullMarkdown,
            FinalUrl = pageUrl
        });

        var action = CreateLlmExtractAction("not a json reply", cache);
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = pageUrl
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = pageUrl,
            Type = "PageText",
            Content = "- [Model 1](/api/docs/models/model-1)\n- [Model 2](/api/docs/models/model-2)",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["modelCount"].Should().Be(30);
        result.Properties!["method"].Should().Be("catalog-links");
        result.Properties["sourceUrl"].Should().Be(pageUrl);
        ctx.State.Properties["modelCountSourceUrl"].Should().Be(pageUrl);
    }

    [Fact]
    public async Task LlmExtractModelCount_PrefersListedEntries_OverAStatedPhrase()
    {
        // A page that both claims a number and lists its catalog is counted from the
        // listing: the claim may be marketing or stale, the links are what the page shows.
        const string pageUrl = "https://test.example.com/models";
        var action = CreateLlmExtractAction("not a json reply");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = pageUrl
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = pageUrl,
            Type = "PageText",
            Content = "Over 100 models available.\n"
                + "- [Alpha](/models/alpha)\n- [Beta](/models/beta)\n- [Gamma](/models/gamma)",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["modelCount"].Should().Be(3);
        result.Properties!["method"].Should().Be("catalog-links");
    }

    [Fact]
    public async Task LlmExtractModelCount_CountCertifiedByEnumeration_IsAccepted()
    {
        // The page states no number at all, so the only honest answer is one backed by the
        // entries themselves: every name the model counted is on the page, and there are
        // exactly as many names as the count claims.
        const string pageUrl = "https://test.example.com/lineup";
        var action = CreateLlmExtractAction(
            "{\"count\": 3, \"quote\": \"Our lineup\", \"modelNames\": [\"Alpha\", \"Beta\", \"Gamma\"]}");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = pageUrl
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = pageUrl,
            Type = "PageText",
            Content = "Our lineup\nAlpha\nBeta\nGamma",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["modelCount"].Should().Be(3);
        result.Properties!["method"].Should().Be("llm");
    }

    [Fact]
    public async Task LlmExtractModelCount_InventedCountBehindARealQuote_IsRejected()
    {
        // The quote is verbatim page text, so it passes a quote-exists check on its own —
        // but the page never states 250 and the model named nothing it counted.
        const string pageUrl = "https://test.example.com/docs";
        var action = CreateLlmExtractAction(
            "{\"count\": 250, \"quote\": \"Reference documentation for the endpoint\"}");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = pageUrl
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = pageUrl,
            Type = "PageText",
            Content = "Reference documentation for the endpoint. Models are listed in the console.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        result.Properties!["extractResult"].Should().Be("not-found");
        ctx.State.Properties.Should().NotContainKey("modelCount");
    }

    [Fact]
    public async Task LlmExtractModelCount_EnumeratedNameMissingFromThePage_IsRejected()
    {
        // Two of the three names are real; one is invented, so the total is unverifiable.
        const string pageUrl = "https://test.example.com/lineup";
        var action = CreateLlmExtractAction(
            "{\"count\": 3, \"quote\": \"Our lineup\", \"modelNames\": [\"Alpha\", \"Beta\", \"Delta\"]}");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = pageUrl
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = pageUrl,
            Type = "PageText",
            Content = "Our lineup\nAlpha\nBeta\nGamma",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        ctx.State.Properties.Should().NotContainKey("modelCount");
    }

    [Fact]
    public async Task LlmExtractModelCount_EnumerationShorterThanTheCount_IsRejected()
    {
        // Every name is real, but the claimed total exceeds what the model could point to.
        const string pageUrl = "https://test.example.com/lineup";
        var action = CreateLlmExtractAction(
            "{\"count\": 5, \"quote\": \"Our lineup\", \"modelNames\": [\"Alpha\", \"Beta\", \"Gamma\"]}");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = pageUrl
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = pageUrl,
            Type = "PageText",
            Content = "Our lineup\nAlpha\nBeta\nGamma",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        ctx.State.Properties.Should().NotContainKey("modelCount");
    }

    [Fact]
    public async Task LlmExtractModelCount_CountThatIsASubstringOfTheQuotedNumber_IsRejected()
    {
        // "250 models" does not attest a count of 25: numbers are read as whole tokens.
        const string pageUrl = "https://test.example.com/about";
        var action = CreateLlmExtractAction(
            "{\"count\": 25, \"quote\": \"250 models\"}");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = pageUrl
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = pageUrl,
            Type = "PageText",
            Content = "Choose from 250 models across every modality.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        ctx.State.Properties.Should().NotContainKey("modelCount");
    }

    [Fact]
    public async Task LlmExtractModelCount_ThousandsSeparatedQuote_AttestsTheCount()
    {
        const string pageUrl = "https://test.example.com/about";
        var action = CreateLlmExtractAction(
            "{\"count\": 1200, \"quote\": \"1,200 models\"}");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = pageUrl
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = pageUrl,
            Type = "PageText",
            Content = "We list 1,200 models today.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["modelCount"].Should().Be(1200);
    }

    [Fact]
    public async Task LlmExtractModelCount_ModelVersionInQuote_DoesNotAttestACount()
    {
        // "gpt-3.5" is a name, not the number 3.5 — so it cannot attest a count of 4.
        const string pageUrl = "https://test.example.com/docs";
        var action = CreateLlmExtractAction(
            "{\"count\": 4, \"quote\": \"gpt-3.5\"}");
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = pageUrl
            });
        ctx.Data.Add(new DecisionData
        {
            Id = "page-1",
            Source = pageUrl,
            Type = "PageText",
            Content = "See gpt-3.5 for details.",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
        ctx.State.Properties.Should().NotContainKey("modelCount");
    }

    private static LlmExtractModelCountAction CreateLlmExtractAction(
        string replyContent,
        ProviderResearchCache? cache = null)
    {
        var mockProvider = new Mock<IAIProvider>();
        mockProvider
            .Setup(p => p.ChatAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatCompletionResponse { Content = replyContent });

        var mockFactory = new Mock<IAIProviderFactory>();
        mockFactory
            .Setup(f => f.GetProvider(It.IsAny<string>()))
            .Returns(mockProvider.Object);

        var settings = new AppSettings
        {
            ApiKey = "fake-api-key",
            SelectedProviderId = "test-provider",
            PrimaryModel = "test-model"
        };

        return new LlmExtractModelCountAction(
            mockFactory.Object,
            settings,
            cache ?? new ProviderResearchCache(),
            new ModelCatalogCounter());
    }

    private static Mock<ICandidateRegionContentSelector> CreatePassthroughSelector()
    {
        var mock = new Mock<ICandidateRegionContentSelector>();
        mock.Setup(s => s.Select(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Uri?>()))
            .Returns<string, string?, Uri?>((md, _, _) => new CandidateRegionContentResult(md, 0));
        return mock;
    }

    /// <summary>
    /// Queues a candidate page on the context, in discovery order.
    /// </summary>
    private static void AddCandidateLink(DecisionActionContext context, string id, string url, int order)
    {
        context.Data.Add(new DecisionData
        {
            Id = id,
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = url,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(order)
        });
    }

    /// <summary>
    /// A fetcher that serves the given page contents and reports every other page as missing.
    /// </summary>
    private static Mock<IWebContentFetcher> CreatePageFetcher(params (string Url, string Content)[] pages)
    {
        var served = pages.ToDictionary(p => p.Url, p => p.Content, StringComparer.Ordinal);

        var mock = new Mock<IWebContentFetcher>();
        mock.Setup(f => f.FetchAsAsync(
                It.IsAny<string>(),
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string url, EContentFormat format, int? maxChars, ESanitizeLevel level, CancellationToken ct) =>
                served.TryGetValue(url, out var content)
                    ? new WebContent(Success: true, Content: content, ErrorMessage: null, FinalUrl: url)
                    // A page nobody queued must not be silently readable: fetching a candidate
                    // the queue does not hold would hide an index that advanced too far.
                    : new WebContent(
                        Success: false,
                        Content: string.Empty,
                        ErrorMessage: "HTTP 404",
                        FinalUrl: url));

        return mock;
    }

    // ── VerifyReachableAction ───────────────────────────────────────

    [Fact]
    public async Task VerifyReachable_Success_SetsVerifiedWinnerUrl()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                "https://test.example.com/pricing",
                EContentFormat.Markdown,
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: "Pricing page content",
                ErrorMessage: null,
                FinalUrl: "https://test.example.com/pricing"));

        var action = new VerifyReachableAction(mockFetcher.Object);
        var ctx = MakeActionContext(properties: new Dictionary<string, object?>
        {
            ["lastFetchedUrl"] = "https://test.example.com/pricing"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["lastVerifySucceeded"].Should().Be(true);
        ctx.State.Properties["verifiedWinnerUrl"].Should().Be("https://test.example.com/pricing");
    }

    [Fact]
    public async Task VerifyReachable_Failure_DoesNotSetVerifiedWinnerUrl()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                "https://test.example.com/broken",
                EContentFormat.Markdown,
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: false,
                Content: string.Empty,
                ErrorMessage: "HTTP 404",
                FinalUrl: "https://test.example.com/broken"));

        var action = new VerifyReachableAction(mockFetcher.Object);
        var ctx = MakeActionContext(properties: new Dictionary<string, object?>
        {
            ["lastFetchedUrl"] = "https://test.example.com/broken"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        ctx.State.Properties["lastVerifySucceeded"].Should().Be(false);
        ctx.State.Properties.Should().NotContainKey("verifiedWinnerUrl");
    }

    [Fact]
    public async Task VerifyReachable_NoUrl_ReturnsPermanentFailureWithoutVerifiedWinner()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        var action = new VerifyReachableAction(mockFetcher.Object);
        var ctx = MakeActionContext();

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        ctx.State.Properties["lastVerifySucceeded"].Should().Be(false);
        ctx.State.Properties.Should().NotContainKey("verifiedWinnerUrl");
    }

    [Fact]
    public async Task VerifyReachable_NoUrl_ResetsStaleLastVerifySucceeded()
    {
        // Simulate a stale "true" from a previous iteration.
        var mockFetcher = new Mock<IWebContentFetcher>();
        var action = new VerifyReachableAction(mockFetcher.Object);
        var ctx = MakeActionContext(properties: new Dictionary<string, object?>
        {
            ["lastVerifySucceeded"] = true
        });

        var result = await action.ExecuteAsync(ctx);

        // Even though lastVerifySucceeded was true before the call,
        // the action must reset it to false so the is-reachable
        // condition doesn't return a stale positive.
        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        ctx.State.Properties["lastVerifySucceeded"].Should().Be(false);
    }

    [Fact]
    public async Task VerifyReachable_FallsBackToKnownCurrentValue_WhenNoLastFetchedUrl()
    {
        // The baseUrl tree goes directly to verify-reachable after
        // record-current-fact, without fetching a candidate. In this
        // path lastFetchedUrl is not set but knownCurrentValue is.
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                "https://api.example.com/v1",
                EContentFormat.Markdown,
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: "API endpoint content",
                ErrorMessage: null,
                FinalUrl: "https://api.example.com/v1"));

        var action = new VerifyReachableAction(mockFetcher.Object);
        var ctx = MakeActionContext(properties: new Dictionary<string, object?>
        {
            ["knownCurrentValue"] = "https://api.example.com/v1"
        });

        var result = await action.ExecuteAsync(ctx);

        // The action should fall back to knownCurrentValue and verify it.
        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["lastVerifySucceeded"].Should().Be(true);
        ctx.State.Properties["verifiedWinnerUrl"].Should().Be("https://api.example.com/v1");
    }

    [Fact]
    public async Task VerifyReachable_ApiLikeUrl_ForBaseUrlField_DefersToProbeWithoutFetch()
    {
        // For the baseUrl field, an API-like URL must NOT be fetched as a web page
        // (its root typically 404s for a valid API host). It is treated as reachable
        // so the flow proceeds to probe-models-endpoint, the authoritative test.
        var mockFetcher = new Mock<IWebContentFetcher>();
        var action = new VerifyReachableAction(mockFetcher.Object);
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string> { ["fieldKind"] = "baseUrl" },
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://api.test.example.com"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["lastVerifySucceeded"].Should().Be(true);
        ctx.State.Properties["verifiedWinnerUrl"].Should().Be("https://api.test.example.com");
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(), It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task VerifyReachable_ApiLikeUrl_ForNonBaseUrlField_StillFetches()
    {
        // The deferral is scoped to the baseUrl field only; other trees still
        // fetch and verify the page normally.
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                "https://api.test.example.com",
                EContentFormat.Markdown,
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: false,
                Content: string.Empty,
                ErrorMessage: "HTTP 404",
                FinalUrl: "https://api.test.example.com"));

        var action = new VerifyReachableAction(mockFetcher.Object);
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string> { ["fieldKind"] = "subscriptionPricingUrl" },
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://api.test.example.com"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        ctx.State.Properties["lastVerifySucceeded"].Should().Be(false);
    }

    [Fact]
    public async Task VerifyReachable_PageCandidate_ForBaseUrlField_DefersToProbeWithoutFetch()
    {
        // A site page says nothing about whether the host serves an API, and rendering it costs
        // seconds the candidate loop cannot afford. It is passed to the probe unscored — and not
        // recorded as the winner, because a page address is never this field's answer.
        var mockFetcher = new Mock<IWebContentFetcher>();
        var action = new VerifyReachableAction(mockFetcher.Object);
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string> { ["fieldKind"] = "baseUrl" },
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://test.example.com/pricing"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["verifyResult"].Should().Be("page-deferred-to-probe");
        ctx.State.Properties["lastVerifySucceeded"].Should().Be(true);
        ctx.State.Properties.Should().NotContainKey("verifiedWinnerUrl");
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(), It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── FetchNextCandidateAction — 404 permanent failure ────────────

    [Fact]
    public async Task FetchNextCandidate_Http404_ReturnsPermanentFailure()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                It.IsAny<string>(),
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: false,
                Content: string.Empty,
                ErrorMessage: "HTTP 404",
                FinalUrl: "https://test.example.com/gone"));

        var mockSelector = CreatePassthroughSelector();
        var action = new FetchNextCandidateAction(
            mockFetcher.Object,
            new TextSummarizer(),
            mockSelector.Object,
            new CandidateUrlProvider(),
            new ProviderResearchCache());

        var data = new DataStore();
        data.Add(new DecisionData
        {
            Id = "link-1",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://test.example.com/gone",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var ctx = MakeActionContext(templateParameters: null, properties: null);
        ctx.Data.Add(new DecisionData
        {
            Id = "link-1",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://test.example.com/gone",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        // 404 is a permanent failure — retrying the same dead URL won't help.
        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
    }

    [Fact]
    public async Task FetchNextCandidate_ApiLikeUrl_ForBaseUrlField_DefersToProbeWithoutFetch()
    {
        // For the baseUrl field, an API-like candidate must NOT be fetched as a web
        // page — GETting its root 404s for a valid API host, which previously
        // discarded the correct baseUrl before it could be probed.
        var mockFetcher = new Mock<IWebContentFetcher>();
        var mockSelector = CreatePassthroughSelector();
        var action = new FetchNextCandidateAction(
            mockFetcher.Object,
            new TextSummarizer(),
            mockSelector.Object,
            new CandidateUrlProvider(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string> { ["fieldKind"] = "baseUrl" });
        ctx.Data.Add(new DecisionData
        {
            Id = "link-1",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://api.test.example.com",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["fetchResult"].Should().Be("api-candidate-deferred-to-probe");
        ctx.State.Properties["lastFetchedUrl"].Should().Be("https://api.test.example.com");
        ctx.State.Properties["candidateIndex"].Should().Be(1);
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(), It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FetchNextCandidate_ApiLikeUrl_ForNonBaseUrlField_StillFetches()
    {
        // The deferral is scoped to the baseUrl field only; other trees still fetch
        // the candidate page normally.
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                It.IsAny<string>(),
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: false,
                Content: string.Empty,
                ErrorMessage: "HTTP 404",
                FinalUrl: "https://api.test.example.com"));

        var mockSelector = CreatePassthroughSelector();
        var action = new FetchNextCandidateAction(
            mockFetcher.Object,
            new TextSummarizer(),
            mockSelector.Object,
            new CandidateUrlProvider(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string> { ["fieldKind"] = "subscriptionPricingUrl" });
        ctx.Data.Add(new DecisionData
        {
            Id = "link-1",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://api.test.example.com",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(), It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task FetchNextCandidate_Timeout_ReturnsTransientFailure()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                It.IsAny<string>(),
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: false,
                Content: string.Empty,
                ErrorMessage: "Network timeout",
                FinalUrl: "https://test.example.com/slow"));

        var mockSelector = CreatePassthroughSelector();
        var action = new FetchNextCandidateAction(
            mockFetcher.Object,
            new TextSummarizer(),
            mockSelector.Object,
            new CandidateUrlProvider(),
            new ProviderResearchCache());

        var data = new DataStore();
        data.Add(new DecisionData
        {
            Id = "link-1",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://test.example.com/slow",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var ctx = MakeActionContext();
        ctx.Data.Add(new DecisionData
        {
            Id = "link-1",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://test.example.com/slow",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await action.ExecuteAsync(ctx);

        // Timeout is transient — the server may recover.
        result.Status.Should().Be(DecisionActionStatus.TransientFailure);
    }

    // ── WebSearchAction — domain filtering ─────────────────────────

    [Fact]
    public async Task WebSearch_FiltersThirdPartyDomains()
    {
        var mockSearch = new Mock<IWebSearchProvider>();
        mockSearch
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResult(
                Success: true,
                new List<SearchResultItem>
                {
                    new("MiniMax Pricing", "https://platform.minimax.io/pricing", "Subscription plans"),
                    new("Talkie AI", "https://www.talkie-ai.com/", "AI platform mentioning MiniMax"),
                    new("Some Repo", "https://github.com/some/repo", "GitHub project"),
                },
                ErrorMessage: null));

        var action = new WebSearchAction(
            mockSearch.Object,
            new StringListFormatter(),
            new CandidateUrlProvider());

        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["providerUrl"] = "https://www.minimax.io",
            ["searchQueryTemplate"] = "{providerName} subscription pricing plans"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        // Only the provider's own domain should pass through.
        // talkie-ai.com is a third-party, github.com is a non-provider host.
        result.ProducedData.Should().ContainSingle();
        result.ProducedData![0].Content.Should().Be("https://platform.minimax.io/pricing");
    }

    [Fact]
    public async Task WebSearch_WithoutProviderUrl_AcceptsAllNonBlocked()
    {
        var mockSearch = new Mock<IWebSearchProvider>();
        mockSearch
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResult(
                Success: true,
                new List<SearchResultItem>
                {
                    new("Pricing", "https://some-provider.com/pricing", "Plans"),
                    new("Info", "https://other-site.com/info", "Information"),
                },
                ErrorMessage: null));

        var action = new WebSearchAction(
            mockSearch.Object,
            new StringListFormatter(),
            new CandidateUrlProvider());

        // No providerUrl → domain filtering is skipped.
        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["searchQueryTemplate"] = "some query"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().HaveCount(2);
    }

    [Fact]
    public async Task WebSearch_WhenCurrentValueIsOnASubdomain_KeepsTheProvidersRootDomain()
    {
        // The case a stored page on a subdomain creates: the homepage the query asks for answers at
        // the root, while the value being repaired names only one of its sections. Recognising the
        // provider by the whole host of that value reads its own homepage as a third party and
        // discards it, so the rung meant to recover a wrong subdomain value can never propose a fix.
        var mockSearch = new Mock<IWebSearchProvider>();
        mockSearch
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResult(
                Success: true,
                new List<SearchResultItem>
                {
                    new("Provider", "https://example-provider.com/", "Public homepage"),
                    new("Docs", "https://docs.example-provider.com/en/stable/", "Another page of the site"),
                    new("Other", "https://www.other-ai.com/", "Unrelated site naming the provider"),
                },
                ErrorMessage: null));

        var action = new WebSearchAction(
            mockSearch.Object,
            new StringListFormatter(),
            new CandidateUrlProvider());

        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["providerUrl"] = "https://docs.example-provider.com/en/latest/",
            ["currentValue"] = "https://docs.example-provider.com/en/latest/",
            ["searchQueryTemplate"] = "{providerName} official website"
        });

        var result = await action.ExecuteAsync(ctx);

        // Both pages of the provider's own domain pass, the third party does not. The filter is
        // field-agnostic: which of the provider's own pages can answer is the classifier's call.
        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData!.Select(d => d.Content)
            .Should().ContainInOrder(
                "https://example-provider.com/",
                "https://docs.example-provider.com/en/stable/");
    }

    [Fact]
    public async Task WebSearch_SkipsThePageTheFindingNames()
    {
        // The page a finding names was fetched and judged by the rungs above the search — that is why
        // the search runs. Queued again it costs a search slot and a classification call to reach the
        // answer the tree already has, and the only value it could propose is the one being replaced.
        var mockSearch = new Mock<IWebSearchProvider>();
        mockSearch
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResult(
                Success: true,
                new List<SearchResultItem>
                {
                    new("Docs", "https://docs.example-provider.com/en/latest/", "The stored page"),
                    new("DocsNoSlash", "https://docs.example-provider.com/en/latest", "Same page, other spelling"),
                    new("Provider", "https://example-provider.com/", "Public homepage"),
                },
                ErrorMessage: null));

        var action = new WebSearchAction(
            mockSearch.Object,
            new StringListFormatter(),
            new CandidateUrlProvider());

        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["providerUrl"] = "https://docs.example-provider.com/en/latest/",
            ["currentValue"] = "https://docs.example-provider.com/en/latest/",
            ["searchQueryTemplate"] = "{providerName} official website"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData!.Select(d => d.Content)
            .Should().ContainSingle().Which.Should().Be("https://example-provider.com/");
    }

    [Fact]
    public async Task WebSearch_WithNumericProviderHost_KeepsTheAddressIntact()
    {
        // Self-hosted entries carry a local address rather than a domain. Cutting a "domain suffix"
        // from it would leave a fragment that matches unrelated hosts instead of that machine.
        var mockSearch = new Mock<IWebSearchProvider>();
        mockSearch
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResult(
                Success: true,
                new List<SearchResultItem>
                {
                    new("Local", "http://127.0.0.1:1234/v1/", "Local server"),
                    new("Other", "http://198.51.100.7/", "Unrelated machine"),
                },
                ErrorMessage: null));

        var action = new WebSearchAction(
            mockSearch.Object,
            new StringListFormatter(),
            new CandidateUrlProvider());

        var ctx = MakeActionContext(new Dictionary<string, string>
        {
            ["providerUrl"] = "http://127.0.0.1:1234/v1/",
            ["searchQueryTemplate"] = "{providerName} official website"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData!.Select(d => d.Content)
            .Should().ContainSingle().Which.Should().Be("http://127.0.0.1:1234/v1/");
    }

    [Fact]
    public async Task FetchNextCandidate_ResetsStaleLastVerifySucceeded()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                It.IsAny<string>(),
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: false,
                Content: string.Empty,
                ErrorMessage: "HTTP 404",
                FinalUrl: "https://test.example.com/gone"));

        var mockSelector = CreatePassthroughSelector();
        var action = new FetchNextCandidateAction(
            mockFetcher.Object,
            new TextSummarizer(),
            mockSelector.Object,
            new CandidateUrlProvider(),
            new ProviderResearchCache());

        var data = new DataStore();
        data.Add(new DecisionData
        {
            Id = "link-1",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://test.example.com/gone",
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Simulate stale reachability state from a previous iteration.
        var ctx = MakeActionContext(properties: new Dictionary<string, object?>
        {
            ["lastVerifySucceeded"] = true
        });
        ctx.Data.Add(new DecisionData
        {
            Id = "link-1",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://test.example.com/gone",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await action.ExecuteAsync(ctx);

        // The fetch must reset lastVerifySucceeded to false so the stale
        // value doesn't leak through to the is-reachable condition.
        ctx.State.Properties["lastVerifySucceeded"].Should().Be(false);
    }

    [Fact]
    public async Task FetchNextCandidate_NextCandidateAlreadyVisited_StepsOverItWithinTheSameVisit()
    {
        // The tree re-enters this action without advancing its index when a fetched page yielded
        // no answer. Reporting success for a page this run already fetched left the evidence
        // holding the previous page's content while the state named this one, so the classifier
        // judged a page it was never given.
        const string VisitedUrl = "https://test.example.com/go";
        const string NextUrl = "https://test.example.com/docs";

        var mockFetcher = CreatePageFetcher((NextUrl, "docs page content"));
        var action = new FetchNextCandidateAction(
            mockFetcher.Object,
            new TextSummarizer(),
            CreatePassthroughSelector().Object,
            new CandidateUrlProvider(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(properties: new Dictionary<string, object?>
        {
            // Spelled with a trailing slash: the visited record has to recognise the address the
            // queue holds, and the queue holds the page this action stored it under.
            ["visitedUrls"] = VisitedUrl + "/",
            ["candidateIndex"] = 0
        });
        AddCandidateLink(ctx, "link-1", VisitedUrl, 0);
        AddCandidateLink(ctx, "link-2", NextUrl, 1);

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["url"].Should().Be(NextUrl);
        ctx.State.Properties["lastFetchedUrl"].Should().Be(NextUrl);
        ctx.State.Properties["candidateIndex"].Should().Be(2);
        // The whole point of the invariant: the content handed on always belongs to the URL named.
        var pageText = result.ProducedData!.Single();
        pageText.Source.Should().Be(NextUrl);
        pageText.Content.Should().Be("docs page content");
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                VisitedUrl, It.IsAny<EContentFormat>(), It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FetchNextCandidate_VisitedRecordHoldsManyLongUrls_StillRecognisesEveryPage()
    {
        // The visited value used to be truncated to a few entries of a few dozen characters
        // each, which silently un-visited every page dropped from it — and the run spent another
        // fetch and another LLM classification judging a page it had already concluded on.
        var visitedUrls = Enumerable
            .Range(0, 12)
            .Select(i => $"https://test.example.com/pricing-page-with-a-path-well-over-eighty-characters-long-{i}")
            .ToList();

        const string RepeatUrl = "https://test.example.com/pricing-page-with-a-path-well-over-eighty-characters-long-7";
        const string NextUrl = "https://test.example.com/docs";

        var mockFetcher = CreatePageFetcher((NextUrl, "docs page content"));
        var action = new FetchNextCandidateAction(
            mockFetcher.Object,
            new TextSummarizer(),
            CreatePassthroughSelector().Object,
            new CandidateUrlProvider(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(properties: new Dictionary<string, object?>
        {
            ["visitedUrls"] = string.Join(',', visitedUrls),
            ["candidateIndex"] = 0
        });
        AddCandidateLink(ctx, "link-1", RepeatUrl, 0);
        AddCandidateLink(ctx, "link-2", NextUrl, 1);

        var result = await action.ExecuteAsync(ctx);

        result.Properties!["url"].Should().Be(NextUrl);
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                RepeatUrl, It.IsAny<EContentFormat>(), It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // And the record of what was read stays complete enough to be recognised next time.
        ctx.State.Properties["visitedUrls"].Should().BeOfType<string>()
            .Which.Split(',').Should().HaveCount(13);
    }

    [Fact]
    public async Task FetchNextCandidate_EveryCandidateVisited_FailsRatherThanReuseTheLastPage()
    {
        const string FirstUrl = "https://test.example.com/go";
        const string SecondUrl = "https://test.example.com/docs";

        var mockFetcher = CreatePageFetcher();
        var action = new FetchNextCandidateAction(
            mockFetcher.Object,
            new TextSummarizer(),
            CreatePassthroughSelector().Object,
            new CandidateUrlProvider(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(properties: new Dictionary<string, object?>
        {
            ["visitedUrls"] = $"{FirstUrl},{SecondUrl}",
            ["candidateIndex"] = 0,
            // Left over from the last successful fetch; an exhausted queue must not vouch for it.
            ["lastFetchedUrl"] = SecondUrl,
            ["lastFetchedContent"] = "docs page content"
        });
        AddCandidateLink(ctx, "link-1", FirstUrl, 0);
        AddCandidateLink(ctx, "link-2", SecondUrl, 1);

        var result = await action.ExecuteAsync(ctx);

        // Existing routing takes permanentFailure to the web-search fallback, which is the
        // honest next step: there is no unvisited page left to read.
        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        result.Properties!["fetchResult"].Should().Be("exhausted");
        result.ProducedData.Should().BeNull();
        ctx.State.Properties["candidateIndex"].Should().Be(2);
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(), It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanCandidateContent_PageLinksOnlyVariantsOfPagesAlreadyVisited_AddsNoCandidates()
    {
        // The scan exists to widen the pool. Re-adding the page just judged, under a spelling
        // that differs only by a trailing slash or a section anchor, restarts it instead.
        const string VisitedUrl = "https://test.example.com/go";
        const string FetchedUrl = "https://test.example.com/docs";

        var mockFetcher = CreatePageFetcher(
            (FetchedUrl, "[Go](https://test.example.com/go/) and [Go again](https://test.example.com/GO#pricing)"));
        var action = new ScanCandidateContentAction(mockFetcher.Object, new CandidateUrlProvider());

        var ctx = MakeActionContext(properties: new Dictionary<string, object?>
        {
            ["lastFetchedUrl"] = FetchedUrl,
            ["visitedUrls"] = VisitedUrl
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["scanResult"].Should().Be("no-new-urls");
        result.ProducedData.Should().BeNull();
        ctx.State.Properties.Should().NotContainKey("candidateIndex");
    }

    // ── ScanSiblingContentAction ───────────────────────────────────────

    [Fact]
    public void ScanSiblingContent_ExtractUrlsFromContent_FindsUrlsInCodeExamples()
    {
        var content = """
        # API Reference

        To make API calls, use the following endpoint:

        ```bash
        curl https://api.test.example.com/v1/chat/completions \
          -H "Authorization: Bearer fake-key" \
          -d '{"model": "test"}'
        ```

        Base URL: https://api.test.example.com/v1
        Documentation: https://docs.test.example.com/api
        """;

        var urls = ScanSiblingContentAction.ExtractUrlsFromContent(content);

        urls.Should().Contain("https://api.test.example.com/v1/chat/completions");
        urls.Should().Contain("https://api.test.example.com/v1");
        urls.Should().Contain("https://docs.test.example.com/api");
    }

    [Fact]
    public void ScanSiblingContent_ExtractUrlsFromContent_DeduplicatesUrls()
    {
        var content = """
        Endpoint: https://api.test.example.com/v1
        Also see: https://api.test.example.com/v1
        """;

        var urls = ScanSiblingContentAction.ExtractUrlsFromContent(content);

        urls.Should().ContainSingle();
    }

    [Fact]
    public async Task ScanSiblingContent_WithSiblingUrls_ExtractsAndProducesEvidence()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                "https://docs.test.example.com/pricing",
                EContentFormat.Markdown,
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: "API endpoint: https://api.test.example.com/v1\nDocs: https://docs.test.example.com",
                ErrorMessage: null,
                FinalUrl: "https://docs.test.example.com/pricing"));

        // Empty handler: it throws if any request reaches it, so this also asserts
        // that a page the renderer already read is not fetched a second time.
        var action = new ScanSiblingContentAction(
            mockFetcher.Object, new HttpClient(new FakeHttpMessageHandler()), new ProviderResearchCache());
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["siblingUrls"] = "apiPricingUrl=https://docs.test.example.com/pricing"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().NotBeEmpty();
        result.ProducedData!.Select(d => d.Content)
            .Should().Contain("https://api.test.example.com/v1");
    }

    [Fact]
    public async Task ScanSiblingContent_WithoutSiblingUrls_ReturnsNoSiblingUrls()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        var action = new ScanSiblingContentAction(
            mockFetcher.Object, new HttpClient(new FakeHttpMessageHandler()), new ProviderResearchCache());
        var ctx = MakeActionContext();

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().BeNull();
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(),
                It.IsAny<int?>(), It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanSiblingContent_RendererFails_RecoversTheCallExampleFromTheServedDocument()
    {
        // The whole point of this scan is the documented call example. A browser that
        // times out on the docs page must not end the search — the example is plain text
        // in the served document, so it is read directly and its base URL becomes a candidate.
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(),
                It.IsAny<int?>(), It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: false,
                Content: string.Empty,
                ErrorMessage: "browser timed out",
                FinalUrl: null));

        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """
            <html><body>
            <a href="https://docs.test.example.com/guides">Guides</a>
            <a href="https://docs.test.example.com/changelog">Changelog</a>
            <pre>curl https://api.test.example.com/v1/chat/completions -H "Authorization: Bearer fake-api-key"</pre>
            </body></html>
            """);

        var action = new ScanSiblingContentAction(
            mockFetcher.Object, new HttpClient(handler), new ProviderResearchCache());
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["siblingUrls"] = "documentationUrl=https://docs.test.example.com/quickstart"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://docs.test.example.com/quickstart");

        // The call example is first: extraction stops at a cap, and navigation links
        // would otherwise fill it before the base URL is ever seen.
        result.ProducedData![0].Content.Should().Be("https://api.test.example.com/v1/chat/completions");
        result.Properties!["scanResult"].Should().Be("success");
        result.Properties.Should().NotContainKey("unreadablePages");
        ctx.State.Properties.Should().NotContainKey("unreadableSiblingPages");
    }

    [Fact]
    public async Task ScanSiblingContent_PageCouldNotBeOpened_ReportsUnreadableNotNoUrlsExtracted()
    {
        // "No URLs extracted" is a statement about the documentation. Reporting it when the
        // documentation was never opened states a gap in this run as a fact about the provider.
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(),
                It.IsAny<int?>(), It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: false,
                Content: string.Empty,
                ErrorMessage: "browser timed out",
                FinalUrl: null));

        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.NotFound, "<html>not found</html>");

        var action = new ScanSiblingContentAction(
            mockFetcher.Object, new HttpClient(handler), new ProviderResearchCache());
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["siblingUrls"] = "apiPricingUrl=https://docs.test.example.com/pricing"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().BeNull();
        result.Properties!["scanResult"].Should().Be("documentation-unreadable");
        result.Properties["unreadablePages"].Should().Be("1");

        // Both read attempts are named, so the log says why there is no evidence.
        var unreadable = ctx.State.Properties["unreadableSiblingPages"] as string;
        unreadable.Should().NotBeNull();
        unreadable.Should().Contain("https://docs.test.example.com/pricing");
        unreadable.Should().Contain("browser timed out");
        unreadable.Should().Contain("HTTP 404");
    }

    [Fact]
    public async Task ScanSiblingContent_PageOpenedWithNoUrls_StillReportsNoUrlsExtracted()
    {
        // The converse of the unreadable case: the page was opened and genuinely held no
        // URLs. That is a fact about the provider and must keep its own outcome.
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(),
                It.IsAny<int?>(), It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: "Pricing is charged per million tokens.",
                ErrorMessage: null,
                FinalUrl: "https://docs.test.example.com/pricing"));

        var action = new ScanSiblingContentAction(
            mockFetcher.Object, new HttpClient(new FakeHttpMessageHandler()), new ProviderResearchCache());
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["siblingUrls"] = "apiPricingUrl=https://docs.test.example.com/pricing"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().BeNull();
        result.Properties!["scanResult"].Should().Be("no-urls-extracted");
        result.Properties.Should().NotContainKey("unreadablePages");
        ctx.State.Properties.Should().NotContainKey("unreadableSiblingPages");
    }

    [Fact]
    public async Task ScanSiblingContent_EndpointMeasuredByAnotherTree_QueuedAheadOfPageUrls()
    {
        // The counting tree already asked this URL and got a model list back; the field that has
        // to name the base under it is this one. Twenty-five navigation links must not crowd that
        // fact out of a capped candidate list — losing it is how a batch re-searched from scratch.
        const string MeasuredUrl = "https://api.test.example.com/zen/go/v1/models";

        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(),
                It.IsAny<int?>(), It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: string.Join(
                    "\n",
                    Enumerable.Range(1, 25).Select(i => $"- https://docs.test.example.com/page-{i}")),
                ErrorMessage: null,
                FinalUrl: "https://docs.test.example.com/pricing"));

        var cache = new ProviderResearchCache();
        cache.RecordCatalogEndpoint("https://test.example.com", MeasuredUrl);

        var action = new ScanSiblingContentAction(
            mockFetcher.Object, new HttpClient(new FakeHttpMessageHandler()), cache);
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["siblingUrls"] = "apiPricingUrl=https://docs.test.example.com/pricing"
            });

        var result = await action.ExecuteAsync(ctx);

        result.ProducedData![0].Content.Should().Be(MeasuredUrl);
        result.ProducedData![0].Metadata!["description"]
            .Should().Be("Measured as a model list earlier in this batch");
    }

    [Fact]
    public async Task ScanSiblingContent_MeasuredUrlOnAForeignHost_IsNotQueued()
    {
        // The record is keyed by provider, but a candidate list becomes a suggestion, and a base
        // URL on somebody else's host is a wrong answer written into a catalogue.
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(),
                It.IsAny<int?>(), It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: "Pricing is charged per million tokens.",
                ErrorMessage: null,
                FinalUrl: "https://docs.test.example.com/pricing"));

        var cache = new ProviderResearchCache();
        cache.RecordCatalogEndpoint("https://test.example.com", "https://elsewhere.example/v1/models");

        var action = new ScanSiblingContentAction(
            mockFetcher.Object, new HttpClient(new FakeHttpMessageHandler()), cache);
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["siblingUrls"] = "apiPricingUrl=https://docs.test.example.com/pricing"
            });

        var result = await action.ExecuteAsync(ctx);

        result.ProducedData.Should().BeNull();
    }

    [Fact]
    public async Task ScanSiblingContent_PageAnotherTreeRendered_IsNotRenderedAgain()
    {
        // The tree reads the sibling pages a second time once its first candidate pool is used up,
        // and a browser load per page per pass is what the time budget is spent on.
        var cache = new ProviderResearchCache();
        cache.SetPageFetch("https://docs.test.example.com/pricing", new PageFetchCacheEntry
        {
            MarkdownContent = "API endpoint: https://api.test.example.com/v1",
            Success = true
        });

        var mockFetcher = new Mock<IWebContentFetcher>();
        var action = new ScanSiblingContentAction(
            mockFetcher.Object, new HttpClient(new FakeHttpMessageHandler()), cache);
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["siblingUrls"] = "apiPricingUrl=https://docs.test.example.com/pricing"
            });

        var result = await action.ExecuteAsync(ctx);

        result.ProducedData!.Select(d => d.Content)
            .Should().Contain("https://api.test.example.com/v1");
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(), It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void OrderProbeCandidatesFirst_PutsTheProvidersOwnHostsFirst()
    {
        // Ordering, never filtering: catalogue entries do serve an API from a host none of their
        // other URLs touch, so a foreign-looking link stays in the list — just behind the
        // provider's own pages, because a base on somebody else's host is a different service
        // offered under this provider's name.
        var providerHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test.example.com" };
        var urls = new List<string>
        {
            "https://blog.other.example/post",
            "https://api.upstream.example/v1",
            "https://test.example.com/pricing",
            "https://api.test.example.com/v1/chat/completions"
        };

        var ordered = ScanSiblingContentAction.OrderProbeCandidatesFirst("", urls, providerHosts);

        ordered.Should().ContainInOrder(
            "https://api.test.example.com/v1/chat/completions",
            "https://test.example.com/pricing",
            "https://api.upstream.example/v1",
            "https://blog.other.example/post");
    }

    [Fact]
    public void OrderProbeCandidatesFirst_PutsTheUrlPrintedInACodeSampleFirst()
    {
        // Inside one rank the call example leads: that is where an integration page states a base.
        var providerHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test.example.com" };
        var pageText = "Read the changelog at https://test.example.com/changelog, "
                       + "or call `https://test.example.com/docs/quickstart`.";
        var urls = new List<string>
        {
            "https://test.example.com/changelog",
            "https://test.example.com/docs/quickstart"
        };

        var ordered = ScanSiblingContentAction.OrderProbeCandidatesFirst(pageText, urls, providerHosts);

        ordered[0].Should().Be("https://test.example.com/docs/quickstart");
    }

    [Fact]
    public void ScanSiblingContent_PrioritizeCodeElements_PutsTheCallExampleAheadOfNavigation()
    {
        var html = """
            <html><head>
            <link rel="alternate" hreflang="en" href="https://docs.test.example.com/en">
            </head><body>
            <a href="https://docs.test.example.com/guides">Guides</a>
            <pre>curl https://api.test.example.com/v1/models</pre>
            <p>See <code>https://api.test.example.com/v1beta</code> for the beta base.</p>
            </body></html>
            """;

        var reordered = ScanSiblingContentAction.PrioritizeCodeElements(html);

        reordered.IndexOf("https://api.test.example.com/v1/models", StringComparison.Ordinal)
            .Should().BeLessThan(reordered.IndexOf("https://docs.test.example.com/en", StringComparison.Ordinal));
        reordered.IndexOf("https://api.test.example.com/v1beta", StringComparison.Ordinal)
            .Should().BeLessThan(reordered.IndexOf("https://docs.test.example.com/guides", StringComparison.Ordinal));
        // Nothing is dropped — reordering only decides what survives the extraction cap.
        reordered.Should().Contain("https://docs.test.example.com/guides");
    }

    [Theory]
    [InlineData("https://gateway.test.example.com/zen/go/v1", true)]
    [InlineData("https://gateway.test.example.com/zen/go/v1/models", true)]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/", true)]
    [InlineData("https://docs.test.example.com/quickstart", false)]
    public void ScanSiblingContent_IsApiLikeUrl_RecognisesAVersionedBaseBehindAPathPrefix(
        string url, bool expected)
    {
        // A gateway base carries a prefix and may end at the version segment, so neither
        // the host nor a trailing slash identifies it. Missing it here means the candidate
        // is fetched as a web page and a correct base is discarded before it can be probed.
        ScanSiblingContentAction.IsApiLikeUrl(url).Should().Be(expected);
    }

    // ── ScanCandidateContentAction ──────────────────────────────────

    [Fact]
    public async Task ScanCandidateContent_NoLastFetchedUrl_ReturnsNoCandidateUrl()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        var action = new ScanCandidateContentAction(mockFetcher.Object, new CandidateUrlProvider());
        var ctx = MakeActionContext();

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["scanResult"].Should().Be("no-candidate-url");
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(),
                It.IsAny<int?>(), It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanCandidateContent_ApiEndpointCandidate_ForBaseUrlField_IsNotRenderedAsAPage()
    {
        // The endpoint carries a response body, not a page. Rendering it to look for links costs a
        // browser load and can only turn up the URL the probe is about to ask directly.
        var mockFetcher = new Mock<IWebContentFetcher>();
        var action = new ScanCandidateContentAction(mockFetcher.Object, new CandidateUrlProvider());
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string> { ["fieldKind"] = "baseUrl" },
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://api.test.example.com/zen/go/v1/models"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["scanResult"].Should().Be("endpoint-has-no-page");
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(), It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanCandidateContent_AlreadyScanned_ReturnsAlreadyScanned()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        var action = new ScanCandidateContentAction(mockFetcher.Object, new CandidateUrlProvider());
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://docs.test.example.com/quickstart",
                ["candidateContentScanned"] = true
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["scanResult"].Should().Be("already-scanned");
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                It.IsAny<string>(), It.IsAny<EContentFormat>(),
                It.IsAny<int?>(), It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanCandidateContent_WithUrlsInContent_ProducesCandidateLinkEvidence()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                "https://docs.test.example.com/quickstart",
                EContentFormat.Markdown,
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: "# Quickstart\n\ncurl https://api.test.example.com/v1/chat\n\nSee also https://api.test.example.com/v1",
                ErrorMessage: null,
                FinalUrl: "https://docs.test.example.com/quickstart"));

        var action = new ScanCandidateContentAction(mockFetcher.Object, new CandidateUrlProvider());
        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://docs.test.example.com/quickstart"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["scanResult"].Should().Be("success");
        result.ProducedData.Should().NotBeEmpty();
        result.ProducedData!.Should().AllSatisfy(d =>
        {
            d.Type.Should().Be("CandidateLink");
            d.Source.Should().Be("https://docs.test.example.com/quickstart");
        });
        result.ProducedData.Select(d => d.Content)
            .Should().Contain("https://api.test.example.com/v1");
        ctx.State.Properties["candidateContentScanned"].Should().Be(true);
    }

    [Fact]
    public async Task ScanCandidateContent_NoNewUrls_ReturnsNoNewUrls()
    {
        // The candidate page only contains URLs that are already in the candidate pool.
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                "https://docs.test.example.com/page",
                EContentFormat.Markdown,
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: "Link: https://already-known.test.example.com/v1",
                ErrorMessage: null,
                FinalUrl: "https://docs.test.example.com/page"));

        var action = new ScanCandidateContentAction(mockFetcher.Object, new CandidateUrlProvider());

        // Pre-populate the data store with the same URL
        var dataStore = new DataStore();
        dataStore.Add(new DecisionData
        {
            Id = "existing-1",
            Source = "scan",
            Type = "CandidateLink",
            Content = "https://already-known.test.example.com/v1",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var state = new DecisionState();
        state.Properties["lastFetchedUrl"] = "https://docs.test.example.com/page";

        var ctx = new DecisionActionContext(
            "test-node", "test-exec",
            new Dictionary<string, string>(),
            state, dataStore);

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["scanResult"].Should().Be("no-new-urls");
        result.ProducedData.Should().BeNull();
        ctx.State.Properties["candidateContentScanned"].Should().Be(true);
    }

    // ── QueryModelsEndpointAction: candidate URL preference ──────────

    [Fact]
    public async Task QueryModelsEndpoint_PrefersCandidateUrlFromState_OverTemplateBaseUrl()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"data":[{"id":"m1"}]}""");
        var http = new HttpClient(handler);
        var action = new QueryModelsEndpointAction(http, new StringListFormatter());

        // Template says one baseUrl, state has a different candidate URL.
        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["baseUrl"] = "https://original.test.example.com/v1"
            },
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://candidate.test.example.com/v1"
            });

        await action.ExecuteAsync(ctx);

        // The HTTP request must go to the CANDIDATE URL, not the template baseUrl.
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString()
            .Should().StartWith("https://candidate.test.example.com/v1");
    }

    // ── QueryModelsEndpointAction: documentation URL detection ────────

    [Theory]
    [InlineData("https://platform.minimax.io/docs/guides/pricing-paygo", true)]
    [InlineData("https://platform.minimax.io/docs/api-reference/text-chat", true)]
    [InlineData("https://docs.example.com/guide/getting-started", true)]
    [InlineData("https://api.example.com/v1/chat/completions", false)]
    [InlineData("https://api.minimax.io/v1", false)]
    [InlineData("https://api.example.com", false)]
    public void IsDocumentationPageUrl_DetectsDocPaths(string url, bool expected)
    {
        QueryModelsEndpointAction.IsDocumentationPageUrl(url).Should().Be(expected);
    }

    [Fact]
    public async Task QueryModelsEndpoint_DocumentationPageCandidate_ReturnsPermanentFailure()
    {
        var handler = new FakeHttpMessageHandler();
        var http = new HttpClient(handler);
        var action = new QueryModelsEndpointAction(http, new StringListFormatter());

        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://platform.minimax.io/docs/guides/pricing-paygo"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        handler.Requests.Should().BeEmpty("doc page URLs must not be probed");
    }

    // ── QueryModelsEndpointAction: versioned endpoint URL resolution ──

    [Theory]
    [InlineData(
        "https://api.minimax.io/v1/text/chatcompletion_v2",
        "/v1/models",
        "https://api.minimax.io/v1/models")]
    [InlineData(
        "https://api.example.com/v1/chat/completions",
        "/v1/models",
        "https://api.example.com/v1/models")]
    [InlineData(
        "https://api.example.com/v2/models/list",
        "/v1/models",
        "https://api.example.com/v2/models")]
    [InlineData(
        "https://api.example.com/v1",
        "/v1/models",
        "https://api.example.com/v1/models")]
    [InlineData(
        "https://api.example.com",
        "/v1/models",
        "https://api.example.com/v1/models")]
    // A gateway base carries a path prefix and answers only under it, so the probe
    // must keep the prefix. Probing the bare host here 404s on a base that is correct.
    [InlineData(
        "https://gateway.test.example.com/zen/go/v1/models",
        "/v1/models",
        "https://gateway.test.example.com/zen/go/v1/models")]
    // The version segment can carry a letter suffix (a real catalogue entry serves
    // /v1beta/), and the default models endpoint has no version prefix of its own —
    // recognising only "/v1" would compose "…/v1beta/v1/models".
    [InlineData(
        "https://generativelanguage.googleapis.com/v1beta/",
        "models",
        "https://generativelanguage.googleapis.com/v1beta/models")]
    [InlineData(
        "https://api.example.com/v1/chat/completions",
        "models",
        "https://api.example.com/v1/models")]
    public void ResolveProbeUrl_ProbesTheBaseTheCandidateImplies(
        string candidateUrl, string modelsEndpoint, string expectedProbeUrl)
    {
        var result = QueryModelsEndpointAction.ResolveProbeUrl(candidateUrl, modelsEndpoint);
        result.Should().Be(expectedProbeUrl);
    }

    [Fact]
    public async Task QueryModelsEndpoint_FullEndpointCandidate_ProbesCorrectBaseUrl()
    {
        // Candidate is a full endpoint URL extracted from a curl example.
        // The probe should go to {host}/v1/models, not {fullEndpoint}/v1/models.
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"data":[{"id":"m1"}]}""");
        var http = new HttpClient(handler);
        var action = new QueryModelsEndpointAction(http, new StringListFormatter());

        var ctx = MakeActionContext(
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://api.minimax.io/v1/text/chatcompletion_v2"
            });

        await action.ExecuteAsync(ctx);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api.minimax.io/v1/models");
    }

    // ── QueryModelsEndpointAction: suggested base URL resolution ──

    [Theory]
    [InlineData("https://api.test.example.com/v1/chat/completions", "/v1/models", "https://api.test.example.com/v1")]
    [InlineData("https://api.test.example.com", "/v1/models", "https://api.test.example.com/v1")]
    [InlineData("https://api.test.example.com/", "/v1/models", "https://api.test.example.com/v1")]
    [InlineData("https://api.test.example.com/openai/v1/embeddings", "/v1/models", "https://api.test.example.com/openai/v1")]
    [InlineData("https://api.test.example.com/v2/models", "/v1/models", "https://api.test.example.com/v2")]
    public void ResolveSuggestedBaseUrl_TruncatesAfterVersionSegment(
        string candidateUrl, string modelsEndpoint, string expected)
    {
        QueryModelsEndpointAction.ResolveSuggestedBaseUrl(candidateUrl, modelsEndpoint)
            .Should().Be(expected);
    }

    [Fact]
    public async Task QueryModelsEndpoint_Unauthorized_RecordsVersionedBaseAsWinner()
    {
        // On 401 (auth-gated → win) the winning candidate is a full endpoint route
        // from a curl example; the recorded winner must be the version-prefixed BASE,
        // not the raw candidate, so the baseUrl suggestion is .../v1 (not .../v1/chat/completions).
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.Unauthorized, "unauthorized");
        var http = new HttpClient(handler);
        var action = new QueryModelsEndpointAction(http, new StringListFormatter());

        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["modelsEndpoint"] = "/v1/models",
                ["fieldKind"] = "baseUrl"
            },
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://api.test.example.com/v1/chat/completions"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        ctx.State.Properties["lastHttpStatus"].Should().Be(401);
        ctx.State.Properties["verifiedWinnerUrl"].Should().Be("https://api.test.example.com/v1");
    }

    [Fact]
    public async Task QueryModelsEndpoint_Success_RecordsVersionedBaseAsWinner()
    {
        // On a 2xx models response the recorded winner is likewise the version-prefixed base.
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"data":[{"id":"m1"}]}""");
        var http = new HttpClient(handler);
        var action = new QueryModelsEndpointAction(http, new StringListFormatter());

        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["modelsEndpoint"] = "/v1/models",
                ["fieldKind"] = "baseUrl"
            },
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://api.test.example.com/v1/chat/completions"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["verifiedWinnerUrl"].Should().Be("https://api.test.example.com/v1");
    }

    [Fact]
    public async Task QueryModelsEndpoint_NonBaseUrlField_DoesNotRecordWinner()
    {
        // The counting tree reuses this action to read a model list. A working probe says
        // nothing about a different field's value, so it must not leave a "winner" URL
        // behind — that URL used to surface as the suggestion for unrelated fields.
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"data":[{"id":"m1"}]}""");
        var http = new HttpClient(handler);
        var action = new QueryModelsEndpointAction(http, new StringListFormatter());

        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["modelsEndpoint"] = "/v1/models",
                ["fieldKind"] = "minModelCount"
            },
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://api.test.example.com/v1/chat/completions"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties.Should().NotContainKey("verifiedWinnerUrl");
    }

    // ── Auth-gated detection (401/403 + JSON) ────────────────────────

    [Fact]
    public async Task QueryModelsEndpoint_ForbiddenWithJson_BodyIsAuthGated_ReturnsSuccess()
    {
        // Google Gemini answers 403 + JSON {"error":...} when the models endpoint is
        // probed without an API key. A 403 with a JSON body confirms a real API that
        // requires authentication — the tree must treat this as a win, not a failure.
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.Forbidden, """{"error":{"message":"API key not valid"}}""");
        var http = new HttpClient(handler);
        var action = new QueryModelsEndpointAction(http, new StringListFormatter());

        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["modelsEndpoint"] = "/v1/models",
                ["fieldKind"] = "baseUrl"
            },
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://generativelanguage.googleapis.com/v1beta/"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["lastHttpStatus"].Should().Be(403);
        ctx.State.Properties[DecisionTreeStateKeys.IsAuthGated].Should().Be(true);
        ctx.State.Properties[DecisionTreeStateKeys.VerifiedWinnerUrl].Should().Be("https://generativelanguage.googleapis.com/v1beta");
    }

    [Fact]
    public async Task QueryModelsEndpoint_ForbiddenWithHtml_BodyIsCdnBlock_ReturnsPermanentFailure()
    {
        // A 403 with an HTML body is a CDN or bot-protection block, not an API signal.
        // The probe must NOT treat this as auth-gated — it must fall through to normal
        // error handling and return PermanentFailure.
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.Forbidden, "<html><body>Access denied</body></html>");
        var http = new HttpClient(handler);
        var action = new QueryModelsEndpointAction(http, new StringListFormatter());

        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["modelsEndpoint"] = "/v1/models",
                ["fieldKind"] = "baseUrl"
            },
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://api.test.example.com/v1/"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        ctx.State.Properties["lastHttpStatus"].Should().Be(403);
        ctx.State.Properties.Should().NotContainKey(DecisionTreeStateKeys.IsAuthGated);
    }

    [Fact]
    public async Task QueryModelsEndpoint_UnauthorizedWithJson_ReturnsAuthGatedSuccess()
    {
        // 401 + JSON body is also auth-gated. Previously this was PermanentFailure even
        // with a JSON body; now the JSON content confirms a real API behind the auth wall.
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.Unauthorized, """{"error":"invalid_api_key"}""");
        var http = new HttpClient(handler);
        var action = new QueryModelsEndpointAction(http, new StringListFormatter());

        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["modelsEndpoint"] = "/v1/models",
                ["fieldKind"] = "baseUrl"
            },
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://api.test.example.com/v1/models"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties[DecisionTreeStateKeys.IsAuthGated].Should().Be(true);
        ctx.State.Properties[DecisionTreeStateKeys.VerifiedWinnerUrl].Should().Be("https://api.test.example.com/v1");
    }

    [Fact]
    public async Task QueryModelsEndpoint_ForbiddenWithJson_NonBaseUrlField_ReturnsPermanentFailure()
    {
        // The auth-gated JSON detection is gated on fieldKind=="baseUrl". For other fields
        // (e.g. minModelCount), a 403 must remain PermanentFailure regardless of body shape.
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.Forbidden, """{"error":"forbidden"}""");
        var http = new HttpClient(handler);
        var action = new QueryModelsEndpointAction(http, new StringListFormatter());

        var ctx = MakeActionContext(
            templateParameters: new Dictionary<string, string>
            {
                ["modelsEndpoint"] = "/v1/models",
                ["fieldKind"] = "minModelCount"
            },
            properties: new Dictionary<string, object?>
            {
                ["lastFetchedUrl"] = "https://api.test.example.com/v1/models"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.PermanentFailure);
        ctx.State.Properties.Should().NotContainKey(DecisionTreeStateKeys.IsAuthGated);
    }
}
