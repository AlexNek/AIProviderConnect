using AiCleverness.Models.DecisionTree;

using FluentAssertions;

using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

namespace ScraperTool.Tests;

/// <summary>
/// Tests for <see cref="CandidateUrlProvider"/>: the candidate queue is the budget the whole
/// research run spends from, so one page has to be one entry however many places quoted it.
/// </summary>
public class CandidateUrlProviderTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetCandidateUrls_SamePageQuotedTwoWays_KeepsOneEntryWithTheFirstSpelling()
    {
        // A search result and an in-page link to the same page used to arrive as two candidates,
        // and the tree judged the page twice.
        var data = new DataStore();
        Add(data, "a", "https://test.example.com/go", "CandidateLink", 0);
        Add(data, "b", "https://test.example.com/go/", "SearchResult", 1);
        Add(data, "c", "https://TEST.example.com/GO#pricing", "CandidateLink", 2);
        Add(data, "d", "https://test.example.com/docs/go", "CandidateLink", 3);

        var urls = new CandidateUrlProvider().GetCandidateUrls(data);

        urls.Should().Equal("https://test.example.com/go", "https://test.example.com/docs/go");
    }

    [Fact]
    public void GetCandidateUrls_KeepsEarliestDiscoveredOrder()
    {
        // The tree works the queue from the front, and the scan that filled it ranked pages by
        // how likely they were to answer — so the queue order is the plan, not an artefact.
        var data = new DataStore();
        Add(data, "late", "https://test.example.com/pricing", "CandidateLink", 5);
        Add(data, "early", "https://test.example.com/docs", "CandidateLink", 1);

        new CandidateUrlProvider().GetCandidateUrls(data)
            .Should().Equal("https://test.example.com/docs", "https://test.example.com/pricing");
    }

    [Fact]
    public void GetCandidateUrls_IgnoresOtherEvidenceAndBlankContent()
    {
        // PageText and ModelList evidence carries provider URLs in its content too; queueing
        // those would have the fetcher request a summary as if it were an address.
        var data = new DataStore();
        Add(data, "page", "https://test.example.com/go", "PageText", 0);
        Add(data, "blank", "   ", "CandidateLink", 1);
        Add(data, "kept", "https://test.example.com/docs", "SearchResult", 2);

        new CandidateUrlProvider().GetCandidateUrls(data)
            .Should().Equal("https://test.example.com/docs");
    }

    [Fact]
    public void GetCandidateUrls_DifferentQueriesAreDifferentPages()
    {
        var data = new DataStore();
        Add(data, "a", "https://test.example.com/search?q=models", "CandidateLink", 0);
        Add(data, "b", "https://test.example.com/search?q=pricing", "CandidateLink", 1);

        new CandidateUrlProvider().GetCandidateUrls(data).Should().HaveCount(2);
    }

    [Fact]
    public void GetCandidateUrls_RestoresTheAddressADecoratedLinkPointsAt()
    {
        // An href reaches the queue with its HTML entities intact and its click attribution
        // attached. The string handed over is the string the tree fetches, judges and proposes
        // storing, so a decorated header link used to win as that decorated address.
        var data = new DataStore();
        Add(data, "a", "https://console.test.example.com?utm_source=docs&amp;utm_medium=referral", "CandidateLink", 0);
        Add(data, "b", "https://console.test.example.com/", "CandidateLink", 1);

        new CandidateUrlProvider().GetCandidateUrls(data)
            .Should().Equal("https://console.test.example.com");
    }

    [Fact]
    public void GetCandidateUrls_DropsAttributionButKeepsAQueryThatSelectsContent()
    {
        var data = new DataStore();
        Add(data, "a", "https://test.example.com/search?q=pricing&amp;utm_source=nav", "CandidateLink", 0);

        new CandidateUrlProvider().GetCandidateUrls(data)
            .Should().Equal("https://test.example.com/search?q=pricing");
    }

    private static void Add(
        DataStore data,
        string id,
        string content,
        string type,
        int order)
    {
        data.Add(new DecisionData
        {
            Id = id,
            Source = "https://test.example.com",
            Type = type,
            Content = content,
            CreatedAt = BaseTime.AddSeconds(order)
        });
    }
}
