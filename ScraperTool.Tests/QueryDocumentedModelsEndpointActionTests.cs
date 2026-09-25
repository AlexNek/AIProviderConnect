using System.Net;
using System.Net.Http;
using System.Text;

using AiCleverness.Models.DecisionTree;

using FluentAssertions;

using Moq;

using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.UrlResearch.DecisionTree.Actions;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Tests;

/// <summary>
/// Tests for <see cref="QueryDocumentedModelsEndpointAction"/>: the model count has to come
/// from the catalog URL the provider prints on its own pages, because the stored base URL is
/// often a different host that answers with an error body instead of a model list.
/// </summary>
public class QueryDocumentedModelsEndpointActionTests
{
    private const string DocsUrl = "https://test.example.com/docs/go";
    private const string CatalogUrl = "https://test.example.com/zen/go/v1/models";

    private sealed class RoutedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpResponseMessage> _byUrl =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> RequestedUrls { get; } = new();

        public void Route(string url, HttpStatusCode status, string body)
            => _byUrl[url] = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/plain")
            };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);

            return _byUrl.TryGetValue(url, out var response)
                ? Task.FromResult(response)
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("Not Found", Encoding.UTF8, "text/plain")
                });
        }
    }

    private static DecisionActionContext MakeContext(
        Dictionary<string, string> templateParameters,
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
            templateParameters,
            state,
            new DataStore());
    }

    private static Mock<IWebContentFetcher> CreateFetcher(string pageUrl, string markdown)
    {
        var fetcher = new Mock<IWebContentFetcher>();
        fetcher
            .Setup(f => f.FetchAsAsync(
                pageUrl,
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: markdown,
                ErrorMessage: null,
                FinalUrl: pageUrl));

        return fetcher;
    }

    /// <summary>
    /// A browser fetcher whose page read yields nothing — the failure the log recorded as
    /// "the provider documents no endpoint".
    /// </summary>
    private static Mock<IWebContentFetcher> CreateUnrenderableFetcher(params (string Url, string Error)[] pages)
    {
        var fetcher = new Mock<IWebContentFetcher>();
        foreach (var (url, error) in pages)
        {
            fetcher
                .Setup(f => f.FetchAsAsync(
                    url,
                    It.IsAny<EContentFormat>(),
                    It.IsAny<int?>(),
                    It.IsAny<ESanitizeLevel>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WebContent(
                    Success: false,
                    Content: string.Empty,
                    ErrorMessage: error,
                    FinalUrl: url));
        }

        return fetcher;
    }

    private static Dictionary<string, string> DefaultTemplateParameters() => new()
    {
        ["providerUrl"] = "https://test.example.com",
        ["fieldKind"] = "minModelCount",
        ["siblingUrls"] = $"documentationUrl={DocsUrl}"
    };

    [Fact]
    public async Task Execute_DocumentedEndpointReturnsCatalog_CountsIt()
    {
        // Arrange: the documentation page prints the catalog URL, the catalog answers with
        // three entries. Nothing here needs an LLM.
        var http = new RoutedHttpMessageHandler();
        http.Route(CatalogUrl, HttpStatusCode.OK, """{"data":[{"id":"m1"},{"id":"m2"},{"id":"m3"}]}""");

        var fetcher = CreateFetcher(DocsUrl, $"Fetch them from:\n\n{CatalogUrl}\n");
        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            fetcher.Object,
            new ProviderResearchCache());

        var ctx = MakeContext(DefaultTemplateParameters());

        // Act
        var result = await action.ExecuteAsync(ctx);

        // Assert
        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["modelCount"].Should().Be("3");
        result.Properties!["url"].Should().Be(CatalogUrl);
        ctx.State.Properties["modelCount"].Should().Be(3);
        ctx.State.Properties["modelCountMethod"].Should().Be("documented-endpoint");
        ctx.State.Properties["modelCountSourceUrl"].Should().Be(CatalogUrl);
        result.ProducedData!.Single().Type.Should().Be("ModelList");
    }

    [Fact]
    public async Task Execute_CountsEndpoint_RecordsItForTheTreesThatFollow()
    {
        // The field that has to name this URL is baseUrl — a different tree. Without the record
        // the measurement dies with the tree that made it and the next tree re-searches the same
        // documentation with a weaker method and may not find it again.
        var http = new RoutedHttpMessageHandler();
        http.Route(CatalogUrl, HttpStatusCode.OK, """{"data":[{"id":"m1"}]}""");

        var cache = new ProviderResearchCache();
        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            CreateFetcher(DocsUrl, $"Fetch them from:\n\n{CatalogUrl}\n").Object,
            cache);

        var ctx = MakeContext(DefaultTemplateParameters());

        // Act
        await action.ExecuteAsync(ctx);

        // Assert
        cache.GetCatalogEndpoints("https://test.example.com")
            .Should().ContainSingle()
            .Which.Should().Be(CatalogUrl);
    }

    [Fact]
    public async Task Execute_NoEndpointCounted_RecordsNothingForTheTreesThatFollow()
    {
        // An endpoint that answered with an error body is not a fact worth handing on: the next
        // tree would probe a URL this batch already proved wrong.
        var http = new RoutedHttpMessageHandler();

        var cache = new ProviderResearchCache();
        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            CreateFetcher(DocsUrl, $"See:\n\n{CatalogUrl}\n").Object,
            cache);

        var ctx = MakeContext(DefaultTemplateParameters());

        // Act
        var result = await action.ExecuteAsync(ctx);

        // Assert
        result.Properties!["queryResult"].Should().Be("no-documented-endpoint-responded");
        cache.GetCatalogEndpoints("https://test.example.com").Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_OnlyARelativePathIsMentioned_ProbesNothing()
    {
        // Only a URL the page states outright can be trusted; a bare "/v1/models" fragment
        // would be resolved against the docs host and probe an unrelated route.
        var http = new RoutedHttpMessageHandler();
        var fetcher = CreateFetcher(DocsUrl, "Run `curl $BASE/models` to list models.");
        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            fetcher.Object,
            new ProviderResearchCache());

        var ctx = MakeContext(DefaultTemplateParameters());

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["queryResult"].Should().Be("no-documented-endpoint");
        http.RequestedUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_NoCatalogDocumented_CompletesWithoutACount()
    {
        // The common case: the provider publishes no model list. Reporting it as a failed
        // action would mark the whole research run unsuccessful even though the tree simply
        // moves on to the next counting method.
        var http = new RoutedHttpMessageHandler();
        var fetcher = CreateFetcher(DocsUrl, "The endpoints are shown in ![diagram](https://test.example.com/img/endpoints.png).");
        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            fetcher.Object,
            new ProviderResearchCache());

        var ctx = MakeContext(DefaultTemplateParameters());

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["queryResult"].Should().Be("no-documented-endpoint");
        http.RequestedUrls.Should().BeEmpty();
        ctx.State.Properties.Should().NotContainKey("modelCount");
        ctx.State.Properties["modelCountMethod"].Should().Be("not-documented");
    }

    [Fact]
    public async Task Execute_DocumentedUrlIsNotACatalog_ReportsWhatItAnswered()
    {
        // A soft 404 ("Not Found" with HTTP 200) must be legible: the previous run log only
        // showed a raw JSON reader complaint with no URL and no body.
        var http = new RoutedHttpMessageHandler();
        http.Route(CatalogUrl, HttpStatusCode.OK, "Not Found");

        var fetcher = CreateFetcher(DocsUrl, $"GET {CatalogUrl}\n");
        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            fetcher.Object,
            new ProviderResearchCache());

        var ctx = MakeContext(DefaultTemplateParameters());

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["queryResult"].Should().Be("no-documented-endpoint-responded");
        result.Error.Should().Contain(CatalogUrl);
        result.Error.Should().Contain("Not Found");
    }

    [Fact]
    public async Task Execute_SecondVisitInSameRun_ProbesNothing()
    {
        var http = new RoutedHttpMessageHandler();
        http.Route(CatalogUrl, HttpStatusCode.OK, """{"data":[{"id":"m1"}]}""");

        var fetcher = CreateFetcher(DocsUrl, $"GET {CatalogUrl}\n");
        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            fetcher.Object,
            new ProviderResearchCache());

        var ctx = MakeContext(
            DefaultTemplateParameters(),
            new Dictionary<string, object?> { ["documentedModelsAttempted"] = true });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["queryResult"].Should().Be("already-attempted");
        http.RequestedUrls.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_FirstPageStatesACatalog_DoesNotReadFurtherPages()
    {
        // Each documentation page costs a browser fetch, so reading the next one is only
        // worth it when the previous page produced no usable endpoint.
        const string secondPageUrl = "https://test.example.com/pricing";

        var http = new RoutedHttpMessageHandler();
        http.Route(CatalogUrl, HttpStatusCode.OK, """{"data":[{"id":"m1"}]}""");

        var fetcher = CreateFetcher(DocsUrl, $"See {CatalogUrl}");
        fetcher
            .Setup(f => f.FetchAsAsync(
                secondPageUrl,
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: $"See {CatalogUrl}",
                ErrorMessage: null,
                FinalUrl: secondPageUrl));

        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            fetcher.Object,
            new ProviderResearchCache());

        var ctx = MakeContext(new Dictionary<string, string>
        {
            ["fieldKind"] = "minModelCount",
            ["siblingUrls"] = $"documentationUrl={DocsUrl};apiPricingUrl={secondPageUrl}"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Properties!["modelCount"].Should().Be("1");
        fetcher.Verify(
            f => f.FetchAsAsync(
                secondPageUrl,
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_NothingToRead_ReportsNoDocumentation()
    {
        var http = new RoutedHttpMessageHandler();
        var fetcher = new Mock<IWebContentFetcher>();
        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            fetcher.Object,
            new ProviderResearchCache());

        var ctx = MakeContext(new Dictionary<string, string> { ["fieldKind"] = "minModelCount" });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["queryResult"].Should().Be("no-documentation");
    }

    [Fact]
    public async Task Execute_BrowserRendersNothing_PlainPageReadStillSuppliesTheDocumentedEndpoint()
    {
        // A page this run failed to render is not a page with nothing on it. A docs site that
        // renders on the server prints the catalog URL in the response body, so the read has to
        // be retried plainly before any conclusion about the provider is drawn.
        const string LanguageAlternateUrl = "https://test.example.com/docs/ar/go";

        var http = new RoutedHttpMessageHandler();
        http.Route(DocsUrl, HttpStatusCode.OK, """
            <html><head>
            <link rel="alternate" hreflang="ar" href="https://test.example.com/docs/ar/go">
            </head><body>
            <pre><code>curl https://test.example.com/zen/go/v1/models</code></pre>
            </body></html>
            """);
        http.Route(CatalogUrl, HttpStatusCode.OK, """{"data":[{"id":"m1"},{"id":"m2"}]}""");

        var cache = new ProviderResearchCache();
        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            CreateUnrenderableFetcher((DocsUrl, "navigation timeout")).Object,
            cache);

        var ctx = MakeContext(DefaultTemplateParameters());

        // Act
        var result = await action.ExecuteAsync(ctx);

        // Assert
        result.Properties!["queryResult"].Should().Be("success");
        result.Properties!["modelCount"].Should().Be("2");
        result.Properties!["url"].Should().Be(CatalogUrl);
        // The language alternate is page chrome, not content. With a five-probe budget it must
        // not consume the slot the code sample needs.
        http.RequestedUrls.Should().NotContain(LanguageAlternateUrl);
        // Raw HTML must not be filed in the shared cache's MarkdownContent slot: the trees that
        // read that slot treat HTML as an empty page.
        cache.GetPageFetch(DocsUrl).Should().BeNull();
    }

    [Fact]
    public async Task Execute_OnePageOfTwoUnreadable_ReportsAPartialReadAndNamesThePage()
    {
        const string SecondPageUrl = "https://test.example.com/pricing";

        var http = new RoutedHttpMessageHandler();
        http.Route(SecondPageUrl, HttpStatusCode.NotFound, "Not Found");

        var fetcher = CreateFetcher(DocsUrl, "The endpoints are shown in ![diagram](https://test.example.com/img/endpoints.png).");
        fetcher
            .Setup(f => f.FetchAsAsync(
                SecondPageUrl,
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: false,
                Content: string.Empty,
                ErrorMessage: "browser crashed",
                FinalUrl: SecondPageUrl));

        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            fetcher.Object,
            new ProviderResearchCache());

        var ctx = MakeContext(new Dictionary<string, string>
        {
            ["providerUrl"] = "https://test.example.com",
            ["fieldKind"] = "minModelCount",
            ["siblingUrls"] = $"documentationUrl={DocsUrl};apiPricingUrl={SecondPageUrl}"
        });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["queryResult"].Should().Be("documentation-partial");
        result.Properties!["documentationPagesRead"].Should().Be("1");
        result.Properties!["documentationPagesUnreadable"].Should().Contain(SecondPageUrl);
        // The finding must say which page it could not read and that the conclusion is bounded
        // by what was read — "nothing is documented" is only true of the page that was.
        result.Error.Should().Contain(SecondPageUrl);
        result.Error.Should().Contain("could not be read");
        result.Error.Should().Contain("browser crashed");
        http.RequestedUrls.Should().Contain(SecondPageUrl);
    }

    [Fact]
    public async Task Execute_NoPageReadable_ReportsUnreadableAndNotSilence()
    {
        var http = new RoutedHttpMessageHandler();
        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            CreateUnrenderableFetcher((DocsUrl, "browser unavailable")).Object,
            new ProviderResearchCache());

        var ctx = MakeContext(DefaultTemplateParameters());

        var result = await action.ExecuteAsync(ctx);

        // Still a completed step: the tree routes on the counting method, and a failed read must
        // not mark the whole run unsuccessful. What changes is that the message stops describing
        // the provider and starts describing this run.
        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["queryResult"].Should().Be("documentation-unreadable");
        result.Properties!["documentationPagesRead"].Should().Be("0");
        result.Error.Should().Contain("Could not read any documentation page");
        result.Error.Should().Contain(DocsUrl);
        result.Error.Should().Contain("browser unavailable");
        ctx.State.Properties["modelCountMethod"].Should().Be("not-documented");
    }

    [Fact]
    public void CollectProviderHosts_ReadsUrlShapedParametersOnly()
    {
        // Free-text parameters quote third-party pages, so they must not widen the set of
        // hosts the action is willing to send a request to.
        var hosts = QueryDocumentedModelsEndpointAction.CollectProviderHosts(
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["baseUrl"] = "https://api.test.example.com/v1/",
                ["currentValue"] = "none",
                ["siblingUrls"] = "documentationUrl=https://docs.test.example.com/guide;apiPricingUrl=not-a-url",
                ["task"] = "Compare with https://thirdparty.example.com/pricing"
            });

        hosts.Should().BeEquivalentTo(new[]
        {
            "test.example.com", "api.test.example.com", "docs.test.example.com"
        });
        hosts.Should().NotContain("thirdparty.example.com");
    }

    [Fact]
    public void IsProbeableEndpoint_KeepsProviderHostsAndDropsForeignAndDownloadableUrls()
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test.example.com" };

        QueryDocumentedModelsEndpointAction.IsProbeableEndpoint(CatalogUrl, hosts).Should().BeTrue();
        QueryDocumentedModelsEndpointAction.IsProbeableEndpoint(CatalogUrl + "/", hosts).Should().BeTrue();
        // A catalog hosted on a subdomain of the provider site is still the provider's, and a
        // ".json" catalog file is a legitimate answer — its body decides, not its extension.
        QueryDocumentedModelsEndpointAction.IsProbeableEndpoint(
            "https://api.test.example.com/models.json", hosts).Should().BeTrue();

        QueryDocumentedModelsEndpointAction.IsProbeableEndpoint(
            "https://other.example/v1/models", hosts).Should().BeFalse();
        QueryDocumentedModelsEndpointAction.IsProbeableEndpoint(
            "https://test.example.com/assets/models.png", hosts).Should().BeFalse();
        QueryDocumentedModelsEndpointAction.IsProbeableEndpoint("not a url", hosts).Should().BeFalse();
        QueryDocumentedModelsEndpointAction.IsProbeableEndpoint(CatalogUrl, new HashSet<string>()).Should().BeFalse();
    }

    [Fact]
    public void ExtractCandidateEndpoints_OrdersCodeSamplesFirstAndSkipsForeignHosts()
    {
        // An integration guide states the endpoint in a curl example; the same URL mentioned
        // again in prose is the same candidate, and a link to someone else's catalog is not a
        // candidate at all.
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test.example.com" };
        var markdown = """
            Endpoint: `https://api.test.example.com/v1/models`

            ```bash
            curl https://eu.test.example.com/v1/models
            ```

            Chat: https://eu.test.example.com/v1/chat/completions
            Also see https://api.test.example.com/v1/models
            Upstream: https://openai.example.com/v1/models
            """;

        var urls = QueryDocumentedModelsEndpointAction.ExtractCandidateEndpoints(markdown, hosts);

        urls.Should().Equal(
            "https://api.test.example.com/v1/models",
            "https://eu.test.example.com/v1/models",
            "https://eu.test.example.com/v1/chat/completions");
    }

    [Fact]
    public void ExtractCandidateEndpoints_PrefersUrlsPrintedInCodeElementsOnAnHtmlPage()
    {
        // A page read as HTML is mostly chrome — language alternates, canonical self-links,
        // navigation. Its code elements are the part that carries what the page states, so an
        // endpoint printed in a sample has to be asked before a header link.
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test.example.com" };
        var html = """
            <html><head>
            <link rel="alternate" hreflang="ar" href="https://test.example.com/docs/ar/go">
            <link rel="canonical" href="https://test.example.com/docs/en/go">
            </head><body>
            <p>See the guide at https://test.example.com/docs/en/go</p>
            <pre><code>curl https://test.example.com/zen/go/v1/models</code></pre>
            </body></html>
            """;

        var urls = QueryDocumentedModelsEndpointAction.ExtractCandidateEndpoints(html, hosts);

        urls.Should().NotBeEmpty();
        urls[0].Should().Be(CatalogUrl);
    }

    [Fact]
    public async Task Execute_DocumentedPathDoesNotSayModels_CountsWhatItReturns()
    {
        // The path is not evidence, the body is. A provider that publishes its catalog under
        // another name has to be counted, exactly as one whose "/models" URL answers with an
        // error body has to be rejected.
        const string CatalogueUrl = "https://test.example.com/api/model-catalogue";

        var http = new RoutedHttpMessageHandler();
        http.Route(CatalogueUrl, HttpStatusCode.OK, """{"models":[{"name":"m1"},{"name":"m2"}]}""");

        var fetcher = CreateFetcher(DocsUrl, $"```bash\ncurl {CatalogueUrl}\n```\n");
        var action = new QueryDocumentedModelsEndpointAction(
            new HttpClient(http),
            fetcher.Object,
            new ProviderResearchCache());

        var ctx = MakeContext(DefaultTemplateParameters());

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.Properties!["modelCount"].Should().Be("2");
        result.Properties!["url"].Should().Be(CatalogueUrl);
    }

    [Fact]
    public void ParseModelList_SupportsEveryCatalogShapeSeenInPractice()
    {
        QueryDocumentedModelsEndpointAction.ParseModelList("""{"data":[{"id":"m1"},{"name":"m2"}]}""")
            .Count.Should().Be(2);
        QueryDocumentedModelsEndpointAction.ParseModelList("""{"models":[{"slug":"m1"}]}""")
            .Count.Should().Be(1);
        QueryDocumentedModelsEndpointAction.ParseModelList("""["m1","m2","m3"]""")
            .Count.Should().Be(3);
    }

    [Fact]
    public void ParseModelList_NonJsonBody_QuotesWhatCameBack()
    {
        var (count, _, error) = QueryDocumentedModelsEndpointAction.ParseModelList("Not Found");

        count.Should().Be(0);
        error.Should().Contain("\"Not Found\"");
    }

    [Fact]
    public void ParseModelList_JsonWithoutACatalog_SaysSo()
    {
        var (count, _, error) = QueryDocumentedModelsEndpointAction.ParseModelList("""{"error":"nope"}""");

        count.Should().Be(0);
        error.Should().Contain("no 'data' or 'models' array");
    }
}
