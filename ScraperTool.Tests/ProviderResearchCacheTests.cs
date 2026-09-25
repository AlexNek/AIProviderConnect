using System.Net;

using AiCleverness.Models.DecisionTree;

using FluentAssertions;

using Moq;

using ScraperTool.Services.UrlResearch;
using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.UrlResearch.DecisionTree.Actions;
using ScraperTool.Services.UrlResearch.DecisionTree.Formatting;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Tests;

/// <summary>
/// Tests for the batch-scoped ProviderResearchCache integration with
/// ScanProviderLinksAction and FetchNextCandidateAction.
/// </summary>
public class ProviderResearchCacheTests
{
    // ── ProviderResearchCache ────────────────────────────────────────

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var cache = new ProviderResearchCache();
        cache.SetScan("https://test.example.com", new ScanCacheEntry
        {
            Links = new List<(string, string)> { ("https://test.example.com/pricing", "Pricing") }
        });
        cache.SetPageFetch("https://test.example.com/page", new PageFetchCacheEntry
        {
            MarkdownContent = "content",
            Success = true
        });
        cache.RecordCatalogEndpoint("https://test.example.com", "https://api.test.example.com/v1/models");

        cache.Clear();

        cache.GetScan("https://test.example.com").Should().BeNull();
        cache.GetPageFetch("https://test.example.com/page").Should().BeNull();
        cache.GetCatalogEndpoints("https://test.example.com").Should().BeEmpty();
    }

    [Fact]
    public void GetScan_ReturnsNull_WhenNotCached()
    {
        var cache = new ProviderResearchCache();

        cache.GetScan("https://test.example.com").Should().BeNull();
    }

    [Fact]
    public void GetPageFetch_ReturnsNull_WhenNotCached()
    {
        var cache = new ProviderResearchCache();

        cache.GetPageFetch("https://test.example.com/page").Should().BeNull();
    }

    // ── Measured catalog endpoints ───────────────────────────────────

    [Fact]
    public void GetCatalogEndpoints_ReturnsNothing_WhenProviderUnknown()
    {
        var cache = new ProviderResearchCache();
        cache.RecordCatalogEndpoint("https://other.example.com", "https://other.example.com/v1/models");

        cache.GetCatalogEndpoints("https://test.example.com").Should().BeEmpty();
    }

    [Fact]
    public void RecordCatalogEndpoint_KeepsEachEndpointOnce()
    {
        // Two trees can measure the same catalog; the second recording must not queue it twice.
        var cache = new ProviderResearchCache();
        cache.RecordCatalogEndpoint("https://test.example.com", "https://api.test.example.com/v1/models");
        cache.RecordCatalogEndpoint("https://test.example.com", "https://API.test.example.com/v1/models");

        cache.GetCatalogEndpoints("https://test.example.com").Should().ContainSingle();
    }

    [Fact]
    public void RecordCatalogEndpoint_IgnoresBlankValues()
    {
        var cache = new ProviderResearchCache();
        cache.RecordCatalogEndpoint("https://test.example.com", " ");
        cache.RecordCatalogEndpoint(" ", "https://api.test.example.com/v1/models");

        cache.GetCatalogEndpoints("https://test.example.com").Should().BeEmpty();
    }

    // ── ScanProviderLinksAction cache integration ────────────────────

    [Fact]
    public async Task ScanProviderLinksAction_CacheHit_SkipsScannerCall()
    {
        var cache = new ProviderResearchCache();
        cache.SetScan("https://test.example.com", new ScanCacheEntry
        {
            Links = new List<(string, string)>
            {
                ("https://test.example.com/pricing", "Pricing"),
                ("https://test.example.com/models", "Models")
            }
        });

        var mockScanner = new Mock<IDeterministicLinkScanner>();
        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            cache);

        var ctx = MakeScanContext();
        var result = await action.ExecuteAsync(ctx);

        // Scanner should NOT have been called — data came from cache
        mockScanner.Verify(
            s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.Status.Should().Be(DecisionActionStatus.Success);
        result.ProducedData.Should().HaveCount(2);
    }

    [Fact]
    public async Task ScanProviderLinksAction_CacheMiss_StoresResultInCache()
    {
        var cache = new ProviderResearchCache();
        var mockScanner = new Mock<IDeterministicLinkScanner>();
        mockScanner
            .Setup(s => s.ScanAsync("https://test.example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeterministicLinkScanner.ScanResult(
                new List<DeterministicLinkScanner.LinkInfo>
                {
                    new("https://test.example.com/pricing", "Pricing")
                },
                null));

        var action = new ScanProviderLinksAction(
            mockScanner.Object,
            new StringListFormatter(),
            cache);

        var ctx = MakeScanContext();
        await action.ExecuteAsync(ctx);

        // Cache should now contain the scan result
        var cached = cache.GetScan("https://test.example.com");
        cached.Should().NotBeNull();
        cached!.Links.Should().HaveCount(1);
        cached.Links[0].Url.Should().Be("https://test.example.com/pricing");
    }

    // ── FetchNextCandidateAction cache integration ───────────────────

    [Fact]
    public async Task FetchNextCandidateAction_CacheHit_SkipsHttpFetch()
    {
        var cache = new ProviderResearchCache();
        cache.SetPageFetch("https://test.example.com/pricing", new PageFetchCacheEntry
        {
            MarkdownContent = "# Pricing\n$10/month",
            HtmlContent = "<html><body>Pricing</body></html>",
            Success = true,
            FinalUrl = "https://test.example.com/pricing"
        });

        var mockFetcher = new Mock<IWebContentFetcher>();
        var mockSelector = CreatePassthroughSelector();
        var action = new FetchNextCandidateAction(
            mockFetcher.Object,
            new TextSummarizer(),
            mockSelector.Object,
            new CandidateUrlProvider(),
            cache);

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

        // Fetcher should NOT have been called — data came from cache
        mockFetcher.Verify(
            f => f.FetchAsAsync(
                It.IsAny<string>(),
                It.IsAny<EContentFormat>(),
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        result.Status.Should().Be(DecisionActionStatus.Success);
        ctx.State.Properties["lastFetchedUrl"].Should().Be("https://test.example.com/pricing");
    }

    [Fact]
    public async Task FetchNextCandidateAction_CacheMiss_StoresResultInCache()
    {
        var cache = new ProviderResearchCache();

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
                Content: "# Pricing\n$10/month",
                ErrorMessage: null,
                FinalUrl: "https://test.example.com/pricing"));
        mockFetcher
            .Setup(f => f.FetchAsAsync(
                "https://test.example.com/pricing",
                EContentFormat.Html,
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(
                Success: true,
                Content: "<html><body>Pricing</body></html>",
                ErrorMessage: null,
                FinalUrl: "https://test.example.com/pricing"));

        var mockSelector = CreatePassthroughSelector();
        var action = new FetchNextCandidateAction(
            mockFetcher.Object,
            new TextSummarizer(),
            mockSelector.Object,
            new CandidateUrlProvider(),
            cache);

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
        await action.ExecuteAsync(ctx);

        // Cache should now contain the fetch result
        var cached = cache.GetPageFetch("https://test.example.com/pricing");
        cached.Should().NotBeNull();
        cached!.Success.Should().BeTrue();
        cached.MarkdownContent.Should().Be("# Pricing\n$10/month");
        cached.HtmlContent.Should().Be("<html><body>Pricing</body></html>");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static DecisionActionContext MakeScanContext(
        Dictionary<string, string>? templateParameters = null)
    {
        return new DecisionActionContext(
            "test-node",
            "test-exec",
            templateParameters ?? new Dictionary<string, string>
            {
                ["providerUrl"] = "https://test.example.com"
            },
            new DecisionState(),
            new DataStore());
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

    private static Mock<ICandidateRegionContentSelector> CreatePassthroughSelector()
    {
        var mock = new Mock<ICandidateRegionContentSelector>();
        mock.Setup(s => s.Select(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<Uri?>()))
            .Returns((string content, string? _, Uri? _) =>
                new CandidateRegionContentResult(content, 1));
        return mock;
    }
}
