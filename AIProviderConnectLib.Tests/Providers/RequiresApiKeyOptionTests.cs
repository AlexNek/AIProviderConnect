using AIProviderConnect.Abstractions;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// The three-layer API-key requirement (typed options → JSON definition → derived from the address)
/// and the pre-flight gate that applies it, so a keyless loopback server is usable while a routable
/// host keeps demanding a key.
/// </summary>
public class RequiresApiKeyOptionTests
{
    private const string RoutableBaseUrl = "https://test.example.com/v1/";
    private const string LoopbackBaseUrl = "http://127.0.0.1:1234/v1/";

    private static OpenAICompatibleProvider Create(
        CapturingHttpMessageHandler handler,
        string baseUrl,
        bool? optionsRequiresApiKey = null,
        bool? definitionRequiresApiKey = null)
    {
        var definition = new ProviderDefinition
        {
            Id = "key-rule",
            DisplayName = "Key rule",
            BaseUrl = baseUrl,
            Protocol = EProviderProtocol.OpenAICompatible,
            RequiresApiKey = definitionRequiresApiKey
        };
        var options = new OpenAICompatibleProviderOptions
        {
            BaseUrl = baseUrl,
            ApiKey = string.Empty,
            Enabled = true,
            RequiresApiKey = optionsRequiresApiKey
        };

        return new OpenAICompatibleProvider(
            new HttpClient(handler), options, new ProviderCatalog([definition]), definition.Id);
    }

    private static ChatCompletionRequest Chat() => new()
    {
        Model = "test-model",
        Messages = [new ChatMessage { Role = EChatRole.User, Content = "hi" }]
    };

    [Fact]
    public async Task LoopbackHttp_NoKey_IsAccepted()
    {
        // Arrange — the derivation from a loopback address reports no key requirement
        using var handler = new CapturingHttpMessageHandler("{}");
        var provider = Create(handler, LoopbackBaseUrl);

        // Act
        var act = () => provider.ChatAsync(Chat());

        // Assert
        provider.RequiresApiKey.Should().BeFalse();
        await act.Should().NotThrowAsync();
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task RoutableHttps_NoKey_ThrowsNoApiKey()
    {
        // Arrange — unset everywhere on a routable host keeps today's refusal
        using var handler = new CapturingHttpMessageHandler("{}");
        var provider = Create(handler, RoutableBaseUrl);

        // Act
        var act = () => provider.ChatAsync(Chat());

        // Assert
        provider.RequiresApiKey.Should().BeTrue();
        (await act.Should().ThrowAsync<AiException>())
            .Which.Code.Should().Be(AiErrorCodes.NoApiKey);
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task DefinitionFalse_OnRoutableHost_WinsOverDerivation()
    {
        // Arrange — layer 2: the JSON definition declares that no key is required
        using var handler = new CapturingHttpMessageHandler("{}");
        var provider = Create(handler, RoutableBaseUrl, definitionRequiresApiKey: false);

        // Act
        var act = () => provider.ChatAsync(Chat());

        // Assert
        provider.RequiresApiKey.Should().BeFalse();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OptionsFalse_OnRoutableHost_WinsOverDefinitionTrue()
    {
        // Arrange — layer 1 over layer 2
        using var handler = new CapturingHttpMessageHandler("{}");
        var provider = Create(
            handler, RoutableBaseUrl,
            optionsRequiresApiKey: false, definitionRequiresApiKey: true);

        // Act
        var act = () => provider.ChatAsync(Chat());

        // Assert
        provider.RequiresApiKey.Should().BeFalse();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OptionsTrue_OnRoutableHost_WinsOverDefinitionFalse()
    {
        // Arrange — the consumer pins the requirement against the definition
        using var handler = new CapturingHttpMessageHandler("{}");
        var provider = Create(
            handler, RoutableBaseUrl,
            optionsRequiresApiKey: true, definitionRequiresApiKey: false);

        // Act
        var act = () => provider.ChatAsync(Chat());

        // Assert
        provider.RequiresApiKey.Should().BeTrue();
        (await act.Should().ThrowAsync<AiException>())
            .Which.Code.Should().Be(AiErrorCodes.NoApiKey);
    }

    [Fact]
    public async Task OptionsTrue_OnLoopbackHost_WinsOverDerivation()
    {
        // Arrange — a loopback server that really does demand a key
        using var handler = new CapturingHttpMessageHandler("{}");
        var provider = Create(handler, LoopbackBaseUrl, optionsRequiresApiKey: true);

        // Act
        var act = () => provider.ChatAsync(Chat());

        // Assert
        provider.RequiresApiKey.Should().BeTrue();
        (await act.Should().ThrowAsync<AiException>())
            .Which.Code.Should().Be(AiErrorCodes.NoApiKey);
    }

    [Fact]
    public void CatalogShippedProvider_Default_RequiresAKey()
    {
        // Arrange — "openai" is routable and declares no override
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders();

        // Act
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("openai");

        // Assert
        resolved.RequiresApiKey.Should().BeTrue();
    }

    [Fact]
    public void ConfigureCallback_OnCatalogShippedProvider_FlipsRequiresApiKey()
    {
        // Arrange — typed options flip the value without any Add/Replace definition
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(b => b.Configure<OpenAICompatibleProviderOptions>(
            "openai", o => o.RequiresApiKey = false));

        // Act
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("openai");

        // Assert — read through the interface, no cast or catalog lookup
        resolved.RequiresApiKey.Should().BeFalse();
    }
}
