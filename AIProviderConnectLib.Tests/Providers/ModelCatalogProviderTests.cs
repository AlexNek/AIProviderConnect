using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Providers;

public class ModelCatalogProviderTests
{
    [Theory]
    [InlineData("chat")]
    [InlineData("models")]
    [InlineData("stream")]
    public async Task Operations_ReportDistinctConfigurationErrorsBeforeSending(string operation)
    {
        foreach (var (enabled, baseUrl, apiKey, expectedCode) in new[]
        {
            (false, "", "", AiErrorCodes.ProviderDisabled),
            (true, " ", "", AiErrorCodes.NoBaseUrl),
            (true, "https://test.example.com/v1", " ", AiErrorCodes.NoApiKey)
        })
        {
            // Arrange
            using var handler = new CapturingHttpMessageHandler("[]");
            using var client = new HttpClient(handler);
            var catalog = new ProviderCatalog([
                new ProviderDefinition
                {
                    Id = "test-provider", DisplayName = "Test provider",
                    BaseUrl = "https://test.example.com/v1", Protocol = EProviderProtocol.Catalog
                }
            ]);
            var provider = new ModelCatalogProvider(client, new OpenAICompatibleProviderOptions
            {
                Enabled = enabled, BaseUrl = baseUrl, ApiKey = apiKey
            }, catalog, "test-provider");

            // Act
            Func<Task> act = async () =>
            {
                var request = new ChatCompletionRequest { Model = "test-model", Messages = [] };
                switch (operation)
                {
                    case "chat":
                        await provider.ChatAsync(request);
                        break;
                    case "models":
                        await provider.GetModelsAsync();
                        break;
                    case "stream":
                        await foreach (var _ in provider.StreamAsync(request)) { }
                        break;
                }
            };

            // Assert
            (await act.Should().ThrowAsync<AiException>()).Which.Code.Should().Be(expectedCode);
            handler.LastRequest.Should().BeNull();
        }
    }
}
