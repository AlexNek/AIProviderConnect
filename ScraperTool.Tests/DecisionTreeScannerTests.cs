using System.Net;

using AiCleverness.Models.DecisionTree;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using ScraperTool.Services.UrlResearch;
using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.UrlResearch.DecisionTree.Actions;
using ScraperTool.Services.UrlResearch.DecisionTree.Formatting;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Tests;

public class DecisionTreeScannerTests
{
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
    public async Task DeterministicLinkScanner_StripsHtmlTagsFromDescriptions()
    {
        var html = @"
<html>
<body>
<a href=""/pricing""><span>Pricing</span> <svg><path/></svg> Plans</a>
<a href=""/models""><div>Model <header>Library</header></div></a>
<a href=""/about"">About</a>
</body>
</html>";

        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, html);

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var scanner = new DeterministicLinkScanner(
            http,
            NullLogger<DeterministicLinkScanner>.Instance,
            new HtmlTagCleaner());

        var result = await scanner.ScanAsync("https://test.example.com");

        result.Error.Should().BeNull();
        result.Links.Should().HaveCount(3);

        var descriptions = result.Links.Select(l => l.Description).ToList();
        descriptions.Should().Contain("Pricing Plans");
        descriptions.Should().Contain("Model Library");
        descriptions.Should().Contain("About");
    }

    [Fact]
    public async Task DeterministicLinkScanner_LimitsNumberOfReturnedLinks()
    {
        var links = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, 100).Select(i => $"<a href=\"/page{i}\">Page {i}</a>"));
        var html = $"<html><body>{links}</body></html>";

        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, html);

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        var scanner = new DeterministicLinkScanner(
            http,
            NullLogger<DeterministicLinkScanner>.Instance,
            new HtmlTagCleaner());

        var result = await scanner.ScanAsync("https://test.example.com");

        result.Links.Should().HaveCountLessThanOrEqualTo(30);
    }

    [Fact]
    public async Task FetchNextCandidateAction_UsesCandidateLinkEvidenceFromDataStore()
    {
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                "https://test.example.com/pricing",
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: "# Pricing\nSubscribe for $10.",
                ErrorMessage: null,
                FinalUrl: "https://test.example.com/pricing"));

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
            Content = "https://test.example.com/pricing",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var ctx = MakeActionContext(data: data);

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["lastFetchedUrl"].Should().Be("https://test.example.com/pricing");
        ctx.State.Properties["visitedUrls"].Should().Be("https://test.example.com/pricing");
        ctx.State.Properties["lastFetchedContent"].Should().Be("# Pricing\nSubscribe for $10.");
    }

    [Fact]
    public async Task FetchNextCandidateAction_ProducesReadableStateValues()
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
                Success: true,
                Content: new string('x', 1000),
                ErrorMessage: null,
                FinalUrl: "https://test.example.com/page"));

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
            Content = "https://test.example.com/page",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var ctx = MakeActionContext(data: data);

        await action.ExecuteAsync(ctx);

        ctx.State.Properties["lastFetchedContent"].Should().BeOfType<string>();
        ctx.State.Properties["lastFetchedContent"]!.ToString()!.Length.Should().BeLessThan(1000);
        ctx.State.Properties["visitedUrls"].Should().BeOfType<string>();
    }

    [Fact]
    public async Task FetchNextCandidateAction_WhenSelectorReturnsMarkdown_StoresMarkdownInState()
    {
        var markdownContent = "# Pricing Page\nSubscribe now for $20/month.";
        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                It.IsAny<string>(),
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: markdownContent,
                ErrorMessage: null,
                FinalUrl: "https://test.example.com/pricing"));

        var mockSelector = new Mock<ICandidateRegionContentSelector>();
        mockSelector
            .Setup(s => s.Select(markdownContent, It.IsAny<string?>(), It.IsAny<Uri?>()))
            .Returns(new CandidateRegionContentResult(markdownContent, 1));

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
            Content = "https://test.example.com/pricing",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var ctx = MakeActionContext(data: data);

        await action.ExecuteAsync(ctx);

        ctx.State.Properties["lastFetchedContent"].Should().Be(markdownContent);
    }

    [Fact]
    public async Task ScanProviderLinksAction_SortsLinksByRelevanceToFieldKind()
    {
        var mockScanner = new Mock<IDeterministicLinkScanner>();
        mockScanner
            .Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/models", "Models"),
                    new("https://test.example.com/docs", "Documentation"),
                    new("https://test.example.com/pricing", "Pricing"),
                    new("https://test.example.com/blog", "Blog")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "subscriptionPricingUrl"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().HaveCount(3);
        // Blog links are filtered out — only /models, /docs, /pricing remain
        result.ProducedData![0].Content.Should().Be("https://test.example.com/pricing");
        result.ProducedData.Select(d => d.Content).Should().NotContain("https://test.example.com/blog");
    }

    [Fact]
    public async Task ScanProviderLinksAction_PenalisesApiReferenceAndModelPaths()
    {
        var mockScanner = new Mock<IDeterministicLinkScanner>();
        mockScanner
            .Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/docs/api-reference/video-v2", "Video API"),
                    new("https://test.example.com/pricing", "Pricing"),
                    new("https://test.example.com/models/text/m3", "M3 Model")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "apiPricingUrl"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        // /pricing should rank first despite /docs/api-reference/ matching "api"
        result.ProducedData![0].Content.Should().Be("https://test.example.com/pricing");
    }

    [Fact]
    public async Task ScanProviderLinksAction_PromotesPricingPageUnderDocsPath()
    {
        // Reproduces the Vercel AI Gateway miss: the real API pricing page lives at
        // /docs/ai-gateway/pricing. The blanket /docs/ penalty used to sink it below
        // generic marketing pages (/home, /ai-sdk), so the tree never reached it
        // within budget. A /docs/ path that names the destination must rank above them.
        var mockScanner = new Mock<IDeterministicLinkScanner>();
        mockScanner
            .Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/home", "Home"),
                    new("https://test.example.com/ai-sdk", "AI SDK"),
                    new("https://test.example.com/docs/ai-gateway/pricing", "AI Gateway Pricing"),
                    new("https://test.example.com/sandbox", "Sandbox")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "apiPricingUrl"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData![0].Content.Should().Be("https://test.example.com/docs/ai-gateway/pricing");
    }

    [Fact]
    public async Task ScanProviderLinksAction_FiltersBlogAndNewsPaths()
    {
        var mockScanner = new Mock<IDeterministicLinkScanner>();
        mockScanner
            .Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/pricing", "Pricing"),
                    new("https://test.example.com/blog/new-model", "New Model Release"),
                    new("https://test.example.com/news/announcement", "Announcement"),
                    new("https://test.example.com/articles/overview", "Overview"),
                    new("https://test.example.com/press/release-2024", "Press Release"),
                    new("https://test.example.com/about", "About")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "subscriptionPricingUrl"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        // /blog/, /news/, /articles/, /press/ are filtered out
        result.ProducedData.Should().HaveCount(2);
        result.ProducedData![0].Content.Should().Be("https://test.example.com/pricing");
        result.ProducedData[1].Content.Should().Be("https://test.example.com/about");
        result.ProducedData.Select(d => d.Content).Should().NotContain(
            new[]
            {
                "https://test.example.com/blog/new-model",
                "https://test.example.com/news/announcement",
                "https://test.example.com/articles/overview",
                "https://test.example.com/press/release-2024"
            });
    }

    [Fact]
    public async Task ScanProviderLinksAction_WithoutFieldKind_KeepsScanOrder()
    {
        var mockScanner = new Mock<IDeterministicLinkScanner>();
        mockScanner
            .Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/models", "Models"),
                    new("https://test.example.com/pricing", "Pricing")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string> { ["providerUrl"] = "https://test.example.com" });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        // No fieldKind → original scan order preserved
        result.ProducedData![0].Content.Should().Be("https://test.example.com/models");
        result.ProducedData[1].Content.Should().Be("https://test.example.com/pricing");
    }

    [Fact]
    public async Task FetchNextCandidateAction_EvidenceUsesSelectedContentNotRawMarkdown()
    {
        var rawMarkdown = "# Page\n" + string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Line {i}"));
        var selectedContent = "# Pricing\n$10/month";

        var mockFetcher = new Mock<IWebContentFetcher>();
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                It.IsAny<string>(),
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: rawMarkdown,
                ErrorMessage: null,
                FinalUrl: "https://test.example.com/pricing"));

        var mockSelector = new Mock<ICandidateRegionContentSelector>();
        mockSelector
            .Setup(s => s.Select(rawMarkdown, It.IsAny<string?>(), It.IsAny<Uri?>()))
            .Returns(new CandidateRegionContentResult(selectedContent, 1));

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
            Content = "https://test.example.com/pricing",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var ctx = MakeActionContext(data);

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().ContainSingle();
        // Evidence must use the region-selected content, not the raw markdown
        result.ProducedData![0].Content.Should().Be(selectedContent);
        result.ProducedData[0].Content.Should().NotBe(rawMarkdown);
    }

    // ── Sibling URL scanning ─────────────────────────────────────────

    [Fact]
    public async Task ScanProviderLinksAction_ScansSiblingUrls_AndMergesResults()
    {
        var mockScanner = new Mock<IDeterministicLinkScanner>();

        // Homepage returns one link
        mockScanner
            .Setup(s => s.ScanAsync("https://test.example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/about", "About")
                },
                null));

        // Sibling URL (apiPricingUrl) returns additional links
        mockScanner
            .Setup(s => s.ScanAsync("https://platform.test.example.com/pricing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://platform.test.example.com/pricing/subscription", "Subscription Plans"),
                    new("https://platform.test.example.com/pricing/api", "API Pricing")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "subscriptionPricingUrl",
                ["siblingUrls"] = "apiPricingUrl=https://platform.test.example.com/pricing"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        // Homepage link + 2 sibling links + 1 sibling page URL itself = 4 candidates
        result.ProducedData.Should().HaveCount(4);
        result.ProducedData!.Select(d => d.Content).Should().Contain(
            "https://platform.test.example.com/pricing/subscription",
            "https://platform.test.example.com/pricing/api",
            "https://platform.test.example.com/pricing");
    }

    [Fact]
    public async Task ScanProviderLinksAction_ScansCurrentFieldPage_AndQueuesOnlyItsLinks()
    {
        // Reproduces the Vercel AI Gateway miss: the marketing page links no pricing page
        // within the scanner's link cap, but the field's own (invalid) docs page links the
        // real one in its navigation — "Pricing → /docs/ai-gateway/pricing".
        var mockScanner = new Mock<IDeterministicLinkScanner>();

        mockScanner
            .Setup(s => s.ScanAsync("https://test.example.com/ai-gateway", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/home", "Home"),
                    new("https://test.example.com/sandbox", "Sandbox")
                },
                null));

        mockScanner
            .Setup(s => s.ScanAsync("https://test.example.com/docs/ai-gateway", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/docs/ai-gateway/pricing", "Pricing — review model prices")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com/ai-gateway",
                ["fieldKind"] = "apiPricingUrl",
                ["currentValue"] = "https://test.example.com/docs/ai-gateway"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        // The pricing page discovered on the current field page ranks first.
        result.ProducedData![0].Content.Should().Be("https://test.example.com/docs/ai-gateway/pricing");
        // The current page itself — already proved wrong for the field — is never queued.
        result.ProducedData!.Select(d => d.Content).Should().NotContain("https://test.example.com/docs/ai-gateway");
    }

    [Fact]
    public async Task ScanProviderLinksAction_SkipsCurrentFieldPageScan_WhenValueIsNotAUrl()
    {
        // "-" (not applicable) and "none" (empty) are not pages; the scanner must be
        // called for the provider website only.
        var mockScanner = new Mock<IDeterministicLinkScanner>();
        mockScanner
            .Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/pricing", "Pricing")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "apiPricingUrl",
                ["currentValue"] = "-"
            });

        await action.ExecuteAsync(ctx);

        mockScanner.Verify(
            s => s.ScanAsync("https://test.example.com", It.IsAny<CancellationToken>()),
            Times.Once);
        mockScanner.Verify(
            s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScanProviderLinksAction_DeduplicatesLinks_AcrossHomepageAndSiblings()
    {
        var mockScanner = new Mock<IDeterministicLinkScanner>();

        // Both homepage and sibling return the same link
        var sharedLinks = new List<DeterministicLinkScanner.LinkInfo>
        {
            new("https://test.example.com/pricing", "Pricing"),
            new("https://test.example.com/about", "About")
        };

        mockScanner
            .Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(sharedLinks, null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "subscriptionPricingUrl",
                ["siblingUrls"] = "apiPricingUrl=https://test.example.com/pricing"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        // Deduplication: /pricing and /about appear only once each
        result.ProducedData.Should().HaveCount(2);
    }

    [Fact]
    public async Task ScanProviderLinksAction_KeepsRedirectTargetFirst_WhenSiblingPointsToSameUrl()
    {
        // Validation already proved the field resolves to a known sibling page. That page
        // must stay the first candidate classified — re-inserting the duplicate sibling
        // behind the pinned entry used to demote it below arbitrary scanned links.
        var mockScanner = new Mock<IDeterministicLinkScanner>();

        mockScanner
            .Setup(s => s.ScanAsync("https://test.example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/llms.txt", "Index")
                },
                null));

        mockScanner
            .Setup(s => s.ScanAsync("https://docs.test.example.com/pricing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>(),
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "subscriptionPricingUrl",
                ["siblingUrls"] = "apiPricingUrl=https://docs.test.example.com/pricing",
                ["redirectTargetUrl"] = "https://docs.test.example.com/pricing"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData![0].Content.Should().Be("https://docs.test.example.com/pricing");
        result.ProducedData
            .Count(d => d.Content == "https://docs.test.example.com/pricing")
            .Should().Be(1);
    }

    [Fact]
    public async Task ScanProviderLinksAction_TagsSiblingLinksWithSourceField()
    {
        var mockScanner = new Mock<IDeterministicLinkScanner>();

        mockScanner
            .Setup(s => s.ScanAsync("https://test.example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>(), null));

        mockScanner
            .Setup(s => s.ScanAsync("https://docs.test.example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://docs.test.example.com/subscription", "Subscription Info")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "subscriptionPricingUrl",
                ["siblingUrls"] = "documentationUrl=https://docs.test.example.com"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        // 1 sibling link + 1 sibling page URL itself = 2 candidates
        result.ProducedData.Should().HaveCount(2);
        // The sibling link description should be tagged with the source field name
        var siblingLink = result.ProducedData!.First(d => d.Content == "https://docs.test.example.com/subscription");
        siblingLink.Metadata!["description"].Should().Contain("[from documentationUrl]");
        // The sibling page URL itself should also be a candidate
        result.ProducedData!.Select(d => d.Content).Should().Contain("https://docs.test.example.com");
    }

    [Fact]
    public async Task ScanProviderLinksAction_SiblingScanFailure_DoesNotBreakAction()
    {
        var mockScanner = new Mock<IDeterministicLinkScanner>();

        // Homepage returns links normally
        mockScanner
            .Setup(s => s.ScanAsync("https://test.example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/pricing", "Pricing")
                },
                null));

        // Sibling URL throws
        mockScanner
            .Setup(s => s.ScanAsync("https://broken.test.example.com", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "subscriptionPricingUrl",
                ["siblingUrls"] = "apiPricingUrl=https://broken.test.example.com"
            });

        var result = await action.ExecuteAsync(ctx);

        // Action succeeds with homepage links despite sibling scan failure.
        // The sibling page URL itself is still added as a candidate (it's a
        // known-good page regardless of whether its links could be scanned).
        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().HaveCount(2);
        result.ProducedData!.Select(d => d.Content).Should().Contain(
            "https://test.example.com/pricing",
            "https://broken.test.example.com");
    }

    [Fact]
    public async Task ScanProviderLinksAction_WithRelevanceTerms_PromotesAuthLinkFromSiblingPage()
    {
        // The sign-in page is frequently reachable only from the page that sells access,
        // never from the home-page nav, and its URL says "auth" rather than "login". The
        // action ranks by the vocabulary the field's profile supplies; it holds none itself.
        var mockScanner = new Mock<IDeterministicLinkScanner>();

        mockScanner
            .Setup(s => s.ScanAsync("https://test.example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/zen", "Zen"),
                    new("https://test.example.com/download", "Download"),
                    new("https://test.example.com/data", "Data")
                },
                null));

        mockScanner
            .Setup(s => s.ScanAsync("https://test.example.com/go", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/auth", "Subscribe to Go")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "loginUrl",
                ["siblingUrls"] = "apiPricingUrl=https://test.example.com/go",
                ["relevanceTerms"] = "signin;sign-in;sign in;auth",
                // The complete profile the research service passes for this field: the sibling
                // page only links to the sign-in form, so it is not queued as a candidate.
                ["siblingPageMayBeAnswer"] = "false"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData![0].Content.Should().Be("https://test.example.com/auth");
    }

    [Fact]
    public async Task ScanProviderLinksAction_WithoutRelevanceTerms_KeepsScanOrder()
    {
        // A field with no configured vocabulary is ranked on its own name only. This is the
        // fallback every profile-less field kind has always had, and it must not invent words.
        var mockScanner = new Mock<IDeterministicLinkScanner>();

        mockScanner
            .Setup(s => s.ScanAsync("https://test.example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/auth", "Sign in"),
                    new("https://test.example.com/zen", "Zen")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "loginUrl"
            });

        var result = await action.ExecuteAsync(ctx);

        result.ProducedData!.Select(d => d.Content).Should().Equal(
            "https://test.example.com/auth",
            "https://test.example.com/zen");
    }

    [Fact]
    public async Task ScanProviderLinksAction_WhenSiblingPagesCannotBeAnswer_DoesNotQueueSiblingPages()
    {
        // A pricing or documentation page can be told to never be the answer itself, only
        // somewhere that links to one; queueing it costs a fetch and an LLM call that cannot
        // produce the answer.
        var mockScanner = new Mock<IDeterministicLinkScanner>();

        mockScanner
            .Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/auth", "Sign in")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            new ProviderResearchCache());

        var ctx = MakeActionContext(
            new DataStore(),
            new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com",
                ["fieldKind"] = "loginUrl",
                ["siblingUrls"] = "apiPricingUrl=https://test.example.com/go",
                ["siblingPageMayBeAnswer"] = "false"
            });

        var result = await action.ExecuteAsync(ctx);

        result.Status.Should().Be(DecisionActionStatus.Success);
        // The links gathered from the sibling page stay in the queue — only the page itself
        // is dropped.
        result.ProducedData!.Select(d => d.Content).Should().NotContain("https://test.example.com/go");
        result.ProducedData!.Select(d => d.Content).Should().Contain("https://test.example.com/auth");
    }

    private static DecisionActionContext MakeActionContext(
        DataStore data,
        Dictionary<string, string>? templateParameters = null)
    {
        return new DecisionActionContext(
            "test-node",
            "test-exec",
            templateParameters ?? new Dictionary<string, string>(),
            new DecisionState(),
            data);
    }

    private static DecisionActionContext MakeActionContext(DataStore data)
    {
        return new DecisionActionContext(
            "test-node",
            "test-exec",
            new Dictionary<string, string>(),
            new DecisionState(),
            data);
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
}
