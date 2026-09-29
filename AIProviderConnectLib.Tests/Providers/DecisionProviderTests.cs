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
    public async Task DecideAsync_ErrorStatus_IsClassified()
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler(
            """{"error":{"message":"bad"}}""", HttpStatusCode.BadRequest);
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
            .Which.Code.Should().Be(AiErrorCodes.InvalidRequest);
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
}
