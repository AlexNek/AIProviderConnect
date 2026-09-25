using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using FluentAssertions;

using Moq;

using ScraperTool.Data;
using ScraperTool.Data.Entities;
using ScraperTool.Data.Repositories;
using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Services.UrlResearch;
using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.Validation.Checks;

using WebTools.NET.Abstractions;

namespace ScraperTool.Tests;

public class AiUrlFixServiceTests
{
    private readonly Mock<IAiFixConfiguration> _config = new();
    private readonly Mock<IValidationIssueRepository> _issueRepo = new();
    private readonly Mock<ITokenUsageRepository> _tokenRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IProviderCatalog> _catalog = new();
    private readonly Mock<IUrlResearchService> _research = new();
    private readonly Mock<IGeoRegionProvider> _regionProvider = new();
    private readonly Mock<IWebContentFetcher> _fetcher = new();
    private readonly Mock<IPricingPageVerifier> _pricingVerifier = new();

    private AiUrlFixService CreateService()
    {
        // Default: AI is fully configured
        _config.Setup(c => c.HasApiKey).Returns(true);
        _config.Setup(c => c.HasProviderSelection).Returns(true);
        _config.Setup(c => c.IsModelConfigured).Returns(true);
        _config.Setup(c => c.PrimaryModel).Returns("test-model");
        _config.Setup(c => c.SelectedProviderId).Returns("test-provider");

        _regionProvider.Setup(r => r.DetectRegionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("us");

        // Default: pricing verifier accepts any URL (tests that need rejection set up their own mock)
        _pricingVerifier.Setup(v => v.VerifyPricingUrlAsync(
                It.IsAny<Uri>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ScraperTool.Services.Validation.IValidationIssueSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EPricingContentVerdict.HasPricing, "pricing content found"));

        // Research metadata is owned by the catalog separately from the runtime definition; the
        // website the fix service reads now comes from there.
        _catalog.Setup(c => c.GetResearchMetadata(It.IsAny<string>()))
            .Returns<string>(id => new ProviderResearchMetadata { Website = $"https://{id}.com" });

        return new AiUrlFixService(
            _config.Object,
            new ModelPriceResolver(new AppSettings()),
            _issueRepo.Object,
            _tokenRepo.Object,
            _uow.Object,
            _catalog.Object,
            _research.Object,
            _regionProvider.Object,
            _fetcher.Object,
            new ProviderResearchCache(),
            new HttpClient(),
            _pricingVerifier.Object);
    }

    private static ValidationIssue CreateIssue(string fileName, string field, string? currentValue = null)
    {
        var value = currentValue ?? "";
        var message = $"Field '{field}' is not set. Set to - if no subscription plans exist.";
        return new ValidationIssue(fileName, "MissingSubscriptionPricingUrl", message)
        { Field = field, CurrentValue = value };
    }

    private static ProviderDefinition CreateProvider(string id)
    {
        return new ProviderDefinition
        {
            Id = id,
            DisplayName = id,
            BaseUrl = $"https://api.{id}.com/v1",
            Protocol = EProviderProtocol.OpenAICompatible
        };
    }

    [Fact]
    public async Task RunAsync_WhenResearchReturnsNotApplicable_GeneratesDashSuggestion()
    {
        // Arrange
        var service = CreateService();
        var provider = CreateProvider("deepinfra");
        var issue = CreateIssue("deepinfra.json", "subscriptionPricingUrl");

        _catalog.Setup(c => c.All).Returns(new[] { provider });
        _catalog.Setup(c => c.Get("deepinfra")).Returns(provider);

        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlResearchResult(
                SuggestedValue: null,
                Reason: "Provider only offers pay-as-you-go pricing",
                Success: true,
                TotalPromptTokens: 0,
                TotalCompletionTokens: 0,
                Steps: new List<string>(),
                Suggestions: new List<AiSuggestion>()));

        // Act
        var result = await service.RunAsync(
            new[] { issue },
            "test-model",
            null,
            CancellationToken.None);

        // Assert
        result.Suggestions.Should().HaveCount(1);
        result.Suggestions[0].SuggestedValue.Should().Be(ProviderJsonFields.NotApplicable);
        result.Suggestions[0].Field.Should().Be("subscriptionPricingUrl");
        result.Suggestions[0].ProviderId.Should().Be("deepinfra");
        result.Suggestions[0].Reason.Should().Contain("pay-as-you-go");

        issue.SuggestionStatus.Should().Be((int)IssueSuggestionStatus.Pending);
        issue.SuggestedValue.Should().Be("-");
    }

    [Fact]
    public async Task RunAsync_WhenNumericFieldReturnsNull_MarksAsFailed_NoDashSuggestion()
    {
        // Arrange
        var service = CreateService();
        var provider = CreateProvider("minimax");
        var issue = CreateIssue("minimax.json", "minModelCount");

        _catalog.Setup(c => c.All).Returns(new[] { provider });
        _catalog.Setup(c => c.Get("minimax")).Returns(provider);

        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlResearchResult(
                SuggestedValue: null,
                Reason: "Decision tree: completed with fallback; verdict=skip",
                Success: true,
                TotalPromptTokens: 0,
                TotalCompletionTokens: 0,
                Steps: new List<string>(),
                Suggestions: new List<AiSuggestion>()));

        // Act
        var result = await service.RunAsync(
            new[] { issue },
            "test-model",
            null,
            CancellationToken.None);

        // Assert — numeric fields must NOT get "-" (would create a revalidation loop)
        result.Suggestions.Should().BeEmpty();
        issue.SuggestionStatus.Should().Be((int)IssueSuggestionStatus.Failed);
        issue.SuggestedValue.Should().BeNull();
        // The tree ran to a conclusion and the conclusion was "no number found". The summary has
        // to report that as an answer about the provider, not as a breakdown in this run.
        issue.ResearchCompletedWithoutValue.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenResearchFails_MarksAsFailed_NoSuggestion()
    {
        // Arrange
        var service = CreateService();
        var provider = CreateProvider("deepinfra");
        var issue = CreateIssue("deepinfra.json", "subscriptionPricingUrl");
        // Left over from an earlier run: it must not describe this one's outcome.
        issue.ResearchCompletedWithoutValue = true;

        _catalog.Setup(c => c.All).Returns(new[] { provider });
        _catalog.Setup(c => c.Get("deepinfra")).Returns(provider);

        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlResearchResult(
                SuggestedValue: null,
                Reason: "Rate limited — could not complete research",
                Success: false,
                TotalPromptTokens: 100,
                TotalCompletionTokens: 50,
                Steps: new List<string>(),
                Suggestions: new List<AiSuggestion>()));

        // Act
        var result = await service.RunAsync(
            new[] { issue },
            "test-model",
            null,
            CancellationToken.None);

        // Assert
        result.Suggestions.Should().BeEmpty();
        result.FailedCount.Should().Be(1);
        issue.SuggestionStatus.Should().Be((int)IssueSuggestionStatus.Failed);
        // A run that broke is not a run that answered "no value" — the stale verdict is cleared.
        issue.ResearchCompletedWithoutValue.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WhenResearchThrows_LeavesItemPending_NoModelClassification()
    {
        // Arrange
        var service = CreateService();
        var provider = CreateProvider("deepinfra");
        var issue = CreateIssue("deepinfra.json", "subscriptionPricingUrl");

        _catalog.Setup(c => c.All).Returns(new[] { provider });
        _catalog.Setup(c => c.Get("deepinfra")).Returns(provider);

        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM chain exhausted"));

        // Act
        var result = await service.RunAsync(
            new[] { issue },
            "test-model",
            null,
            CancellationToken.None);

        // Assert — the exception is swallowed per item; this layer does not
        // classify model failures or mark the item (recovery is the AI runtime's
        // job), so the batch stays at its pre-run Pending status and FailedCount
        // only reflects items whose suggestion failed validation
        result.Suggestions.Should().BeEmpty();
        result.FailedCount.Should().Be(0);
        issue.SuggestionStatus.Should().Be((int)IssueSuggestionStatus.Pending);
    }

    [Fact]
    public async Task RunAsync_WhenResearchReturnsValidUrl_GeneratesPendingSuggestion()
    {
        // Arrange
        var service = CreateService();
        var provider = CreateProvider("openai");
        var issue = new ValidationIssue(
            "openai.json",
            "BrokenUrl",
            "Field 'apiPricingUrl' URL returns HTTP 404. Current value: https://openai.com/old-pricing")
        { Field = "apiPricingUrl", CurrentValue = "https://openai.com/old-pricing" };

        _catalog.Setup(c => c.All).Returns(new[] { provider });
        _catalog.Setup(c => c.Get("openai")).Returns(provider);

        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlResearchResult(
                SuggestedValue: "https://openai.com/api/pricing",
                Reason: "Found updated pricing page",
                Success: true,
                TotalPromptTokens: 200,
                TotalCompletionTokens: 100,
                Steps: new List<string>(),
                Suggestions: new List<AiSuggestion>()));

        // Act
        var result = await service.RunAsync(
            new[] { issue },
            "test-model",
            null,
            CancellationToken.None);

        // Assert
        result.Suggestions.Should().HaveCount(1);
        result.Suggestions[0].SuggestedValue.Should().Be("https://openai.com/api/pricing");
        result.Suggestions[0].Field.Should().Be("apiPricingUrl");
        issue.SuggestionStatus.Should().Be((int)IssueSuggestionStatus.Pending);
    }

    [Fact]
    public async Task RunAsync_WhenResearchConfirmsCurrentUrl_DismissesWithoutSuggestion()
    {
        // Arrange
        var service = CreateService();
        var provider = CreateProvider("openai");
        var issue = new ValidationIssue(
            "openai.json",
            "BrokenUrl",
            "Field 'apiPricingUrl' URL returns HTTP 403. Current value: https://openai.com/api/pricing")
        { Field = "apiPricingUrl", CurrentValue = "https://openai.com/api/pricing" };

        _catalog.Setup(c => c.All).Returns(new[] { provider });
        _catalog.Setup(c => c.Get("openai")).Returns(provider);

        // Research returns the same URL that's already set — confirming it's correct
        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlResearchResult(
                SuggestedValue: "https://openai.com/api/pricing",
                Reason: "URL is valid but returns 403 to bots",
                Success: true,
                TotalPromptTokens: 150,
                TotalCompletionTokens: 75,
                Steps: new List<string>(),
                Suggestions: new List<AiSuggestion>()));

        // Act
        var result = await service.RunAsync(
            new[] { issue },
            "test-model",
            null,
            CancellationToken.None);

        // Assert
        result.Suggestions.Should().BeEmpty();
        issue.SuggestionStatus.Should().Be((int)IssueSuggestionStatus.Dismissed);
    }

    [Fact]
    public async Task RunAsync_WhenAiNotConfigured_ReturnsEarlyWithAllFailed()
    {
        // Arrange
        _config.Setup(c => c.HasApiKey).Returns(false);
        _config.Setup(c => c.HasProviderSelection).Returns(true);
        _config.Setup(c => c.IsModelConfigured).Returns(true);
        _config.Setup(c => c.PrimaryModel).Returns("test-model");

        _catalog.Setup(c => c.All).Returns(Array.Empty<ProviderDefinition>());

        var service = new AiUrlFixService(
            _config.Object,
            new ModelPriceResolver(new AppSettings()),
            _issueRepo.Object,
            _tokenRepo.Object,
            _uow.Object,
            _catalog.Object,
            _research.Object,
            _regionProvider.Object,
            _fetcher.Object,
            new ProviderResearchCache(),
            new HttpClient(),
            _pricingVerifier.Object);

        var issue = CreateIssue("deepinfra.json", "subscriptionPricingUrl");

        // Act
        var result = await service.RunAsync(
            new[] { issue },
            "test-model",
            null,
            CancellationToken.None);

        // Assert
        result.FailedCount.Should().Be(1);
        result.Suggestions.Should().BeEmpty();
        _research.Verify(
            r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_WhenProviderNotFound_SkipsIssue()
    {
        // Arrange
        var service = CreateService();
        var issue = CreateIssue("unknown-provider.json", "subscriptionPricingUrl");

        _catalog.Setup(c => c.All).Returns(Array.Empty<ProviderDefinition>());
        _catalog.Setup(c => c.Get(It.IsAny<string>())).Returns((ProviderDefinition?)null);

        // Act
        var result = await service.RunAsync(
            new[] { issue },
            "test-model",
            null,
            CancellationToken.None);

        // Assert — provider not found doesn't create a suggestion
        result.Suggestions.Should().BeEmpty();
        result.TotalSuggestions.Should().Be(0);
        _research.Verify(
            r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_PassesRequestedModelNameToResearchContext()
    {
        // Arrange
        var service = CreateService();
        var provider = CreateProvider("deepseek");
        var issue = CreateIssue("deepseek.json", "subscriptionPricingUrl");

        _catalog.Setup(c => c.All).Returns(new[] { provider });
        _catalog.Setup(c => c.Get("deepseek")).Returns(provider);

        ResearchContext? captured = null;
        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .Callback<ResearchContext, CancellationToken>((ctx, _) => captured = ctx)
            .ReturnsAsync(new UrlResearchResult(
                SuggestedValue: null,
                Reason: "research failed",
                Success: false,
                TotalPromptTokens: 0,
                TotalCompletionTokens: 0,
                Steps: new List<string>(),
                Suggestions: new List<AiSuggestion>()));

        // Act — run with an explicit (fallback) model, as the fallback retry does
        await service.RunAsync(
            new[] { issue },
            "cohere/fallback-model:free",
            null,
            CancellationToken.None);

        // Assert — the requested model must reach the research agent so the
        // workflow LLM calls use it instead of the configured primary model
        captured.Should().NotBeNull();
        captured!.ModelName.Should().Be("cohere/fallback-model:free");
    }

    // ── One answer per provider field ─────────────────────────────────────────

    [Fact]
    public async Task RunAsync_TwoFindingsOnSameField_ResearchesOnceAndSuggestsOnce()
    {
        // The reported run: a self-hosted entry whose 'loginUrl' was flagged twice, by two
        // different checks, so the field's tree ran twice and the reviewer got two suggestions
        // that were identical in every field. One field has one repair value.
        // Arrange
        var service = CreateService();
        var provider = CreateProvider("testprovider");
        const string current = "https://test.example.com";
        var applicability = new ValidationIssue(
            "testprovider.json",
            "LoginUrlNotApplicableForSelfHosted",
            "Field 'loginUrl' must be - for a self-hosted provider")
        { Field = "loginUrl", CurrentValue = current };
        var duplication = new ValidationIssue(
            "testprovider.json",
            "LoginUrlSameAsWebsite",
            "Field 'loginUrl' is identical to 'website'")
        { Field = "loginUrl", CurrentValue = current };

        _catalog.Setup(c => c.All).Returns(new[] { provider });
        _catalog.Setup(c => c.Get("testprovider")).Returns(provider);

        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlResearchResult(
                SuggestedValue: null,
                Reason: "Decision tree: completed; verdict=-",
                Success: true,
                TotalPromptTokens: 0,
                TotalCompletionTokens: 0,
                Steps: new List<string>(),
                Suggestions: new List<AiSuggestion>()));

        // Act
        var result = await service.RunAsync(
            new[] { applicability, duplication },
            "test-model",
            null,
            CancellationToken.None);

        // Assert — one suggestion, but both findings carry the verdict so neither is left unanswered
        result.Suggestions.Should().HaveCount(1);
        _research.Verify(
            r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()),
            Times.Once);

        foreach (var issue in new[] { applicability, duplication })
        {
            issue.SuggestedValue.Should().Be(ProviderJsonFields.NotApplicable);
            issue.SuggestionStatus.Should().Be((int)IssueSuggestionStatus.Pending);
        }

        duplication.SuggestionReason.Should().Be("Decision tree: completed; verdict=-");
    }

    [Fact]
    public async Task RunAsync_FindingsOnDifferentFields_BothAreResearched()
    {
        // Collapsing must stay scoped to one field: two different fields of the same provider
        // have two different answers and two different trees.
        // Arrange
        var service = CreateService();
        var provider = CreateProvider("testprovider");
        var login = new ValidationIssue(
            "testprovider.json",
            "LoginUrlNotApplicableForSelfHosted",
            "Field 'loginUrl' must be - for a self-hosted provider")
        { Field = "loginUrl", CurrentValue = "https://test.example.com" };
        var website = new ValidationIssue(
            "testprovider.json",
            "WebsiteIsSubdomain",
            "Field 'website' is a page on the provider's own subdomain")
        { Field = "website", CurrentValue = "https://docs.test.example.com/latest/" };

        _catalog.Setup(c => c.All).Returns(new[] { provider });
        _catalog.Setup(c => c.Get("testprovider")).Returns(provider);

        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlResearchResult(
                SuggestedValue: null,
                Reason: "Decision tree: completed; verdict=-",
                Success: true,
                TotalPromptTokens: 0,
                TotalCompletionTokens: 0,
                Steps: new List<string>(),
                Suggestions: new List<AiSuggestion>()));

        // Act
        var result = await service.RunAsync(
            new[] { login, website },
            "test-model",
            null,
            CancellationToken.None);

        // Assert
        _research.Verify(
            r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        result.Suggestions.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_FirstFindingFailed_SecondStillGetsItsOwnAttempt()
    {
        // A broken research run is not a verdict, so it must not be handed to the next finding for
        // that field as if the provider had been answered.
        // Arrange
        var service = CreateService();
        var provider = CreateProvider("testprovider");
        var first = new ValidationIssue(
            "testprovider.json",
            "UrlTimeout",
            "Field 'loginUrl' timed out")
        { Field = "loginUrl", CurrentValue = "https://test.example.com" };
        var second = new ValidationIssue(
            "testprovider.json",
            "LoginUrlSameAsWebsite",
            "Field 'loginUrl' is identical to 'website'")
        { Field = "loginUrl", CurrentValue = "https://test.example.com" };

        _catalog.Setup(c => c.All).Returns(new[] { provider });
        _catalog.Setup(c => c.Get("testprovider")).Returns(provider);

        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlResearchResult(
                SuggestedValue: null,
                Reason: "Rate limited — could not complete research",
                Success: false,
                TotalPromptTokens: 0,
                TotalCompletionTokens: 0,
                Steps: new List<string>(),
                Suggestions: new List<AiSuggestion>()));

        // Act
        await service.RunAsync(
            new[] { first, second },
            "test-model",
            null,
            CancellationToken.None);

        // Assert
        _research.Verify(
            r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        second.SuggestionStatus.Should().Be((int)IssueSuggestionStatus.Failed);
    }

    // ── IsPathCompatibleWithField ────────────────────────────────────

    [Theory]
    [InlineData("https://example.com/blog/some-post", "apiPricingUrl")]
    [InlineData("https://example.com/news/announcement", "apiPricingUrl")]
    [InlineData("https://example.com/video/demo", "subscriptionPricingUrl")]
    [InlineData("https://example.com/articles/pricing-overview", "apiPricingUrl")]
    [InlineData("https://example.com/images/logo.png", "apiPricingUrl")]
    [InlineData("https://example.com/press/release-2024", "subscriptionPricingUrl")]
    [InlineData("https://example.com/careers/engineer", "apiPricingUrl")]
    [InlineData("https://example.com/about", "apiPricingUrl")]
    public void IsPathCompatibleWithField_PricingField_RejectsContentPaths(string url, string field)
    {
        AiUrlFixService.IsPathCompatibleWithField(url, field).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://example.com/pricing", "apiPricingUrl")]
    [InlineData("https://example.com/api/pricing", "apiPricingUrl")]
    [InlineData("https://example.com/plans", "subscriptionPricingUrl")]
    [InlineData("https://example.com/billing/overview", "apiPricingUrl")]
    [InlineData("https://example.com/docs", "apiPricingUrl")]
    [InlineData("https://example.com/", "website")]
    [InlineData("https://example.com/blog/post", "website")]
    public void IsPathCompatibleWithField_AcceptsCompatiblePaths(string url, string field)
    {
        AiUrlFixService.IsPathCompatibleWithField(url, field).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://example.com/pricing", "baseUrl")]
    [InlineData("https://example.com/docs", "baseUrl")]
    [InlineData("https://example.com/blog", "baseUrl")]
    [InlineData("https://example.com/login", "baseUrl")]
    [InlineData("https://example.com/models", "baseUrl")]
    [InlineData("https://example.com/playground", "baseUrl")]
    [InlineData("https://example.com/dashboard", "baseUrl")]
    public void IsPathCompatibleWithField_BaseUrl_RejectsContentPaths(string url, string field)
    {
        AiUrlFixService.IsPathCompatibleWithField(url, field).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://example.com/", "baseUrl")]
    [InlineData("https://example.com", "baseUrl")]
    [InlineData("https://example.com/v1", "baseUrl")]
    [InlineData("https://example.com/v2", "baseUrl")]
    [InlineData("https://example.com/api", "baseUrl")]
    [InlineData("https://example.com/api/v1", "baseUrl")]
    public void IsPathCompatibleWithField_BaseUrl_AcceptsApiEndpointPaths(string url, string field)
    {
        AiUrlFixService.IsPathCompatibleWithField(url, field).Should().BeTrue();
    }
}
