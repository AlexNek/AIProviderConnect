using System.Text.Json;

using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Protocols;

/// <summary>
/// The host-declared Choice ceiling (D8) applies to both transports and to Choice questions only: an
/// oversized question is refused before any request leaves, while a question at the wire maximum is
/// mapped exactly as before and the Noul / Score kinds are never measured against it.
/// </summary>
public class DecisionsDeclaredCeilingTests
{
    private const string ProviderId = "ceiling-host";

    private static DecisionProvider CreateProvider(
        CapturingHttpMessageHandler handler,
        int? ceiling = null)
    {
        var options = new DecisionProviderOptions
        {
            BaseUrl = "https://test.example.com/api/",
            ApiKey = "fake-api-key",
            Enabled = true,
            DefaultModel = "model-under-test",
            DecisionsMaxOptions = ceiling
        };
        return new DecisionProvider(new HttpClient(handler), options, Catalog(), ProviderId);
    }

    private static ProviderCatalog Catalog() => new(
    [
        new ProviderDefinition
        {
            Id = ProviderId,
            DisplayName = "Ceiling host",
            BaseUrl = "https://test.example.com/api/",
            Protocol = EProviderProtocol.Decision
        }
    ]);

    private static DecisionRequest Request(string name, DecisionQuestion question) => new()
    {
        Model = "model-under-test",
        State = "A billing question.",
        Questions = new Dictionary<string, DecisionQuestion> { [name] = question }
    };

    private static DecisionQuestion ChoiceQuestion(int optionCount) => new()
    {
        Kind = EDecisionQuestionKind.Choice,
        Criteria = Enumerable.Range(0, optionCount)
            .ToDictionary(index => $"option-{index}", _ => string.Empty)
    };

    [Fact]
    public async Task DeclaredCeiling_RejectsOversizedChoiceBeforeSending()
    {
        // Arrange — six options under a declared ceiling of five
        using var handler = new CapturingHttpMessageHandler("{}");
        var provider = CreateProvider(handler, ceiling: 5);

        // Act
        var act = () => provider.DecideAsync(Request("intent", ChoiceQuestion(6)));

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.ConfigurationError);
        exception.Message.Should().Contain(ProviderId)
            .And.Contain("6 options")
            .And.Contain("ceiling of 5");
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task NoCeilingDeclared_TwoHundredFiftyFiveOptions_AreMappedUnchanged()
    {
        // Arrange — the wire maximum with no host-declared ceiling
        using var handler = new CapturingHttpMessageHandler("{}");
        var provider = CreateProvider(handler);

        // Act
        await provider.DecideAsync(Request("intent", ChoiceQuestion(255)));

        // Assert
        handler.LastRequest.Should().NotBeNull();
        using var body = JsonDocument.Parse(handler.CapturedBody!);
        var criteria = body.RootElement
            .GetProperty("questions").GetProperty("intent").GetProperty("criteria");
        criteria.EnumerateObject().Should().HaveCount(255);
        criteria.TryGetProperty("option-0", out _).Should().BeTrue();
        criteria.TryGetProperty("option-254", out _).Should().BeTrue();
    }

    [Fact]
    public async Task DeclaredCeiling_DoesNotApplyToNoulOrScore()
    {
        // Arrange — a ceiling of one that would reject any two-option Choice
        using var handler = new CapturingHttpMessageHandler("{}");
        var provider = CreateProvider(handler, ceiling: 1);
        var request = new DecisionRequest
        {
            Model = "model-under-test",
            State = "A billing question.",
            Questions = new Dictionary<string, DecisionQuestion>
            {
                ["priority"] = new()
                {
                    Kind = EDecisionQuestionKind.Noul,
                    Criteria = new Dictionary<string, string>
                    {
                        ["true"] = "Urgent.",
                        ["false"] = "Routine."
                    }
                },
                ["satisfaction"] = new()
                {
                    Kind = EDecisionQuestionKind.Score,
                    Scale = ["negative", "neutral", "positive"]
                }
            }
        };

        // Act
        await provider.DecideAsync(request);

        // Assert
        handler.LastRequest.Should().NotBeNull();
        using var body = JsonDocument.Parse(handler.CapturedBody!);
        var questions = body.RootElement.GetProperty("questions");
        questions.GetProperty("priority").GetProperty("type").GetString().Should().Be("noul");
        questions.GetProperty("satisfaction").GetProperty("type").GetString().Should().Be("score");
    }
}
