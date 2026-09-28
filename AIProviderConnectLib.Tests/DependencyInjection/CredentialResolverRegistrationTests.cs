using AIProviderConnect.Abstractions;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace AIProviderConnect.Tests.DependencyInjection;

/// <summary>
/// Verifies DI wiring for <see cref="ICredentialResolver"/> and the keyed <see cref="ProviderActivator"/>.
/// </summary>
public class CredentialResolverRegistrationTests
{
    private const string ChatJson =
        """{"choices":[{"message":{"content":"hi"},"finish_reason":"stop"}]}""";

    private static ProviderDefinition Definition(string id = "test-provider") =>
        new()
        {
            Id = id,
            DisplayName = "Test provider",
            BaseUrl = "https://test.example.com/v1/",
            Protocol = EProviderProtocol.OpenAICompatible
        };

    private static ICredentialResolver ResolverReturningKey(string key)
    {
        var mock = new Mock<ICredentialResolver>();
        mock
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<RequestCredentials?>(new RequestCredentials { ApiKey = key }));
        return mock.Object;
    }

    private static ChatCompletionRequest ChatRequest() => new()
    {
        Model = "test-model",
        Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
    };

    [Fact]
    public async Task ManuallyRegisteredResolver_WiredAtProviderConstruction()
    {
        // Arrange — the consumer registers an ICredentialResolver directly in the container.
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(handler));
        services.AddLogging();
        services.AddSingleton(ResolverReturningKey("resolver-key"));
        services.AddAiProviders(b => b
            .Add(Definition())
            .Configure<OpenAICompatibleProviderOptions>("test-provider", o => o.ApiKey = "fake-api-key"));

        // Act
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("test-provider");
        await resolved.ChatAsync(ChatRequest());

        // Assert — the DI-built singleton consulted the registered resolver.
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("resolver-key");
    }

    [Fact]
    public void KeyedProviderActivator_ExistsForEveryRegisteredProviderId()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(new[] { Definition("provider-a"), Definition("provider-b") });

        // Act
        using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetKeyedService<ProviderActivator>("provider-a").Should().NotBeNull();
        provider.GetKeyedService<ProviderActivator>("provider-b").Should().NotBeNull();
    }

    [Fact]
    public void NoResolverRegistered_WhenUseCredentialResolverNotCalled()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(new[] { Definition() });

        // Act
        using var provider = services.BuildServiceProvider();

        // Assert — the library registers no default ICredentialResolver.
        provider.GetService<ICredentialResolver>().Should().BeNull();
    }

    [Fact]
    public void UseCredentialResolver_RegistersResolverAsSingleton()
    {
        // Arrange
        var fakeResolver = ResolverReturningKey("resolver-key");
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(b => b.Add(Definition()).UseCredentialResolver(fakeResolver));

        // Act
        using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<ICredentialResolver>().Should().BeSameAs(fakeResolver);
    }
}
