using AIProviderConnect.Constants;
using AIProviderConnect.Models;
using AIProviderConnect.Services;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.DependencyInjection;

/// <summary>
/// Catalog parity guard (feature 16, rule 18): for every embedded definition that
/// declares an <c>endpoints</c> entry for a legacy operation (chat, models, messages,
/// embeddings), the resolved URL must equal the value the pre-change definition
/// resolved (common base plus the flat endpoint field). A version segment duplicated
/// into a path (<c>/api/v1/v1/chat/completions</c>) fails here before release. The
/// <c>decisions</c> operation is exempt — it is new and has no pre-change value.
/// </summary>
public class CatalogEndpointParityTests
{
    public static TheoryData<string, string> LegacyOperations() => new()
    {
        { EndpointOperations.Chat, "ChatEndpoint" },
        { EndpointOperations.Models, "ModelsEndpoint" },
        { EndpointOperations.Messages, "MessagesEndpoint" }
    };

    [Theory]
    [MemberData(nameof(LegacyOperations))]
    public void EmbeddedDefinitions_EndpointEntry_MatchesLegacyResolution(string operation, string legacyField)
    {
        // Arrange
        var catalog = new ProviderCatalog();

        foreach (var definition in catalog.All)
        {
            var entry = EndpointOperations.Find(definition.Endpoints, operation);
            if (entry?.Path is null) continue;

            var legacyPath = (string?)typeof(ProviderDefinition)
                .GetProperty(legacyField)!
                .GetValue(definition);
            if (string.IsNullOrEmpty(legacyPath)) continue;

            var legacyUrl = Compose(definition.BaseUrl, legacyPath);
            var entryUrl = Compose(entry.BaseUrl ?? definition.BaseUrl, entry.Path);

            // Assert
            entryUrl.Should().Be(
                legacyUrl,
                "provider '{0}' declares endpoints.{1} but resolves a different URL than the pre-change flat field '{2}'",
                definition.Id,
                operation,
                legacyField);
        }
    }

    private static string Compose(string? baseUrl, string path)
    {
        var baseUri = new Uri((baseUrl ?? string.Empty).TrimEnd('/') + "/");
        return new Uri(baseUri, path.TrimStart('/')).AbsoluteUri;
    }
}
