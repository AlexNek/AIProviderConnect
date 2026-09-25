using FluentAssertions;

using ScraperTool.Services;
using ScraperTool.Services.Validation;

namespace ScraperTool.Tests;

public class ContentAnalyzerTests
{
    private readonly ContentAnalyzer _analyzer = new();

    [Theory]
    [InlineData("""<form><input type="password" name="pwd"></form>""")]
    [InlineData("""<input autocomplete="current-password" type="password">""")]
    public async Task AnalyzeLoginUrlAsync_PageTakesCredentials_ConfirmsLoginSurface(string html)
    {
        // Arrange / Act — the address names no endpoint, so only the form itself can confirm it.
        var (verdict, reason) = await _analyzer.AnalyzeLoginUrlAsync(
            html,
            "https://console.test.example.com/account");

        // Assert
        verdict.Should().Be(ELoginUrlVerdict.Confirmed);
        reason.Should().Contain("credential form");
    }

    [Theory]
    [InlineData("https://test.example.com/login")]
    [InlineData("https://test.example.com/users/sign-in")]
    [InlineData("https://accounts.test.example.com/ServiceLogin?continue=https://console.test.example.com")]
    [InlineData("https://test.example.com/unified-login")]
    [InlineData("https://login.test.example.com/")]
    [InlineData("https://test.example.com/app/?from=log-in")]
    public async Task AnalyzeLoginUrlAsync_AddressNamesAnAuthenticationEndpoint_ConfirmsLoginSurface(
        string url)
    {
        // Arrange — a page that renders its form client-side still exposes no credential field.
        var html = "<html><body><div id=\"root\"></div></body></html>";

        // Act
        var (verdict, reason) = await _analyzer.AnalyzeLoginUrlAsync(html, url);

        // Assert
        verdict.Should().Be(ELoginUrlVerdict.Confirmed);
        reason.Should().Contain("authentication endpoint");
    }

    [Fact]
    public async Task AnalyzeLoginUrlAsync_ContentPageWithNoAuthEvidence_ReportsNotLoginPage()
    {
        // Arrange — a documentation page: readable, and nothing on it authenticates anyone.
        var html = """
                   <html><body><h1>Installation guide</h1><p>Run the command below.</p></body></html>
                   """;

        // Act
        var (verdict, reason) = await _analyzer.AnalyzeLoginUrlAsync(
            html,
            "https://docs.test.example.com/en/latest/install");

        // Assert
        verdict.Should().Be(ELoginUrlVerdict.NotLoginPage);
        reason.Should().Contain("no credential form");
    }

    [Fact]
    public async Task AnalyzeLoginUrlAsync_PageOnlyLinksToSignIn_Abstains()
    {
        // Arrange — a console page carrying the site-wide sign-in link. GitHub, GitLab and every
        // other host put that link on every page, so its presence cannot tell a login page from a
        // repository page, and claiming it could is the defect this verdict exists to stop.
        var html = """
                   <html><body><a href="/login">Sign in</a><button>Log in</button></body></html>
                   """;

        // Act
        var (verdict, reason) = await _analyzer.AnalyzeLoginUrlAsync(
            html,
            "https://console.test.example.com/keys");

        // Assert
        verdict.Should().Be(ELoginUrlVerdict.NotEvaluated);
        reason.Should().Contain("only links");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnalyzeLoginUrlAsync_UnreadableBody_Abstains(string html)
    {
        // Act
        var (verdict, reason) = await _analyzer.AnalyzeLoginUrlAsync(
            html,
            "https://test.example.com/portal");

        // Assert
        verdict.Should().Be(ELoginUrlVerdict.NotEvaluated);
        reason.Should().Contain("no readable body");
    }

    [Fact]
    public async Task AnalyzeApiPricingContentAsync_PageDisplaysTokenAmounts_ConfirmsPricing()
    {
        // Arrange
        var content = ReadableBody("gpt-large: $0.10 per 1M input tokens, $0.30 per 1M output tokens.");

        // Act
        var (verdict, reason) = await _analyzer.AnalyzeApiPricingContentAsync(content);

        // Assert
        verdict.Should().Be(EPricingContentVerdict.HasPricing);
        reason.Should().Contain("$0.10");
    }

    [Theory]
    [InlineData("0.25 USD per million tokens")]
    [InlineData("12.5 EUR for a thousand characters")]
    [InlineData("\u00a32 per 1M tokens")]
    public async Task AnalyzeApiPricingContentAsync_AmountInAnyPricedForm_ConfirmsPricing(
        string pricedLine)
    {
        // Act — the amount alone is the evidence; no pricing vocabulary is needed beside it.
        var (verdict, _) = await _analyzer.AnalyzeApiPricingContentAsync(ReadableBody(pricedLine));

        // Assert
        verdict.Should().Be(EPricingContentVerdict.HasPricing);
    }

    [Fact]
    public async Task AnalyzeApiPricingContentAsync_PricingVocabularyWithoutAnAmount_ReportsNoPricing()
    {
        // Arrange — the documentation page this gate exists for: "pricing", "cost", "rate",
        // "model" and "token" fill its navigation and prose, and not one amount is on it.
        var content = ReadableBody(
            "See the pricing page for costs; rate limits and token counts per model are documented there.");

        // Act
        var (verdict, reason) = await _analyzer.AnalyzeApiPricingContentAsync(content);

        // Assert
        verdict.Should().Be(EPricingContentVerdict.NoPricing);
        reason.Should().Contain("no currency amount");
    }

    [Theory]
    [InlineData("Pricing")]
    [InlineData("You need to enable JavaScript to run this app.")]
    public async Task AnalyzeApiPricingContentAsync_BodyTooThinToHoldATable_Abstains(string content)
    {
        // Act — an unrendered shell carries neither the price nor enough text to miss one.
        var (verdict, reason) = await _analyzer.AnalyzeApiPricingContentAsync(content);

        // Assert
        verdict.Should().Be(EPricingContentVerdict.NotEvaluated);
        reason.Should().Contain("characters of page text were readable");
    }

    [Fact]
    public async Task AnalyzeApiPricingContentAsync_ThinBodyThatDisplaysAnAmount_ConfirmsPricing()
    {
        // Act — evidence outranks length, so raising the readability threshold can never turn a
        // price that was found into one that was missed.
        var (verdict, _) = await _analyzer.AnalyzeApiPricingContentAsync("0.10 per 1M input tokens");

        // Assert
        verdict.Should().Be(EPricingContentVerdict.HasPricing);
    }

    [Fact]
    public async Task AnalyzeSubscriptionPricingContentAsync_PricedPlans_ConfirmsPricing()
    {
        // Arrange
        var content = ReadableBody("Pro plan $20 per month, billed annually. Team plan $60 per seat.");

        // Act
        var (verdict, reason) = await _analyzer.AnalyzeSubscriptionPricingContentAsync(content);

        // Assert
        verdict.Should().Be(EPricingContentVerdict.HasPricing);
        reason.Should().Contain("$20");
    }

    [Fact]
    public async Task AnalyzeSubscriptionPricingContentAsync_PerTokenRatesWithoutPlans_ReportsNoPricing()
    {
        // Arrange — usage pricing is the apiPricingUrl field's subject, not this one's.
        var content = ReadableBody("Pay as you go: $0.10 per 1M input tokens, no minimum spend.");

        // Act
        var (verdict, reason) = await _analyzer.AnalyzeSubscriptionPricingContentAsync(content);

        // Assert
        verdict.Should().Be(EPricingContentVerdict.NoPricing);
        reason.Should().Contain("no plan price");
    }

    [Fact]
    public async Task AnalyzeSubscriptionPricingContentAsync_PlanWordsWithoutAnAmount_ReportsNoPricing()
    {
        // Arrange
        var content = ReadableBody("Choose the plan that fits your team — the free tier is included.");

        // Act
        var (verdict, _) = await _analyzer.AnalyzeSubscriptionPricingContentAsync(content);

        // Assert
        verdict.Should().Be(EPricingContentVerdict.NoPricing);
    }

    [Fact]
    public async Task AnalyzeSubscriptionPricingContentAsync_WordsContainingTierTokens_AreNotSignals()
    {
        // Arrange — "process" and "maximum" satisfied the old unbounded (pro|max) signal, so a
        // page with an amount and no plans at all used to be certified as a priced-plan page.
        var content = ReadableBody(
            "Our process is simple: $0.10 for every request, at maximum throughput.");

        // Act
        var (verdict, _) = await _analyzer.AnalyzeSubscriptionPricingContentAsync(content);

        // Assert
        verdict.Should().Be(EPricingContentVerdict.NoPricing);
    }

    /// <summary>
    /// Sentence with no digits and no pricing or plan vocabulary, long enough that a missing price
    /// is a real finding rather than a page that never rendered.
    /// </summary>
    private const string NeutralBody =
        "The gateway routes a request to the selected model and returns the completion. ";

    private static string ReadableBody(string pageText) =>
        string.Concat(Enumerable.Repeat(NeutralBody, 45)) + pageText;
}
