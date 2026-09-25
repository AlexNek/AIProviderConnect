using FluentAssertions;

using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

namespace ScraperTool.Tests;

/// <summary>
/// Tests for <see cref="CandidateUrlCleaner"/>: the address the queue hands over is the address the
/// tree fetches, judges and can propose storing, so it has to be the address the author wrote rather
/// than the string the document happened to contain.
/// </summary>
public class CandidateUrlCleanerTests
{
    [Fact]
    public void Clean_DecodesHtmlEntitiesInAQuery()
    {
        var cleaned = CandidateUrlCleaner.Clean("https://test.example.com/search?q=models&amp;lang=en");

        cleaned.Should().Be("https://test.example.com/search?q=models&lang=en");
    }

    [Fact]
    public void Clean_DropsClickAttributionAndKeepsThePage()
    {
        // The shape a documentation header link arrives with: an entity-escaped query in which
        // every parameter attributes the click and none selects what the page returns.
        var cleaned = CandidateUrlCleaner.Clean(
            "https://console.test.example.com?utm_source=docs&amp;utm_medium=referral&amp;utm_campaign=developers-pricing&amp;utm_content=header-api-console");

        cleaned.Should().Be("https://console.test.example.com");
    }

    [Fact]
    public void Clean_KeepsAQueryThatSelectsContent()
    {
        var cleaned = CandidateUrlCleaner.Clean("https://test.example.com/search?q=pricing&amp;utm_source=docs");

        cleaned.Should().Be("https://test.example.com/search?q=pricing");
    }

    [Fact]
    public void Clean_RecognisesAttributionParametersWhateverTheirCase()
    {
        var cleaned = CandidateUrlCleaner.Clean(
            "https://test.example.com/go?UTM_Source=news&gclid=abc&_ga=2.1&q=1");

        cleaned.Should().Be("https://test.example.com/go?q=1");
    }

    [Fact]
    public void Clean_LeavesPathCaseAndSlashesVerbatim()
    {
        // Cleaning restores an address, it does not tidy one: folding case and comparing two
        // spellings of one page is CandidateUrlNormalizer's job, and it never publishes a value
        // that gets written into a provider definition.
        var cleaned = CandidateUrlCleaner.Clean("https://TEST.example.com/Pricing/Plans/");

        cleaned.Should().Be("https://TEST.example.com/Pricing/Plans/");
    }

    [Fact]
    public void Clean_KeepsFragmentWhenTheQueryWasOnlyAttribution()
    {
        var cleaned = CandidateUrlCleaner.Clean("https://test.example.com/docs?utm_source=nav#pricing");

        cleaned.Should().Be("https://test.example.com/docs#pricing");
    }

    [Fact]
    public void Clean_DoesNotReadAQuestionMarkInsideAFragmentAsAQuery()
    {
        var cleaned = CandidateUrlCleaner.Clean("https://test.example.com/docs#faq?utm_source=nav");

        cleaned.Should().Be("https://test.example.com/docs#faq?utm_source=nav");
    }

    [Fact]
    public void Clean_TrimsSurroundingWhitespace()
    {
        var cleaned = CandidateUrlCleaner.Clean("  https://test.example.com/pricing  ");

        cleaned.Should().Be("https://test.example.com/pricing");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Clean_BlankInput_ReturnsEmpty(string? url)
    {
        CandidateUrlCleaner.Clean(url).Should().BeEmpty();
    }
}
