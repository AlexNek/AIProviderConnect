using System.Net;
using System.Net.Http;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using FluentAssertions;

using Moq;

using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.ViewModels;

namespace ScraperTool.Tests;

public class AiSetupViewModelTests
{
    private readonly Mock<IProviderCatalog> _catalogMock;
    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;

    public AiSetupViewModelTests()
    {
        _catalogMock = new Mock<IProviderCatalog>();
        _catalogMock.Setup(c => c.All).Returns(new List<ProviderDefinition>
        {
            new()
            {
                Id = "no-discovery",
                DisplayName = "No Discovery Provider",
                BaseUrl = "https://api.test.example.com/v1/",
                Protocol = EProviderProtocol.OpenAICompatible,
                HasModelDiscoveryApi = false
            }
        });

        _catalogMock.Setup(c => c.Get("no-discovery")).Returns(new ProviderDefinition
        {
            Id = "no-discovery",
            DisplayName = "No Discovery Provider",
            BaseUrl = "https://api.test.example.com/v1/",
            Protocol = EProviderProtocol.OpenAICompatible,
            HasModelDiscoveryApi = false
        });

        _catalogMock.Setup(c => c.GetResearchMetadata(It.IsAny<string>()))
            .Returns(new ProviderResearchMetadata());

        _settings = new AppSettings
        {
            ApiKey = "fake-api-key",
            SelectedProviderId = "no-discovery"
        };

        var handler = new StubHttpHandler();
        _httpClient = new HttpClient(handler);

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpClientFactoryMock.Setup(f => f.CreateClient(HttpConstants.AiApiHttpClientName))
            .Returns(_httpClient);
    }

    [Fact]
    public void PrimaryModelChanged_WhenProviderDoesNotSupportDiscovery_DoesNotThrow()
    {
        // Arrange
        var ai = new AiAnalysisService(_settings);
        var factory = new AIProviderFactory(
            _settings, _httpClientFactoryMock.Object, _catalogMock.Object);
        var vm = new AiSetupViewModel(_settings, _catalogMock.Object, ai, factory);

        // Act
        vm.PrimaryModel = "some-model";

        // Assert
        vm.PrimaryModelPriceText.Should().Be(
            "Price: not available (select model to capture pricing from provider)",
            "a non-discovery provider should return no models, so the price text shows the fallback");
        vm.LoadedModels.Should().BeEmpty(
            "the !SupportsModelDiscovery fast path should prevent model loading");
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
