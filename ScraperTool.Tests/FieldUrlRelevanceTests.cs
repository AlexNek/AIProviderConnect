using FluentAssertions;

using ScraperTool.Services.UrlResearch.DecisionTree.Quality;

namespace ScraperTool.Tests;

/// <summary>
/// Tests for <see cref="FieldUrlRelevance"/>: the field's address vocabulary, and the one comparison
/// over it that is strong enough to overrule a tree — a winner whose address says nothing about the
/// field, offered in place of a stored address that says something.
/// </summary>
public class FieldUrlRelevanceTests
{
    /// <summary>The vocabulary Config/field-definitions.json declares for apiPricingUrl.</summary>
    private static readonly string[] ApiPricingTerms = ["price", "billing", "plans", "costs"];

    /// <summary>The vocabulary Config/field-definitions.json declares for loginUrl.</summary>
    private static readonly string[] LoginTerms = ["signin", "sign-in", "sign in", "auth"];

    [Fact]
    public void BuildKeywords_SplitsTheFieldNameAndAddsTheConfiguredVocabulary()
    {
        FieldUrlRelevance.BuildKeywords("apiPricingUrl", ApiPricingTerms)
            .Should().Equal("api", "pricing", "price", "billing", "plans", "costs");
    }

    [Fact]
    public void BuildKeywords_DropsWordsThatDescribeAUrlRatherThanADestination()
    {
        // "url", "page" and "link" appear in every field name and in most anchor texts, so they
        // would rank everything alike.
        FieldUrlRelevance.BuildKeywords("modelsPageUrl", []).Should().Equal("models");
    }

    [Fact]
    public void BuildKeywords_FieldNameWithoutAVocabulary_ReturnsEmpty()
    {
        FieldUrlRelevance.BuildKeywords("url", []).Should().BeEmpty();
    }

    [Fact]
    public void GetPath_ReadsThePathOfAnAbsoluteAddressAndLowerCasesIt()
    {
        FieldUrlRelevance.GetPath("https://test.example.com/Docs/Pricing?utm_source=nav#plans")
            .Should().Be("/docs/pricing");
    }

    [Fact]
    public void GetPath_RelativeAddress_IsReadAsItStands()
    {
        FieldUrlRelevance.GetPath("/Pricing/Plans").Should().Be("/pricing/plans");
    }

    [Fact]
    public void CountKeywordMatches_CountsEachWordOnceHoweverOftenItAppears()
    {
        var keywords = FieldUrlRelevance.BuildKeywords("apiPricingUrl", ApiPricingTerms);

        FieldUrlRelevance.CountKeywordMatches(keywords, "/pricing/pricing-plans", "see pricing")
            .Should().Be(2);
    }

    [Fact]
    public void Winner_DocumentationEndpointPage_OverStoredPricingPage_IsRejected()
    {
        // The reported defect: a page documenting one endpoint lists that endpoint's token prices,
        // so the classifier called it api_pricing and the tree proposed it over the stored page.
        FieldUrlRelevance.WinnerSaysNothingWhileStoredDoes(
                "https://www.test.example.com/docs/v3/llms/chat-completions",
                "https://www.test.example.com/pricing",
                "apiPricingUrl",
                ApiPricingTerms)
            .Should().BeTrue();
    }

    [Fact]
    public void Winner_ConsoleRoot_OverStoredLoginPage_IsRejected()
    {
        // The second reported defect, once the queue has restored the address the header link
        // decorated: the console root renders a sign-in form, so it classified as login_page.
        FieldUrlRelevance.WinnerSaysNothingWhileStoredDoes(
                "https://console.test.example.com/",
                "https://console.test.example.com/login",
                "loginUrl",
                LoginTerms)
            .Should().BeTrue();
    }

    [Fact]
    public void WinnerUnderADocsPath_ThatNamesTheField_IsNotRejected()
    {
        // A path is a hint about a page and never proof of it: /docs/pricing displays prices at
        // several providers, so ranking two addresses that both name the field is not this
        // comparison's business.
        FieldUrlRelevance.WinnerSaysNothingWhileStoredDoes(
                "https://docs.test.example.com/developers/pricing",
                "https://www.test.example.com/pricing",
                "apiPricingUrl",
                ApiPricingTerms)
            .Should().BeFalse();
    }

    [Fact]
    public void WinnerThatNamesTheField_OverAStoredAddressThatDoesNot_IsNotRejected()
    {
        FieldUrlRelevance.WinnerSaysNothingWhileStoredDoes(
                "https://console.test.example.com/billing/plans",
                "https://console.test.example.com/",
                "apiPricingUrl",
                ApiPricingTerms)
            .Should().BeFalse();
    }

    [Fact]
    public void NeitherAddressNamingTheField_IsNotRejected()
    {
        // The documentationUrl vocabulary is the single word "documentation", which almost no
        // provider spells in its address — so the comparison stays inert for that field rather
        // than rejecting every winner it is offered.
        FieldUrlRelevance.WinnerSaysNothingWhileStoredDoes(
                "https://docs.test.example.com/v3/llms/chat-completions",
                "https://docs.test.example.com/v2/",
                "documentationUrl",
                [])
            .Should().BeFalse();
    }

    [Fact]
    public void FieldWithNoVocabulary_IsNotRejected()
    {
        FieldUrlRelevance.WinnerSaysNothingWhileStoredDoes(
                "https://test.example.com/",
                "https://test.example.com/anything",
                "url",
                [])
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "https://test.example.com/pricing")]
    [InlineData("", "https://test.example.com/pricing")]
    [InlineData("https://test.example.com/", null)]
    [InlineData("https://test.example.com/", "")]
    public void MissingAddressToCompareAgainst_IsNotRejected(string? winner, string? stored)
    {
        FieldUrlRelevance.WinnerSaysNothingWhileStoredDoes(winner, stored, "apiPricingUrl", ApiPricingTerms)
            .Should().BeFalse();
    }
}
