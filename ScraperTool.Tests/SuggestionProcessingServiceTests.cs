using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using FluentAssertions;

using Moq;

using ScraperTool.Data;
using ScraperTool.Data.Repositories;
using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Services.UrlResearch;
using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.Validation.Checks;

using WebTools.NET.Abstractions;

namespace ScraperTool.Tests;

public class SuggestionProcessingServiceTests
{
    private readonly Mock<IValidationIssueRepository> _issueRepo = new();
    private readonly Mock<ITokenUsageRepository> _tokenRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IProviderCatalog> _catalog = new();
    private readonly Mock<IUrlResearchService> _research = new();
    private readonly Mock<IGeoRegionProvider> _regionProvider = new();
    private readonly Mock<IWebContentFetcher> _fetcher = new();
    private readonly Mock<IOperationLogger> _uiLogger = new();
    private readonly Mock<IPricingPageVerifier> _pricingVerifier = new();

    private SuggestionProcessingService CreateService()
    {
        var configMock = new Mock<IAiFixConfiguration>();
        configMock.Setup(c => c.HasApiKey).Returns(true);
        configMock.Setup(c => c.HasProviderSelection).Returns(true);
        configMock.Setup(c => c.IsModelConfigured).Returns(true);
        configMock.Setup(c => c.PrimaryModel).Returns("primary-model");
        configMock.Setup(c => c.FallbackModel).Returns("fallback-model");
        configMock.Setup(c => c.SelectedProviderId).Returns("test-provider");

        _regionProvider.Setup(r => r.DetectRegionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("us");

        // Default: pricing verifier accepts any URL
        _pricingVerifier.Setup(v => v.VerifyPricingUrlAsync(
                It.IsAny<Uri>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ScraperTool.Services.Validation.IValidationIssueSink>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EPricingContentVerdict.HasPricing, "pricing content found"));

        var provider = new ProviderDefinition
        {
            Id = "openai",
            DisplayName = "openai",
            BaseUrl = "https://api.openai.com/v1",
            Protocol = EProviderProtocol.OpenAICompatible
        };
        _catalog.Setup(c => c.All).Returns(new[] { provider });
        _catalog.Setup(c => c.Get(It.IsAny<string>())).Returns(provider);
        _catalog.Setup(c => c.GetResearchMetadata(It.IsAny<string>()))
            .Returns(new ProviderResearchMetadata { Website = "https://openai.com" });

        var aiUrlFix = new AiUrlFixService(
            configMock.Object,
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

        return new SuggestionProcessingService(aiUrlFix, null!, _uiLogger.Object);
    }

    private static ValidationIssue CreateIssue() =>
        new(
            "openai.json",
            "BrokenUrl",
            "Field 'apiPricingUrl' URL returns HTTP 404. Current value: https://openai.com/old-pricing")
        { Field = "apiPricingUrl", CurrentValue = "https://openai.com/old-pricing" };

    [Fact]
    public async Task RunAiFixAsync_WhenSuggestionRejectedByValidation_DoesNotRetryWithFallback()
    {
        // Arrange — the model answers, but its suggestion fails validation.
        // Model recovery is the AI runtime's job, so no layer above it may
        // re-run the batch with a different model.
        var service = CreateService();
        var issue = CreateIssue();

        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UrlResearchResult(
                SuggestedValue: null,
                Reason: "AI suggested URL 'https://test.example.com' is not related to provider 'openai'",
                Success: false,
                TotalPromptTokens: 10,
                TotalCompletionTokens: 5,
                Steps: new List<string>(),
                Suggestions: new List<AiSuggestion>()));

        var progress = new Mock<IProgress<AiUrlFixProgress>>();

        // Act
        var result = await service.RunAiFixAsync(
            [issue],
            "primary-model",
            "fallback-model",
            progress.Object);

        // Assert — a single research run, no fallback retry
        result.FailedCount.Should().Be(1);
        _research.Verify(
            r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAiFixAsync_WhenResearchThrows_DoesNotRetryWithFallback()
    {
        // Arrange — research throws; even an exception does not justify a
        // fallback-model retry at this layer (the runtime handles inaccessibility)
        var service = CreateService();
        var issue = CreateIssue();

        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM chain exhausted"));

        var progress = new Mock<IProgress<AiUrlFixProgress>>();

        // Act — the per-item exception is swallowed by the fix service; the point
        // here is that no fallback-model retry happens at this layer
        var result = await service.RunAiFixAsync(
            [issue],
            "primary-model",
            "fallback-model",
            progress.Object);

        // Assert — a single research run, no fallback retry
        result.Suggestions.Should().BeEmpty();
        _research.Verify(
            r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAiFixAsync_WhenPrimaryNotConfigured_UsesFallbackModel()
    {
        // Arrange — no primary model configured: the fallback model is used as
        // the model (configuration selection, not failover)
        var service = CreateService();
        var issue = CreateIssue();
        string? usedModel = null;

        _research.Setup(r => r.ResearchAsync(It.IsAny<ResearchContext>(), It.IsAny<CancellationToken>()))
            .Callback<ResearchContext, CancellationToken>((ctx, _) => usedModel = ctx.ModelName)
            .ReturnsAsync(new UrlResearchResult(
                SuggestedValue: "https://openai.com/api/pricing",
                Reason: "Found updated pricing page",
                Success: true,
                TotalPromptTokens: 100,
                TotalCompletionTokens: 50,
                Steps: new List<string>(),
                Suggestions: new List<AiSuggestion>()));

        var progress = new Mock<IProgress<AiUrlFixProgress>>();

        // Act
        var result = await service.RunAiFixAsync(
            [issue],
            primaryModel: "",
            "fallback-model",
            progress.Object);

        // Assert
        result.Suggestions.Should().HaveCount(1);
        usedModel.Should().Be("fallback-model");
        progress.Verify(
            p => p.Report(It.Is<AiUrlFixProgress>(x => x.Message.Contains("Primary model not configured"))),
            Times.Once);
    }
}
