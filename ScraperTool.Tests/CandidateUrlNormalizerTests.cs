using FluentAssertions;

using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

namespace ScraperTool.Tests;

/// <summary>
/// Tests for <see cref="CandidateUrlNormalizer"/>: one page addressed two ways has to stay one
/// page, because a queue that holds it twice spends a fetch and an LLM classification on a page
/// it already judged, and a visited record that cannot recognise an address it wrote itself
/// un-visits pages that were read.
/// </summary>
public class CandidateUrlNormalizerTests
{
    private const string Reference = "https://test.example.com/go";

    [Theory]
    [InlineData("https://test.example.com/go/")]
    [InlineData("https://test.example.com/go///")]
    [InlineData("HTTPS://TEST.Example.COM/go")]
    [InlineData("https://test.example.com/go#endpoints")]
    [InlineData("https://test.example.com/GO")]
    [InlineData("  https://test.example.com/go/  ")]
    public void Normalize_SamePageWrittenDifferently_SharesOneKey(string url)
    {
        CandidateUrlNormalizer.Normalize(url)
            .Should().Be(CandidateUrlNormalizer.Normalize(Reference));
    }

    [Fact]
    public void Normalize_KeepsQueryBecauseItSelectsWhatThePageReturns()
    {
        // Language alternates are different renderings of a page, and the query is what picks
        // between them, so it must not be folded away.
        CandidateUrlNormalizer.Normalize("https://test.example.com/go?lang=ar")
            .Should().NotBe(CandidateUrlNormalizer.Normalize("https://test.example.com/go?lang=bs"));
        CandidateUrlNormalizer.Normalize("https://test.example.com/go?lang=ar")
            .Should().NotBe(CandidateUrlNormalizer.Normalize(Reference));
        // Case is folded across the whole address, query included.
        CandidateUrlNormalizer.Normalize("https://test.example.com/go?LANG=AR")
            .Should().Be(CandidateUrlNormalizer.Normalize("https://test.example.com/go?lang=ar"));
    }

    [Fact]
    public void Normalize_PathCaseDoesNotSplitOnePage()
    {
        // The queue has never treated a differently-cased spelling as a second page; a
        // normalisation that folded only the host would silently start queueing both.
        CandidateUrlNormalizer.Normalize("https://test.example.com/Go/")
            .Should().Be(CandidateUrlNormalizer.Normalize(Reference));
    }

    [Fact]
    public void Normalize_KeepsDistinctPagesDistinct()
    {
        CandidateUrlNormalizer.Normalize("https://test.example.com/docs/go")
            .Should().NotBe(CandidateUrlNormalizer.Normalize(Reference));
        CandidateUrlNormalizer.Normalize("https://docs.test.example.com/go")
            .Should().NotBe(CandidateUrlNormalizer.Normalize(Reference));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankInput_HasOneEmptyKey(string? url)
    {
        CandidateUrlNormalizer.Normalize(url).Should().Be(string.Empty);
    }

    [Fact]
    public void Normalize_TextThatIsNotAnHttpPage_StaysComparableWithoutBeingRewritten()
    {
        // Candidate entries are not all page addresses; a search result can quote a name or a
        // non-web scheme. Those still have to dedupe, without pretending a path rule applies.
        CandidateUrlNormalizer.Normalize("  Doc Home ").Should().Be("doc home");
        CandidateUrlNormalizer.Normalize("doc home")
            .Should().Be(CandidateUrlNormalizer.Normalize("Doc Home"));
        CandidateUrlNormalizer.Normalize("ftp://test.example.com/go/")
            .Should().Be("ftp://test.example.com/go/");
    }
}
