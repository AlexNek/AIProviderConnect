using System.Text.Json;

using AiCleverness.Models.DecisionTree;

using FluentAssertions;

using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.UrlResearch.DecisionTree.Predicates;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

namespace ScraperTool.Tests;

public class DecisionTreePredicateTests
{
    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyParameters =
        new Dictionary<string, JsonElement>();

    private static readonly ICandidateUrlProvider CandidateUrlProvider = new CandidateUrlProvider();

    private static DecisionPredicateContext MakeContext(
        Dictionary<string, object?>? properties = null,
        IEnumerable<DecisionData>? data = null)
    {
        var state = new DecisionState();
        if (properties is not null)
        {
            foreach (var (key, value) in properties)
            {
                state.Properties[key] = value;
            }
        }

        var dataStore = new DataStore();
        if (data is not null)
        {
            foreach (var item in data)
            {
                dataStore.Add(item);
            }
        }

        return new DecisionPredicateContext("test-node", state, dataStore, EmptyParameters);
    }

    // ── HasCandidatesPredicate ──────────────────────────────────────

    [Fact]
    public void HasCandidates_WithCandidateLinkData_ReturnsTrue()
    {
        var predicate = new HasCandidatesPredicate(CandidateUrlProvider);
        var ctx = MakeContext(data: new[]
        {
            new DecisionData
            {
                Id = "link-1",
                Source = "https://test.example.com",
                Type = "CandidateLink",
                Content = "https://test.example.com/a",
                CreatedAt = DateTimeOffset.UtcNow
            }
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void HasCandidates_WithNoCandidateData_ReturnsFalse()
    {
        var predicate = new HasCandidatesPredicate(CandidateUrlProvider);
        var ctx = MakeContext();

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    [Fact]
    public void HasCandidates_WithSearchResultData_ReturnsTrue()
    {
        var predicate = new HasCandidatesPredicate(CandidateUrlProvider);
        var ctx = MakeContext(data: new[]
        {
            new DecisionData
            {
                Id = "search-1",
                Source = "query",
                Type = "SearchResult",
                Content = "https://test.example.com/a",
                CreatedAt = DateTimeOffset.UtcNow
            }
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    // ── HasUntriedCandidatesPredicate ───────────────────────────────

    /// <summary>
    /// A candidate entry stays in the data store after being tried, so the queue is never empty
    /// mid-run. These tests pin the difference this predicate exists for: the tree's "nothing left
    /// to try" branch has to become reachable.
    /// </summary>
    [Fact]
    public void HasUntriedCandidates_WithEmptyQueue_ReturnsFalse()
    {
        var predicate = new HasUntriedCandidatesPredicate(CandidateUrlProvider);

        predicate.Evaluate(MakeContext()).Should().BeFalse();
    }

    [Fact]
    public void HasUntriedCandidates_WithUnvisitedCandidate_ReturnsTrue()
    {
        var predicate = new HasUntriedCandidatesPredicate(CandidateUrlProvider);
        var ctx = MakeContext(
            properties: new Dictionary<string, object?> { ["visitedUrls"] = "https://test.example.com/a" },
            data: new[] { CandidateLink("https://test.example.com/b") });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void HasUntriedCandidates_WhenEveryCandidateVisited_ReturnsFalse()
    {
        var predicate = new HasUntriedCandidatesPredicate(CandidateUrlProvider);
        var ctx = MakeContext(
            properties: new Dictionary<string, object?>
            {
                ["visitedUrls"] = "https://test.example.com/a,https://test.example.com/b"
            },
            data: new[]
            {
                CandidateLink("https://test.example.com/a"),
                CandidateLink("https://test.example.com/b")
            });

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    [Fact]
    public void HasUntriedCandidates_VisitedUrlWrittenAnotherWay_StillCountsAsVisited()
    {
        // The fetch step skips pages by the address they point at, so the gate must agree with it:
        // a trailing slash or a different case is the same page already judged.
        var predicate = new HasUntriedCandidatesPredicate(CandidateUrlProvider);
        var ctx = MakeContext(
            properties: new Dictionary<string, object?> { ["visitedUrls"] = "https://TEST.example.com/a/" },
            data: new[] { CandidateLink("https://test.example.com/a") });

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    private static DecisionData CandidateLink(string url) => new()
    {
        Id = $"link-{Guid.NewGuid():N}",
        Source = "https://test.example.com",
        Type = "CandidateLink",
        Content = url,
        CreatedAt = DateTimeOffset.UtcNow
    };

    // ── LastVerifySucceededPredicate ────────────────────────────────

    [Fact]
    public void LastVerifySucceeded_WhenTrue_ReturnsTrue()
    {
        var predicate = new LastVerifySucceededPredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["lastVerifySucceeded"] = true
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void LastVerifySucceeded_WhenFalse_ReturnsFalse()
    {
        var predicate = new LastVerifySucceededPredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["lastVerifySucceeded"] = false
        });

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    [Fact]
    public void LastVerifySucceeded_WhenMissing_ReturnsFalse()
    {
        var predicate = new LastVerifySucceededPredicate();
        var ctx = MakeContext();

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    // ── HttpStatusIs401Predicate ────────────────────────────────────

    [Fact]
    public void HttpStatusIs401_WhenInt401_ReturnsTrue()
    {
        var predicate = new HttpStatusIs401Predicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["lastHttpStatus"] = 401
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void HttpStatusIs401_WhenString401_ReturnsTrue()
    {
        var predicate = new HttpStatusIs401Predicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["lastHttpStatus"] = "401"
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void HttpStatusIs401_When200_ReturnsFalse()
    {
        var predicate = new HttpStatusIs401Predicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["lastHttpStatus"] = 200
        });

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    [Fact]
    public void HttpStatusIs401_WhenMissing_ReturnsFalse()
    {
        var predicate = new HttpStatusIs401Predicate();
        var ctx = MakeContext();

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    // ── HasModelDiscoveryApiPredicate ───────────────────────────────

    [Fact]
    public void HasModelDiscoveryApi_WhenBoolTrue_ReturnsTrue()
    {
        var predicate = new HasModelDiscoveryApiPredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["hasModelDiscoveryApi"] = true
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void HasModelDiscoveryApi_WhenStringTrue_ReturnsTrue()
    {
        var predicate = new HasModelDiscoveryApiPredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["hasModelDiscoveryApi"] = "true"
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void HasModelDiscoveryApi_WhenFalse_ReturnsFalse()
    {
        var predicate = new HasModelDiscoveryApiPredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["hasModelDiscoveryApi"] = false
        });

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    [Fact]
    public void HasModelDiscoveryApi_WhenMissing_ReturnsFalse()
    {
        var predicate = new HasModelDiscoveryApiPredicate();
        var ctx = MakeContext();

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    // ── IsModelCountAccuratePredicate ───────────────────────────────

    [Fact]
    public void IsModelCountAccurate_WhenMatches_ReturnsTrue()
    {
        var predicate = new IsModelCountAccuratePredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["modelCountComparison"] = "matches"
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void IsModelCountAccurate_WhenOutdatedHigh_ReturnsFalse()
    {
        var predicate = new IsModelCountAccuratePredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["modelCountComparison"] = "outdated_high"
        });

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    [Fact]
    public void IsModelCountAccurate_WhenMissing_ReturnsFalse()
    {
        var predicate = new IsModelCountAccuratePredicate();
        var ctx = MakeContext();

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    // ── HasModelsPageUrlPredicate ───────────────────────────────────

    [Fact]
    public void HasModelsPageUrl_WhenBoolTrue_ReturnsTrue()
    {
        var predicate = new HasModelsPageUrlPredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["hasModelsPageUrl"] = true
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void HasModelsPageUrl_WhenStringTrue_ReturnsTrue()
    {
        var predicate = new HasModelsPageUrlPredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["hasModelsPageUrl"] = "true"
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void HasModelsPageUrl_WhenFalse_ReturnsFalse()
    {
        var predicate = new HasModelsPageUrlPredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["hasModelsPageUrl"] = false
        });

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    [Fact]
    public void HasModelsPageUrl_WhenMissing_ReturnsFalse()
    {
        var predicate = new HasModelsPageUrlPredicate();
        var ctx = MakeContext();

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    // ── HasKnownCurrentValuePredicate ───────────────────────────────

    [Fact]
    public void HasKnownCurrentValue_WhenNonEmptyString_ReturnsTrue()
    {
        var predicate = new HasKnownCurrentValuePredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["knownCurrentValue"] = "https://test.example.com"
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void HasKnownCurrentValue_WhenEmptyString_ReturnsFalse()
    {
        var predicate = new HasKnownCurrentValuePredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["knownCurrentValue"] = ""
        });

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    [Fact]
    public void HasKnownCurrentValue_WhenMissing_ReturnsFalse()
    {
        var predicate = new HasKnownCurrentValuePredicate();
        var ctx = MakeContext();

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    // ── HasDocumentedModelCountPredicate ────────────────────────────

    [Fact]
    public void HasDocumentedModelCount_WhenCountCameFromDocumentedEndpoint_ReturnsTrue()
    {
        var predicate = new HasDocumentedModelCountPredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["modelCountMethod"] = "documented-endpoint"
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void HasDocumentedModelCount_WhenNothingDocumented_ReturnsFalse()
    {
        var predicate = new HasDocumentedModelCountPredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["modelCountMethod"] = "not-documented"
        });

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    [Fact]
    public void HasDocumentedModelCount_WhenMethodBelongsToAnotherRung_ReturnsFalse()
    {
        // A count extracted from page prose by an earlier rung must not be mistaken for
        // confirmation from the provider's own catalog endpoint.
        var predicate = new HasDocumentedModelCountPredicate();
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["modelCountMethod"] = "llm",
            ["modelCount"] = 12
        });

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    // ── PageConfirmsSubscriptionPredicate ───────────────────────────

    [Fact]
    public void PageConfirmsSubscription_WhenFetchedPageMentionsSubscription_ReturnsTrue()
    {
        var cache = new ProviderResearchCache();
        cache.SetPageFetch("https://test.example.com/pricing", new PageFetchCacheEntry
        {
            Success = true,
            MarkdownContent = "Choose a monthly subscription plan, billed annually per seat."
        });
        var predicate = new PageConfirmsSubscriptionPredicate(cache);
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["lastFetchedUrl"] = "https://test.example.com/pricing"
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void PageConfirmsSubscription_WhenPayAsYouGoAndNoSubscription_ReturnsFalse()
    {
        // The bug this gate exists for: a per-token price list the classifier misreads as
        // recurring pricing. The page that was actually fetched declares usage-based billing
        // ("pay as you go") and never says "subscription", so the field is not applicable.
        var cache = new ProviderResearchCache();
        cache.SetPageFetch("https://test.example.com/pricing", new PageFetchCacheEntry
        {
            Success = true,
            MarkdownContent = "Pay as you go. $3 per 1M input tokens, prepaid credits, no minimum spend."
        });
        var predicate = new PageConfirmsSubscriptionPredicate(cache);
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["lastFetchedUrl"] = "https://test.example.com/pricing"
        });

        predicate.Evaluate(ctx).Should().BeFalse();
    }

    [Fact]
    public void PageConfirmsSubscription_WhenSubscriptionWordDiffersInCase_ReturnsTrue()
    {
        var cache = new ProviderResearchCache();
        cache.SetPageFetch("https://test.example.com/pricing", new PageFetchCacheEntry
        {
            Success = true,
            MarkdownContent = "Manage your SUBSCRIPTION billing at any time."
        });
        var predicate = new PageConfirmsSubscriptionPredicate(cache);
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["lastFetchedUrl"] = "https://test.example.com/pricing"
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void PageConfirmsSubscription_WhenNoPageWasFetched_ReturnsTrue()
    {
        // Nothing was fetched to judge — the gate must not override the classifier, so the
        // run keeps the prior verify-reachable path instead of guessing "-".
        var predicate = new PageConfirmsSubscriptionPredicate(new ProviderResearchCache());
        var ctx = MakeContext();

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void PageConfirmsSubscription_WhenFetchedContentUnavailable_ReturnsTrue()
    {
        var cache = new ProviderResearchCache();
        cache.SetPageFetch("https://test.example.com/pricing", new PageFetchCacheEntry
        {
            Success = false,
            MarkdownContent = null
        });
        var predicate = new PageConfirmsSubscriptionPredicate(cache);
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["lastFetchedUrl"] = "https://test.example.com/pricing"
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void PageConfirmsSubscription_WhenPayAsYouGoAndSubscriptionBothPresent_ReturnsTrue()
    {
        // The page offers BOTH usage-based and recurring billing, so a subscription genuinely
        // exists and the classifier's positive answer must stand.
        var cache = new ProviderResearchCache();
        cache.SetPageFetch("https://test.example.com/pricing", new PageFetchCacheEntry
        {
            Success = true,
            MarkdownContent = "Pay as you go with prepaid credits, or pick a monthly subscription plan."
        });
        var predicate = new PageConfirmsSubscriptionPredicate(cache);
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["lastFetchedUrl"] = "https://test.example.com/pricing"
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void PageConfirmsSubscription_WhenNeitherPayAsYouGoNorSubscription_ReturnsTrue()
    {
        // The page declares neither signal, so there is no positive evidence of usage-only
        // billing. Omitting "subscription" alone must NOT force "-": an ambiguous page is left
        // to the classifier instead of being overridden.
        var cache = new ProviderResearchCache();
        cache.SetPageFetch("https://test.example.com/pricing", new PageFetchCacheEntry
        {
            Success = true,
            MarkdownContent = "Contact our sales team for enterprise pricing and custom plans."
        });
        var predicate = new PageConfirmsSubscriptionPredicate(cache);
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["lastFetchedUrl"] = "https://test.example.com/pricing"
        });

        predicate.Evaluate(ctx).Should().BeTrue();
    }

    [Fact]
    public void PageConfirmsSubscription_WhenHyphenatedPayAsYouGoAndNoSubscription_ReturnsFalse()
    {
        // The hyphenated rendering of the same usage-based phrase must be recognized too.
        var cache = new ProviderResearchCache();
        cache.SetPageFetch("https://test.example.com/pricing", new PageFetchCacheEntry
        {
            Success = true,
            MarkdownContent = "Pay-as-you-go pricing: $2 per 1M tokens, prepaid credits, no minimum spend."
        });
        var predicate = new PageConfirmsSubscriptionPredicate(cache);
        var ctx = MakeContext(new Dictionary<string, object?>
        {
            ["lastFetchedUrl"] = "https://test.example.com/pricing"
        });

        predicate.Evaluate(ctx).Should().BeFalse();
    }
}
