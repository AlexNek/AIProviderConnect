using System.Text.Json;

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
    public void EmbeddingsEntry_FillsEndpointAndBaseUrlOverride()
    {
        // Arrange — mirrors DecisionsEntry_FillsEndpointAndBaseUrlOverride; the embeddings
        // surface lives on a different root than the /api/v1/ common base
        var definition = Definition(d => d with
        {
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["embeddings"] = new EndpointDefinition
                {
                    Path = "custom/embeddings",
                    BaseUrl = "https://test.example.com/embed/"
                }
            }
        });

        // Act
        var options = ResolveOptions<OpenAICompatibleProviderOptions>(definition);

        // Assert
        options.EmbeddingsEndpoint.Should().Be("custom/embeddings");
        options.EmbeddingsBaseUrl.Should().Be("https://test.example.com/embed/");
    }

    [Fact]
    public void EmbeddingsEntryWithoutOverride_LeavesBaseUrlNull()
    {
        // Arrange — the common base is the default: no baseUrl member on the entry
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
        options.EmbeddingsBaseUrl.Should().BeNull();
    }

    [Fact]
    public void HybridGatewayEmbeddingsEntry_FillsBaseUrlOverride()
    {
        // Arrange — the HybridGateway options type must fold the same way
        var definition = new ProviderDefinition
        {
            Id = ProviderId,
            DisplayName = "Hybrid seed test",
            Protocol = EProviderProtocol.HybridGateway,
            BaseUrl = "https://test.example.com/api/v1/",
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["embeddings"] = new EndpointDefinition
                {
                    BaseUrl = "https://test.example.com/embed/"
                }
            }
        };

        // Act
        var options = ResolveOptions<HybridGatewayProviderOptions>(definition);

        // Assert — baseUrl-only entry passes the decoration rule (baseUrl is a change) and
        // now carries meaning via the seeded EmbeddingsBaseUrl
        options.EmbeddingsBaseUrl.Should().Be("https://test.example.com/embed/");
    }

    [Fact]
    public void ConsumerConfigureOverridesSeededEmbeddingsBaseUrl()
    {
        // Arrange — pins precedence step 4: a consumer Configure callback runs after
        // SeedFromDefinition and its value wins
        var definition = Definition(d => d with
        {
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["embeddings"] = new EndpointDefinition
                {
                    BaseUrl = "https://test.example.com/embed/"
                }
            }
        });
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(b =>
        {
            b.Add(definition);
            b.Configure<OpenAICompatibleProviderOptions>(
                ProviderId, o => o.EmbeddingsBaseUrl = "https://consumer.example.com/");
        });
        using var provider = services.BuildServiceProvider();

        // Act
        var options = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<OpenAICompatibleProviderOptions>>()
            .Get(ProviderId);

        // Assert
        options.EmbeddingsBaseUrl.Should().Be("https://consumer.example.com/");
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

    [Fact]
    public void ModelsEntryWithAdditionalQueryParameter_AppendsToModelsEndpoint()
    {
        // Arrange
        var definition = Definition(d => d with
        {
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["models"] = new EndpointDefinition
                {
                    Path = "models",
                    AdditionalQueryParameter = "output_modalities=all"
                }
            }
        });

        // Act
        var options = ResolveOptions<OpenAICompatibleProviderOptions>(definition);

        // Assert
        options.ModelsEndpoint.Should().Be("models?output_modalities=all");
    }

    [Fact]
    public void ModelsEntryWithoutAdditionalQueryParameter_LeavesModelsEndpointUnchanged()
    {
        // Arrange
        var definition = Definition(d => d with
        {
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["models"] = new EndpointDefinition { Path = "models" }
            }
        });

        // Act
        var options = ResolveOptions<OpenAICompatibleProviderOptions>(definition);

        // Assert
        options.ModelsEndpoint.Should().Be("models");
    }

    [Fact]
    public void MessagesApiModelsWithAdditionalQueryParameter_AppendsToModelsEndpoint()
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
                ["models"] = new EndpointDefinition
                {
                    Path = "models",
                    AdditionalQueryParameter = "output_modalities=all"
                }
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
        options.ModelsEndpoint.Should().Be("models?output_modalities=all");
    }

    [Fact]
    public void KeyQueryModelsWithAdditionalQueryParameter_AppendsToModelsEndpoint()
    {
        // Arrange
        var definition = new ProviderDefinition
        {
            Id = ProviderId,
            DisplayName = "Seed Test",
            Protocol = EProviderProtocol.KeyQuery,
            BaseUrl = "https://test.example.com/",
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["models"] = new EndpointDefinition
                {
                    Path = "models",
                    AdditionalQueryParameter = "output_modalities=all"
                }
            }
        };
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(new[] { definition });
        using var provider = services.BuildServiceProvider();

        // Act
        var options = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<KeyQueryOptions>>()
            .Get(ProviderId);

        // Assert
        options.ModelsEndpoint.Should().Be("models?output_modalities=all");
    }

    [Fact]
    public void OpenRouterJson_DeserializesAdditionalQueryParameter()
    {
        // Arrange — read the actual embedded openrouter.json from the output directory
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "ai-providers", "openrouter.json");
        if (!File.Exists(jsonPath))
        {
            // Fallback: try the source directory
            jsonPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIProviderConnectLib", "ai-providers", "openrouter.json"));
        }
        File.Exists(jsonPath).Should().BeTrue($"openrouter.json should exist at {jsonPath}");
        var json = File.ReadAllText(jsonPath);
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Act
        var provider = JsonSerializer.Deserialize<ProviderDefinition>(json, jsonOptions);

        // Assert
        provider.Should().NotBeNull();
        provider!.Endpoints.Should().ContainKey("models");
        var modelsEntry = provider.Endpoints!["models"]!;
        modelsEntry.Path.Should().Be("models");
        modelsEntry.AdditionalQueryParameter.Should().Be("output_modalities=all");
    }

    [Fact]
    public void SeededModelsEndpoint_BuildsCorrectUrl()
    {
        // Arrange — full flow: JSON → deserialize → seed → build URL
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "ai-providers", "openrouter.json");
        if (!File.Exists(jsonPath))
            jsonPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIProviderConnectLib", "ai-providers", "openrouter.json"));
        var json = File.ReadAllText(jsonPath);
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var definition = JsonSerializer.Deserialize<ProviderDefinition>(json, jsonOptions)!;

        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(new[] { definition });
        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<OpenAICompatibleProviderOptions>>().Get("openrouter");

        // Act — simulate what BuildRequest does
        var baseUrl = "https://openrouter.ai/api/v1/";
        var url = new Uri(new Uri(baseUrl), options.ModelsEndpoint);

        // Assert
        options.ModelsEndpoint.Should().Be("models?output_modalities=all");
        url.ToString().Should().Be("https://openrouter.ai/api/v1/models?output_modalities=all");
        url.Query.Should().Be("?output_modalities=all");
    }
}
