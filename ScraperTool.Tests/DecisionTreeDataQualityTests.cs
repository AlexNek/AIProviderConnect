using AiCleverness.Models.DecisionTree;

using FluentAssertions;

using Moq;

using ScraperTool.Services.UrlResearch.DecisionTree.Formatting;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

using WebTools.NET.ContentAnalysis.Abstractions;
using WebTools.NET.ContentAnalysis.Models;

namespace ScraperTool.Tests;

/// <summary>
/// Tests for data quality services used by the decision-tree URL research pipeline.
/// </summary>
public class DecisionTreeDataQualityTests
{
    // ── HtmlTagCleaner ──────────────────────────────────────────────

    [Fact]
    public void HtmlTagCleaner_StripsNestedTagsAndNormalizesWhitespace()
    {
        var cleaner = new HtmlTagCleaner();

        var result = cleaner.Clean(
            "  <span>Start</span><svg><path/></svg><div> nested <header>header</header> text </div>  ");

        result.Should().Be("Start nested header text");
    }

    [Fact]
    public void HtmlTagCleaner_DecodesHtmlEntities()
    {
        var cleaner = new HtmlTagCleaner();

        var result = cleaner.Clean("&amp; pricing &lt;test&gt;");

        result.Should().Be("& pricing <test>");
    }

    [Fact]
    public void HtmlTagCleaner_WithEmptyInput_ReturnsEmpty()
    {
        var cleaner = new HtmlTagCleaner();

        cleaner.Clean("").Should().BeEmpty();
        cleaner.Clean("   ").Should().BeEmpty();
    }

    [Fact]
    public void HtmlTagCleaner_WithPlainText_ReturnsUnchanged()
    {
        var cleaner = new HtmlTagCleaner();

        var result = cleaner.Clean("Plain text without markup");

        result.Should().Be("Plain text without markup");
    }

    // ── StringListFormatter ─────────────────────────────────────────

    [Fact]
    public void StringListFormatter_WithFewItems_ShowsAll()
    {
        var formatter = new StringListFormatter();

        var result = formatter.FormatSummary(new[] { "a", "b" }, 5, 80);

        result.Should().Be("2 items: a, b");
    }

    [Fact]
    public void StringListFormatter_WithManyItems_ShowsHeadAndTail()
    {
        var formatter = new StringListFormatter();

        var result = formatter.FormatSummary(new[] { "a", "b", "c", "d", "e", "f" }, 4, 80);

        result.Should().Be("6 items: a, b, e, f ...");
    }

    [Fact]
    public void StringListFormatter_TruncatesLongItems()
    {
        var formatter = new StringListFormatter();

        var result = formatter.FormatSummary(new[] { "very-long-item-value" }, 1, 10);

        result.Should().Be("1 item: very-lo...");
    }

    [Fact]
    public void StringListFormatter_WithEmptyList_ReturnsZeroItems()
    {
        var formatter = new StringListFormatter();

        var result = formatter.FormatSummary(Array.Empty<string>(), 5, 80);

        result.Should().Be("0 items");
    }

    // ── TextSummarizer ──────────────────────────────────────────────

    [Fact]
    public void TextSummarizer_WhenContentFits_ReturnsUnchanged()
    {
        var summarizer = new TextSummarizer();

        var result = summarizer.Summarize("short", 10);

        result.Should().Be("short");
    }

    [Fact]
    public void TextSummarizer_WhenContentTooLong_ReturnsTruncatedWithMarker()
    {
        var summarizer = new TextSummarizer();

        var result = summarizer.Summarize("hello world", 8);

        result.Should().Be("hello...");
    }

    // ── CandidateRegionQualityAssessor ──────────────────────────────

    [Fact]
    public void QualityAssessor_WithEmptyRegions_ReturnsFalse()
    {
        var assessor = new CandidateRegionQualityAssessor();

        assessor.HasMeaningfulContent(Array.Empty<HtmlCandidateRegion>()).Should().BeFalse();
    }

    [Fact]
    public void QualityAssessor_WithShortChromeLikeRegions_ReturnsFalse()
    {
        var assessor = new CandidateRegionQualityAssessor();
        var regions = new[]
        {
            CreateRegion("Filter by"),
            CreateRegion("Sort order"),
            CreateRegion("Items per page")
        };

        assessor.HasMeaningfulContent(regions).Should().BeFalse();
    }

    [Fact]
    public void QualityAssessor_WithSubstantiveRegions_ReturnsTrue()
    {
        var assessor = new CandidateRegionQualityAssessor();
        var regions = new[]
        {
            CreateRegion(new string('x', 150)),
            CreateRegion(new string('x', 150)),
            CreateRegion(new string('x', 150))
        };

        assessor.HasMeaningfulContent(regions).Should().BeTrue();
    }

    [Fact]
    public void QualityAssessor_WithDecorativeUnicodeSymbols_ReturnsFalse()
    {
        var assessor = new CandidateRegionQualityAssessor();
        var decorativePattern = "✳ ⟡ · ◎ ✦ · ⟡ ✳ · ◎ ✧ · ⟡ ✳ → · ◎ ✦ ⟡ · ✳ ✧ · ◎ ⟡ ✳ ";
        var regions = new[]
        {
            CreateRegion(string.Concat(Enumerable.Repeat(decorativePattern, 10))),
            CreateRegion(string.Concat(Enumerable.Repeat(decorativePattern, 10))),
            CreateRegion(string.Concat(Enumerable.Repeat(decorativePattern, 10)))
        };

        assessor.HasMeaningfulContent(regions).Should().BeFalse();
    }

    // ── CandidateUrlProvider ────────────────────────────────────────

    [Fact]
    public void CandidateUrlProvider_CollectsCandidateLinksAndSearchResults()
    {
        var provider = new CandidateUrlProvider();
        var data = new DataStore();
        var now = DateTimeOffset.UtcNow;
        data.Add(new DecisionData
        {
            Id = "link-1",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://test.example.com/a",
            CreatedAt = now
        });
        data.Add(new DecisionData
        {
            Id = "search-1",
            Source = "query",
            Type = "SearchResult",
            Content = "https://test.example.com/b",
            CreatedAt = now.AddSeconds(1)
        });

        var urls = provider.GetCandidateUrls(data);

        urls.Should().Equal("https://test.example.com/a", "https://test.example.com/b");
    }

    [Fact]
    public void CandidateUrlProvider_DeduplicatesUrlsCaseInsensitively()
    {
        var provider = new CandidateUrlProvider();
        var data = new DataStore();
        data.Add(new DecisionData
        {
            Id = "link-1",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://test.example.com/A",
            CreatedAt = DateTimeOffset.UtcNow
        });
        data.Add(new DecisionData
        {
            Id = "link-2",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://test.example.com/a",
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        });

        var urls = provider.GetCandidateUrls(data);

        urls.Should().ContainSingle();
    }

    [Fact]
    public void CandidateUrlProvider_IgnoresIrrelevantDataTypes()
    {
        var provider = new CandidateUrlProvider();
        var data = new DataStore();
        data.Add(new DecisionData
        {
            Id = "page-1",
            Source = "https://test.example.com",
            Type = "PageText",
            Content = "full page text",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var urls = provider.GetCandidateUrls(data);

        urls.Should().BeEmpty();
    }

    // ── UrlResearchDecisionDataPolicy ───────────────────────────────

    [Fact]
    public void UrlResearchPolicy_ForClassifyNode_FiltersNavigationEvidence()
    {
        var policy = new UrlResearchDecisionDataPolicy();
        var data = new DataStore();
        data.Add(new DecisionData
        {
            Id = "link-1",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://test.example.com/a",
            CreatedAt = DateTimeOffset.UtcNow
        });
        data.Add(new DecisionData
        {
            Id = "page-1",
            Source = "https://test.example.com/a",
            Type = "PageText",
            Content = "Pricing details",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var classifyNode = new DecisionNode
        {
            Type = EDecisionNodeType.Classify,
            Task = "classify",
            Answers = new[] { "a", "b" }
        };
        var context = new DecisionDataSelectionContext(
            new AiCleverness.Models.DecisionTree.DecisionTree { TreeId = "test", StartNodeId = "classify" },
            classifyNode,
            new DecisionState(),
            new Dictionary<string, string>());

        var selection = policy.Select(data.GetAll(), context);

        selection.Items.Should().ContainSingle();
        selection.Items[0].Type.Should().Be("PageText");
    }

    [Fact]
    public void UrlResearchPolicy_ForNonClassifyNode_KeepsAllEvidence()
    {
        var policy = new UrlResearchDecisionDataPolicy();
        var data = new DataStore();
        data.Add(new DecisionData
        {
            Id = "link-1",
            Source = "https://test.example.com",
            Type = "CandidateLink",
            Content = "https://test.example.com/a",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var actionNode = new DecisionNode
        {
            Type = EDecisionNodeType.Action,
            ActionKey = "test"
        };
        var context = new DecisionDataSelectionContext(
            new AiCleverness.Models.DecisionTree.DecisionTree { TreeId = "test", StartNodeId = "action" },
            actionNode,
            new DecisionState(),
            new Dictionary<string, string>());

        var selection = policy.Select(data.GetAll(), context);

        selection.Items.Should().ContainSingle();
    }

    [Fact]
    public void UrlResearchPolicy_ForClassifyNode_KeepsOnlyThePageJustFetched()
    {
        // PageText from earlier candidates must not reach the classifier: the bounding
        // policy fills the prompt oldest-first, so accumulated pages push the newest one
        // out and the classifier keeps re-judging stale content for every candidate.
        var policy = new UrlResearchDecisionDataPolicy();
        var data = new DataStore();
        AddPageText(data, "page-1", "https://test.example.com/first", "First page content");
        AddPageText(data, "page-2", "https://test.example.com/second", "Second page content");
        AddPageText(data, "page-3", "https://test.example.com/third", "Third page content");

        var state = new DecisionState();
        state.Properties["lastFetchedUrl"] = "https://test.example.com/third";

        var selection = policy.Select(data.GetAll(), MakeClassifyContext(state));

        selection.Items.Should().ContainSingle();
        selection.Items[0].Content.Should().Be("Third page content");
    }

    [Fact]
    public void UrlResearchPolicy_ForClassifyNode_WithoutLastFetchedUrl_KeepsNewestPage()
    {
        var policy = new UrlResearchDecisionDataPolicy();
        var data = new DataStore();
        AddPageText(data, "page-1", "https://test.example.com/first", "First page content");
        AddPageText(data, "page-2", "https://test.example.com/second", "Second page content");

        var selection = policy.Select(data.GetAll(), MakeClassifyContext(new DecisionState()));

        selection.Items.Should().ContainSingle();
        selection.Items[0].Content.Should().Be("Second page content");
    }

    [Fact]
    public void UrlResearchPolicy_ForClassifyNode_KeepsNonPageEvidence()
    {
        var policy = new UrlResearchDecisionDataPolicy();
        var data = new DataStore();
        data.Add(new DecisionData
        {
            Id = "fact-1",
            Source = "validation",
            Type = "KnownFact",
            Content = "https://test.example.com/current",
            CreatedAt = DateTimeOffset.UtcNow
        });
        AddPageText(data, "page-1", "https://test.example.com/first", "First page content");
        AddPageText(data, "page-2", "https://test.example.com/second", "Second page content");

        var state = new DecisionState();
        state.Properties["lastFetchedUrl"] = "https://test.example.com/first";

        var selection = policy.Select(data.GetAll(), MakeClassifyContext(state));

        selection.Items.Select(i => i.Type).Should().Equal("KnownFact", "PageText");
        selection.Items.Single(i => i.Type == "PageText").Content.Should().Be("First page content");
    }

    private static void AddPageText(DataStore data, string id, string source, string content)
    {
        data.Add(new DecisionData
        {
            Id = id,
            Source = source,
            Type = "PageText",
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static DecisionDataSelectionContext MakeClassifyContext(DecisionState state)
    {
        var classifyNode = new DecisionNode
        {
            Type = EDecisionNodeType.Classify,
            Task = "classify",
            Answers = new[] { "a", "b" }
        };

        return new DecisionDataSelectionContext(
            new AiCleverness.Models.DecisionTree.DecisionTree { TreeId = "test", StartNodeId = "classify" },
            classifyNode,
            state,
            new Dictionary<string, string>());
    }

    // ── CandidateRegionContentSelector ─────────────────────────────

    [Fact]
    public void ContentSelector_WithNullHtml_ReturnsMarkdownWithZeroRegions()
    {
        var analyzer = new Mock<IHtmlContentAnalyzer>();
        var assessor = new CandidateRegionQualityAssessor();
        var selector = new CandidateRegionContentSelector(analyzer.Object, assessor);

        var result = selector.Select("# Markdown content", null, null);

        result.Content.Should().Be("# Markdown content");
        result.RegionCount.Should().Be(0);
        analyzer.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void ContentSelector_WithEmptyHtml_ReturnsMarkdownWithZeroRegions()
    {
        var analyzer = new Mock<IHtmlContentAnalyzer>();
        var assessor = new CandidateRegionQualityAssessor();
        var selector = new CandidateRegionContentSelector(analyzer.Object, assessor);

        var result = selector.Select("# Markdown content", "  ", null);

        result.Content.Should().Be("# Markdown content");
        result.RegionCount.Should().Be(0);
    }

    [Fact]
    public void ContentSelector_WithSubstantiveRegions_ReturnsRegionText()
    {
        var regions = new[]
        {
            CreateRegion(new string('a', 150)),
            CreateRegion(new string('b', 150))
        };
        var analysisResult = new HtmlAnalysisResult(
            Array.Empty<HtmlTextBlock>(),
            Array.Empty<HtmlStructuredDataRecord>(),
            regions,
            new HtmlAnalysisMetadata(0, 0, false, false, 0, 0, 0, Array.Empty<string>()));

        var analyzer = new Mock<IHtmlContentAnalyzer>();
        analyzer.Setup(a => a.Analyze(
                It.IsAny<string>(),
                It.IsAny<HtmlAnalysisOptions?>(),
                It.IsAny<Uri?>()))
            .Returns(analysisResult);

        var assessor = new CandidateRegionQualityAssessor();
        var selector = new CandidateRegionContentSelector(analyzer.Object, assessor);

        var result = selector.Select("# Fallback", "<html>content</html>", null);

        result.Content.Should().Contain(new string('a', 150));
        result.Content.Should().Contain(new string('b', 150));
        result.RegionCount.Should().Be(2);
    }

    [Fact]
    public void ContentSelector_WithLowQualityRegions_FallsBackToMarkdown()
    {
        var regions = new[] { CreateRegion("Short") };
        var analysisResult = new HtmlAnalysisResult(
            Array.Empty<HtmlTextBlock>(),
            Array.Empty<HtmlStructuredDataRecord>(),
            regions,
            new HtmlAnalysisMetadata(0, 0, false, false, 0, 0, 0, Array.Empty<string>()));

        var analyzer = new Mock<IHtmlContentAnalyzer>();
        analyzer.Setup(a => a.Analyze(
                It.IsAny<string>(),
                It.IsAny<HtmlAnalysisOptions?>(),
                It.IsAny<Uri?>()))
            .Returns(analysisResult);

        var assessor = new CandidateRegionQualityAssessor();
        var selector = new CandidateRegionContentSelector(analyzer.Object, assessor);

        var result = selector.Select("# Full Markdown", "<html>chrome</html>", null);

        result.Content.Should().Be("# Full Markdown");
        result.RegionCount.Should().Be(1);
    }

    [Fact]
    public void ContentSelector_WhenAnalyzerThrows_FallsBackToMarkdown()
    {
        var analyzer = new Mock<IHtmlContentAnalyzer>();
        analyzer.Setup(a => a.Analyze(
                It.IsAny<string>(),
                It.IsAny<HtmlAnalysisOptions?>(),
                It.IsAny<Uri?>()))
            .Throws(new InvalidOperationException("parse error"));

        var assessor = new CandidateRegionQualityAssessor();
        var selector = new CandidateRegionContentSelector(analyzer.Object, assessor);

        var result = selector.Select("# Markdown fallback", "<html>bad</html>", null);

        result.Content.Should().Be("# Markdown fallback");
        result.RegionCount.Should().Be(0);
    }

    private static HtmlCandidateRegion CreateRegion(string text)
    {
        return new HtmlCandidateRegion(
            text,
            string.Empty,
            Array.Empty<string>(),
            1.0,
            1,
            new HtmlSourceLocation("body", "div", 0),
            false);
    }
}
