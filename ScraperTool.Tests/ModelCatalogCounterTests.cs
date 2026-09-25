using FluentAssertions;

using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

namespace ScraperTool.Tests;

/// <summary>
/// Tests for the deterministic model-catalog counter used by the minModelCount tree.
/// </summary>
public class ModelCatalogCounterTests
{
    private static readonly Uri PageUri = new("https://test.example.com/models/all");

    private readonly ModelCatalogCounter _counter = new();

    [Fact]
    public void Count_ModelCollection_ReturnsDistinctEntryCount()
    {
        var markdown = """
            # Catalog
            - [Alpha](/models/alpha)
            - [Beta](/models/beta)
            - [Gamma](/models/gamma)
            - [Pricing](/pricing/overview)
            - [Docs](/docs/intro)
            """;

        var result = _counter.Count(markdown, PageUri);

        result.Should().NotBeNull();
        result!.Count.Should().Be(3);
        result.CollectionPath.Should().Be("/models");
    }

    [Fact]
    public void Count_SameModelLinkedTwice_CountedOnce()
    {
        var markdown = """
            - [Alpha](/models/alpha)
            - [Alpha documentation](/models/alpha#details)
            - [Beta](/models/beta)
            """;

        var result = _counter.Count(markdown, PageUri);

        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
    }

    [Fact]
    public void Count_QueryAndFragment_VariantsCollapseToOneEntry()
    {
        var markdown = """
            - [Alpha](/models/alpha?ref=nav)
            - [Alpha](/models/alpha#parameters)
            - [Beta](/models/beta)
            """;

        var result = _counter.Count(markdown, PageUri);

        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
    }

    [Fact]
    public void Count_OffSiteLinks_AreIgnored()
    {
        var markdown = """
            - [Alpha](/models/alpha)
            - [Beta](/models/beta)
            - [Partner model](https://partner.test.example.org/models/partner)
            """;

        var result = _counter.Count(markdown, PageUri);

        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
    }

    [Fact]
    public void Count_AssetAndAnchorLinks_AreIgnored()
    {
        var markdown = """
            - [Alpha](/models/alpha)
            - [Beta](/models/beta)
            - [Icon](/models/icon.png)
            - [Logo](/models/logo.svg)
            - [Jump](#model-list)
            - [Contact](mailto:sales@test.example.com)
            """;

        var result = _counter.Count(markdown, PageUri);

        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
    }

    [Fact]
    public void Count_ImageSyntax_IsNotTreatedAsAnEntry()
    {
        var markdown = """
            - [Alpha](/models/alpha)
            - [Beta](/models/beta)
            ![Gamma card](/models/gamma)
            """;

        var result = _counter.Count(markdown, PageUri);

        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
    }

    [Fact]
    public void Count_RelativeLinks_ResolvedAgainstThePageUrl()
    {
        var pageUri = new Uri("https://test.example.com/api/docs/models/all");
        var markdown = """
            - [Alpha](alpha)
            - [Beta](beta)
            """;

        var result = _counter.Count(markdown, pageUri);

        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result.CollectionPath.Should().Be("/api/docs/models");
    }

    [Fact]
    public void Count_LinkBackToThePageItself_IsNotAnEntry()
    {
        var markdown = """
            - [All models](/models/all)
            - [Alpha](/models/alpha)
            - [Beta](/models/beta)
            """;

        var result = _counter.Count(markdown, PageUri);

        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result.SampleModelNames.Should().NotContain("All models");
    }

    [Fact]
    public void Count_NoModelCollection_ReturnsNull()
    {
        var pageUri = new Uri("https://test.example.com/pricing");
        var markdown = """
            - [Overview](/docs/overview)
            - [Quickstart](/docs/quickstart)
            """;

        _counter.Count(markdown, pageUri).Should().BeNull();
    }

    [Fact]
    public void Count_SingleEntryCollection_ReturnsNull()
    {
        var pageUri = new Uri("https://test.example.com/catalog");

        _counter.Count("- [Alpha](/models/alpha)", pageUri).Should().BeNull();
    }

    [Fact]
    public void Count_ModelsPageWithDifferentlyNamedCollection_UsesTheDominantCluster()
    {
        // The page is a models listing, but each entry hangs off /llm/<slug>.
        var pageUri = new Uri("https://test.example.com/docs/models/all");
        var markdown = string.Join(
            "\n",
            Enumerable.Range(1, 8).Select(i => $"- [Entry {i}](/llm/entry-{i})"));

        var result = _counter.Count(markdown, pageUri);

        result.Should().NotBeNull();
        result!.Count.Should().Be(8);
        result.CollectionPath.Should().Be("/llm");
    }

    [Fact]
    public void Count_NonModelsPageWithDifferentlyNamedCollection_ReturnsNull()
    {
        var pageUri = new Uri("https://test.example.com/pricing");
        var markdown = string.Join(
            "\n",
            Enumerable.Range(1, 8).Select(i => $"- [Entry {i}](/llm/entry-{i})"));

        _counter.Count(markdown, pageUri).Should().BeNull();
    }

    [Fact]
    public void Count_EmptyMarkdown_ReturnsNull()
    {
        _counter.Count(string.Empty, PageUri).Should().BeNull();
        _counter.Count("   \n  ", PageUri).Should().BeNull();
    }

    [Fact]
    public void Count_WithoutPageUri_StillCountsRootRelativeLinks()
    {
        var markdown = """
            - [Alpha](/models/alpha)
            - [Beta](/models/beta)
            """;

        var result = _counter.Count(markdown, null);

        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
    }

    [Fact]
    public void Count_EmptyLinkText_FallsBackToTheUrlSlug()
    {
        var markdown = """
            - [](/models/alpha-slug)
            - [Beta](/models/beta)
            """;

        var result = _counter.Count(markdown, PageUri);

        result.Should().NotBeNull();
        result!.SampleModelNames.Should().Contain("alpha-slug");
    }

    [Fact]
    public void Count_PrefersTheLargestModelCollection()
    {
        var markdown = """
            - [Legacy one](/legacy/models/one)
            - [Legacy two](/legacy/models/two)
            - [Alpha](/api/docs/models/alpha)
            - [Beta](/api/docs/models/beta)
            - [Gamma](/api/docs/models/gamma)
            """;

        var result = _counter.Count(markdown, PageUri);

        result.Should().NotBeNull();
        result!.Count.Should().Be(3);
        result.CollectionPath.Should().Be("/api/docs/models");
    }
}
