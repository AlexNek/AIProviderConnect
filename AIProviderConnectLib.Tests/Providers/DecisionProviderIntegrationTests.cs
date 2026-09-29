using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// Live integration test for the decision transport.
/// Skipped by default; requires a configured decisions host and API key in environment variables.
/// Excluded from CI via the "Integration" category trait.
/// </summary>
[Trait("Category", "Integration")]
public class DecisionProviderIntegrationTests
{
    private const string HostEnvVar = "AIPROVIDERCONNECT_DECISION_HOST";
    private const string KeyEnvVar = "AIPROVIDERCONNECT_DECISION_KEY";
    private const string ModelEnvVar = "AIPROVIDERCONNECT_DECISION_MODEL";

    [Fact(Skip = "Requires a live decisions host configured via environment variables.")]
    public async Task DecideAsync_LiveTicketTriage_ReturnsTypedAnswers()
    {
        // Arrange
        var host = Environment.GetEnvironmentVariable(HostEnvVar);
        var key = Environment.GetEnvironmentVariable(KeyEnvVar);
        var model = Environment.GetEnvironmentVariable(ModelEnvVar) ?? "typesafe/jev-1.13";

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                $"Set {HostEnvVar} and {KeyEnvVar} environment variables to run this integration test.");

        var definition = new ProviderDefinition
        {
            Id = "integration-decisions",
            DisplayName = "Integration Decision Provider",
            Protocol = EProviderProtocol.Decision,
            BaseUrl = host
        };

        var options = new DecisionProviderOptions
        {
            BaseUrl = host,
            ApiKey = key,
            Enabled = true,
            DefaultModel = model
        };

        using var httpClient = new HttpClient();
        var catalog = new ProviderCatalog([definition]);
        var provider = new DecisionProvider(httpClient, options, catalog, "integration-decisions");

        var request = new DecisionRequest
        {
            Model = model,
            State = "Customer was double-charged for their annual plan and wants a refund.",
            Questions = new Dictionary<string, DecisionQuestion>
            {
                ["is_billing"] = new() { Kind = EDecisionQuestionKind.Noul },
                ["priority"] = new()
                {
                    Kind = EDecisionQuestionKind.Choice,
                    Instructions = "Pick the priority.",
                    Criteria = new Dictionary<string, string>
                    {
                        ["low"] = "Can wait several days",
                        ["normal"] = "Handle within a day",
                        ["high"] = "Handle within the hour"
                    }
                },
                ["severity"] = new()
                {
                    Kind = EDecisionQuestionKind.Score,
                    Scale = ["minor", "moderate", "major", "critical"]
                }
            }
        };

        // Act
        var response = await provider.DecideAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Model.Should().NotBeNullOrWhiteSpace();
        response.Answers.Should().HaveCount(3);

        response.Answers["is_billing"].Kind.Should().Be(EDecisionAnswerKind.Noul);
        response.Answers["priority"].Kind.Should().Be(EDecisionAnswerKind.Choice);
        response.Answers["severity"].Kind.Should().Be(EDecisionAnswerKind.Score);

        response.Usage.Should().NotBeNull();
        response.Usage.TotalTokens.Should().BeGreaterThan(0);

        // The first-party host returns no cost field; a router may. Cost must be null when unreported.
        if (response.Usage.Cost is null)
        {
            // Acceptable: the configured host does not report cost.
        }
        else
        {
            response.Usage.Cost.Value.Should().BeGreaterThanOrEqualTo(0m);
        }
    }
}
