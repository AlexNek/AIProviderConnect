using System.Net;
using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Providers;

public class DecisionProviderTests
{
    private const string DecisionResponseJson = """
    {
        "id": "resp-1",
        "model": "typesafe/jev-1.13",
        "provider": "TypeSafe",
        "answers": {
            "priority": {
                "type": "noul",
                "noul": 0.7
            }
        },
        "usage": { "input_tokens": 10, "output_tokens": 4 }
    }
    """;

    private static DecisionRequest Request(string model = "typesafe/jev-1.13") => new()
    {
        Model = model,
        State = "A ticket about a billing error.",
        Questions = new Dictionary<string, DecisionQuestion>
        {
            ["priority"] = new() { Kind = EDecisionQuestionKind.Noul }
        }
    };

    private static DecisionProvider CreateProvider(
        CapturingHttpMessageHandler handler,
        string defaultModel = "typesafe/jev-1.13",
        string? decisionsBaseUrl = null)
    {
        var options = new DecisionProviderOptions
        {
            BaseUrl = "https://openrouter.example.com/api/",
            ApiKey = "fake-api-key",
            Enabled = true,
            DefaultModel = defaultModel,
            DecisionsBaseUrl = decisionsBaseUrl
        };
        return new DecisionProvider(new HttpClient(handler), options, Catalog(), "openrouter-decisions");
    }

    private static ProviderCatalog Catalog() => new(
    [
        new ProviderDefinition
        {
            Id = "openrouter-decisions",
            DisplayName = "Decisions provider",
            BaseUrl = "https://openrouter.example.com/api/",
            Protocol = EProviderProtocol.Decision
        }
    ]);

    [Fact]
    public void Provider_IsIDecisionProvider_AndSupportsDecisions()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);

        // Act
        var provider = CreateProvider(handler);

        // Assert
        provider.Should().BeAssignableTo<IDecisionProvider>();
        ((IDecisionProvider)provider).SupportsDecisions.Should().BeTrue();
    }

    [Fact]
    public async Task ChatAsync_Throws_ChatNotSupported()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var provider = CreateProvider(handler);

        // Act
        var act = () => provider.ChatAsync(new ChatCompletionRequest
        {
            Model = "m",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "hi" }]
        });

        // Assert
        (await act.Should().ThrowAsync<AiException>())
            .Which.Code.Should().Be(AiErrorCodes.ChatNotSupported);
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task DecideAsync_SendsBearerAndParsesResponse()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var provider = CreateProvider(handler);

        // Act
        var response = await provider.DecideAsync(Request());

        // Assert
        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("fake-api-key");
        handler.LastRequest.RequestUri!.AbsoluteUri
            .Should().Be("https://openrouter.example.com/api/alpha/decisions");
        response.Provider.Should().Be("TypeSafe");
        response.Answers["priority"].Noul!.ProbabilityOfYes.Should().Be(0.7);
    }

    [Fact]
    public async Task DecideAsync_EmptyRequestModel_FallsBackToDefaultModel()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var provider = CreateProvider(handler, defaultModel: "default-decision-model");

        // Act
        await provider.DecideAsync(Request(model: string.Empty));

        // Assert
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        doc.RootElement.GetProperty("model").GetString().Should().Be("default-decision-model");
    }

    [Fact]
    public async Task DecideAsync_NoModelAnywhere_ThrowsInvalidRequest()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var provider = CreateProvider(handler, defaultModel: string.Empty);

        // Act
        var act = () => provider.DecideAsync(Request(model: string.Empty));

        // Assert
        (await act.Should().ThrowAsync<AiException>())
            .Which.Code.Should().Be(AiErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task DecideAsync_DecisionsBaseUrlOverride_TargetsThatOrigin()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var provider = CreateProvider(handler, decisionsBaseUrl: "https://decisions.example.com/");

        // Act
        await provider.DecideAsync(Request());

        // Assert
        handler.LastRequest!.RequestUri!.AbsoluteUri
            .Should().Be("https://decisions.example.com/alpha/decisions");
    }

    [Fact]
    public async Task DecideAsync_NonAbsoluteDecisionsBaseUrl_ThrowsInvalidRequest()
    {
        // Arrange — a scheme-less override must surface as a clean AiException before the
        // request is built, not leak a raw UriFormatException
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var provider = CreateProvider(handler, decisionsBaseUrl: "decisions.example.com/");

        // Act
        var act = () => provider.DecideAsync(Request());

        // Assert
        var ex = (await act.Should().ThrowAsync<AiException>()).Which;
        ex.Code.Should().Be(AiErrorCodes.InvalidRequest);
        ex.Message.Should().Contain("is not a valid absolute URL");
    }

    [Fact]
    public async Task DecideAsync_NonHttpsDecisionsBaseUrlWithApiKey_ThrowsInvalidRequest()
    {
        // Arrange — an API key must never travel to a non-HTTPS decisions surface
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var provider = CreateProvider(handler, decisionsBaseUrl: "http://insecure.example.com/");

        // Act
        var act = () => provider.DecideAsync(Request());

        // Assert
        var ex = (await act.Should().ThrowAsync<AiException>()).Which;
        ex.Code.Should().Be(AiErrorCodes.InvalidRequest);
        ex.Message.Should().Contain("decisions base URL must use HTTPS");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, AiErrorCodes.InvalidRequest)]
    [InlineData(HttpStatusCode.Unauthorized, AiErrorCodes.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, AiErrorCodes.EndpointNotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, AiErrorCodes.RateLimited)]
    public async Task DecideAsync_ErrorStatus_IsClassified(HttpStatusCode status, string expectedCode)
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler(
            """{"error":{"message":"err"}}""", status);
        var options = new DecisionProviderOptions
        {
            BaseUrl = "https://openrouter.example.com/api/",
            ApiKey = "fake-api-key",
            Enabled = true,
            DefaultModel = "m"
        };
        var provider = new DecisionProvider(
            new HttpClient(handler), options, Catalog(), "openrouter-decisions");

        // Act
        var act = () => provider.DecideAsync(Request());

        // Assert
        (await act.Should().ThrowAsync<AiException>())
            .Which.Code.Should().Be(expectedCode);
    }

    [Fact]
    public void SupportsModelDiscovery_IsFalse()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);

        // Act
        var provider = CreateProvider(handler);

        // Assert
        provider.SupportsModelDiscovery.Should().BeFalse();
    }

    [Fact]
    public async Task GetModelsAsync_ThrowsModelDiscoveryNotSupported()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var provider = CreateProvider(handler);

        // Act
        var act = () => provider.GetModelsAsync(CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<AiException>())
            .Which.Code.Should().Be(AiErrorCodes.ModelDiscoveryNotSupported);
    }
}
