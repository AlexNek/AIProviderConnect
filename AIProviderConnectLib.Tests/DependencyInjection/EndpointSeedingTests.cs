using AIProviderConnect.Constants;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Models;
using AIProviderConnect.Options;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnect.Tests.DependencyInjection;

/// <summary>
/// Locks the rule 5 precedence chain for one operation's effective endpoint:
/// options-class default, legacy flat field, endpoints[op].path, consumer
/// Configure&lt;TOptions&gt;, and the endpoints[op].baseUrl fold into the operation's
/// base-URL override.
/// </summary>
public class EndpointSeedingTests
{
    private const string ProviderId = "seed-test";

    private static ProviderDefinition Definition(
        Func<ProviderDefinition, ProviderDefinition>? customize = null)
    {
        var definition = new ProviderDefinition
        {
            Id = ProviderId,
            DisplayName = "Seed Test",
            Protocol = EProviderProtocol.OpenAICompatible,
            BaseUrl = "https://test.example.com/api/v1/"
        };

        return customize is null ? definition : customize(definition);
    }

    private static TOptions ResolveOptions<TOptions>(ProviderDefinition definition)
        where TOptions : class
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(new[] { definition });
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<TOptions>>().Get(ProviderId);
    }

    [Fact]
    public void NoEndpoints_KeepsOptionDefaultsAndFlatFields()
    {
        // Arrange
        var definition = Definition(d => d with { ChatEndpoint = "custom/chat" });

        // Act
        var options = ResolveOptions<OpenAICompatibleProviderOptions>(definition);

        // Assert
        options.ChatEndpoint.Should().Be("custom/chat");
        options.ModelsEndpoint.Should().Be(EndpointDefaults.Models);
        options.EmbeddingsEndpoint.Should().Be(EndpointDefaults.Embeddings);
        options.DecisionsEndpoint.Should().Be(EndpointDefaults.Decisions);
        options.DecisionsBaseUrl.Should().BeNull();
    }

    [Fact]
    public void PrecedenceChain_FlatFieldOverridesDefault()
    {
        // Arrange — flat field set, no endpoints block
        var definition = Definition(d => d with { ChatEndpoint = "flat/chat" });

        // Act
        var options = ResolveOptions<OpenAICompatibleProviderOptions>(definition);

        // Assert
        options.ChatEndpoint.Should().Be("flat/chat");
    }

    [Fact]
    public void EndpointsPathOverridesFlatField()
    {
        // Arrange
        var definition = Definition(d => d with
        {
            ChatEndpoint = "flat/chat",
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["chat"] = new EndpointDefinition { Path = "endpoints/chat" }
            }
        });

        // Act
        var options = ResolveOptions<OpenAICompatibleProviderOptions>(definition);

        // Assert
        options.ChatEndpoint.Should().Be("endpoints/chat");
    }

    [Fact]
    public void ConsumerConfigureOverridesEndpointsEntry()
    {
        // Arrange
        var definition = Definition(d => d with
        {
            ChatEndpoint = "flat/chat",
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["chat"] = new EndpointDefinition { Path = "endpoints/chat" }
            }
        });
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(b =>
        {
            b.Add(definition);
            b.Configure<OpenAICompatibleProviderOptions>(ProviderId, o => o.ChatEndpoint = "consumer/chat");
        });
        using var provider = services.BuildServiceProvider();

        // Act
        var options = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<OpenAICompatibleProviderOptions>>()
            .Get(ProviderId);

        // Assert
        options.ChatEndpoint.Should().Be("consumer/chat");
    }

    [Fact]
    public void DecisionsEntry_FillsEndpointAndBaseUrlOverride()
    {
        // Arrange
        var definition = Definition(d => d with
        {
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["decisions"] = new EndpointDefinition
                {
                    Path = "alpha/decisions",
                    BaseUrl = "https://test.example.com/api/",
                    Protocol = EProviderProtocol.Decision
                }
            }
        });

        // Act
        var options = ResolveOptions<OpenAICompatibleProviderOptions>(definition);

        // Assert
        options.DecisionsEndpoint.Should().Be("alpha/decisions");
        options.DecisionsBaseUrl.Should().Be("https://test.example.com/api/");
    }

    [Fact]
    public void DecisionsEntryWithoutOverride_LeavesBaseUrlNull()
    {
        // Arrange — the common base is the default: no baseUrl member on the entry
        var definition = Definition(d => d with
        {
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["decisions"] = new EndpointDefinition { Path = "custom/decisions" }
            }
        });

        // Act
        var options = ResolveOptions<OpenAICompatibleProviderOptions>(definition);

        // Assert
        options.DecisionsEndpoint.Should().Be("custom/decisions");
        options.DecisionsBaseUrl.Should().BeNull();
    }

    [Fact]
    public void EmbeddingsEntry_FillsEmbeddingsEndpoint()
    {
        // Arrange
        var definition = Definition(d => d with
        {
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["embeddings"] = new EndpointDefinition { Path = "custom/embeddings" }
            }
        });

        // Act
        var options = ResolveOptions<OpenAICompatibleProviderOptions>(definition);

        // Assert
        options.EmbeddingsEndpoint.Should().Be("custom/embeddings");
    }

    [Fact]
    public void MessagesApiPrimary_MessagesEntryFillsMessagesEndpoint_LeavesChatUntouched()
    {
        // Arrange
        var definition = new ProviderDefinition
        {
            Id = ProviderId,
            DisplayName = "Seed Test",
            Protocol = EProviderProtocol.MessagesApi,
            BaseUrl = "https://test.example.com/",
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["messages"] = new EndpointDefinition { Path = "custom/messages" },
                ["chat"] = new EndpointDefinition { Path = "ignored/chat" }
            }
        };
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(new[] { definition });
        using var provider = services.BuildServiceProvider();

        // Act
        var options = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<MessagesApiOptions>>()
            .Get(ProviderId);

        // Assert
        options.MessagesEndpoint.Should().Be("custom/messages");
    }

    [Fact]
    public void OperationKeysMatchCaseInsensitively()
    {
        // Arrange
        var definition = Definition(d => d with
        {
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["Chat"] = new EndpointDefinition { Path = "case/chat" }
            }
        });

        // Act
        var options = ResolveOptions<OpenAICompatibleProviderOptions>(definition);

        // Assert
        options.ChatEndpoint.Should().Be("case/chat");
    }
}
