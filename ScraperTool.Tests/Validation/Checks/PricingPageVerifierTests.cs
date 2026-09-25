using FluentAssertions;

using Moq;

using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Services.Validation;
using ScraperTool.Services.Validation.Checks;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Tests.Validation.Checks;

/// <summary>
/// Unit tests for <see cref="PricingPageVerifier"/> retirement detection on pricing pages.
/// </summary>
public class PricingPageVerifierTests
{
    private readonly Mock<IWebContentFetcher> _fetcherMock = new();
    private readonly Mock<IContentAnalyzer> _analyzerMock = new();

    private readonly PricingPageVerifier _verifier;

    public PricingPageVerifierTests()
    {
        _verifier = new PricingPageVerifier(_fetcherMock.Object, _analyzerMock.Object);
    }

    private static (ValidationIssueSink Sink, List<ValidationIssue> Issues) CreateSink()
    {
        var issues = new List<ValidationIssue>();
        return (new ValidationIssueSink(issues, null), issues);
    }

    [Fact]
    public async Task VerifyPricingUrlAsync_RetirementPage_EmitsServiceRetiredIssue()
    {
        // Arrange — page content that announces the service has been retired.
        var retirementContent = """
            # GitHub Models

            GitHub Models has been retired and is no longer available.
            The service has been discontinued as of September 2025.
            """;

        _fetcherMock
            .Setup(f => f.FetchAsAsync(
                "https://docs.github.com/en/github-models",
                EContentFormat.Markdown,
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(true, retirementContent, null, "https://docs.github.com/en/github-models"));

        var (sink, issues) = CreateSink();

        // Act
        var (verdict, reason) = await _verifier.VerifyPricingUrlAsync(
            new Uri("https://docs.github.com/en/github-models"),
            "https://docs.github.com/en/github-models",
            "github-models.json",
            ProviderJsonFields.ApiPricingUrl,
            sink);

        // Assert
        verdict.Should().Be(EPricingContentVerdict.NotEvaluated);
        reason.Should().Contain("retirement");
        issues.Should().ContainSingle()
            .Which.Code.Should().Be(ValidationIssueCodes.ServiceRetired);
        issues[0].Field.Should().Be(ProviderJsonFields.ApiPricingUrl);
        issues[0].CurrentValue.Should().Be("https://docs.github.com/en/github-models");
    }

    [Fact]
    public async Task VerifyPricingUrlAsync_NormalPricingPage_DoesNotEmitRetirementIssue()
    {
        // Arrange — a normal pricing page with no retirement signals.
        var pricingContent = "GPT-4: $0.03 per 1K input tokens, $0.06 per 1K output tokens.";

        _fetcherMock
            .Setup(f => f.FetchAsAsync(
                "https://test.example.com/pricing",
                EContentFormat.Markdown,
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(true, pricingContent, null, "https://test.example.com/pricing"));

        _analyzerMock
            .Setup(a => a.AnalyzeApiPricingContentAsync(It.IsAny<string>()))
            .ReturnsAsync((EPricingContentVerdict.HasPricing, "found per-token rates"));

        var (sink, issues) = CreateSink();

        // Act
        var (verdict, reason) = await _verifier.VerifyPricingUrlAsync(
            new Uri("https://test.example.com/pricing"),
            "https://test.example.com/pricing",
            "test.json",
            ProviderJsonFields.ApiPricingUrl,
            sink);

        // Assert
        verdict.Should().Be(EPricingContentVerdict.HasPricing);
        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyPricingUrlAsync_SingleRetirementPhrase_DoesNotTrigger()
    {
        // Arrange — only one retirement phrase (below the 2-signal threshold).
        var ambiguousContent = "This feature is no longer available in the free tier. Prices start at $10/month.";

        _fetcherMock
            .Setup(f => f.FetchAsAsync(
                "https://test.example.com/pricing",
                EContentFormat.Markdown,
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(true, ambiguousContent, null, "https://test.example.com/pricing"));

        _analyzerMock
            .Setup(a => a.AnalyzeApiPricingContentAsync(It.IsAny<string>()))
            .ReturnsAsync((EPricingContentVerdict.HasPricing, "found monthly price"));

        var (sink, issues) = CreateSink();

        // Act
        var (verdict, reason) = await _verifier.VerifyPricingUrlAsync(
            new Uri("https://test.example.com/pricing"),
            "https://test.example.com/pricing",
            "test.json",
            ProviderJsonFields.ApiPricingUrl,
            sink);

        // Assert — single phrase is below threshold; content analysis runs normally.
        verdict.Should().Be(EPricingContentVerdict.HasPricing);
        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyPricingUrlAsync_RetirementOnSubscriptionField_EmitsServiceRetiredIssue()
    {
        // Arrange — retirement content on the subscription pricing field.
        var retirementContent = """
            # Service Update

            Our platform has been discontinued. The service is no longer supported.
            """;

        _fetcherMock
            .Setup(f => f.FetchAsAsync(
                "https://test.example.com/plans",
                EContentFormat.Markdown,
                It.IsAny<int?>(),
                It.IsAny<ESanitizeLevel>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebContent(true, retirementContent, null, "https://test.example.com/plans"));

        var (sink, issues) = CreateSink();

        // Act
        var (verdict, reason) = await _verifier.VerifyPricingUrlAsync(
            new Uri("https://test.example.com/plans"),
            "https://test.example.com/plans",
            "test.json",
            ProviderJsonFields.SubscriptionPricingUrl,
            sink);

        // Assert
        verdict.Should().Be(EPricingContentVerdict.NotEvaluated);
        reason.Should().Contain("retirement");
        issues.Should().ContainSingle()
            .Which.Code.Should().Be(ValidationIssueCodes.ServiceRetired);
        issues[0].Field.Should().Be(ProviderJsonFields.SubscriptionPricingUrl);
    }
}
