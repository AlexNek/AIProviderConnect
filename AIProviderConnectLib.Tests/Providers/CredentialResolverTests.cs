using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace AIProviderConnectLib.Tests.Providers;

public class CredentialResolverTests
{
    private const string ChatJson = """{"choices":[{"message":{"content":"hi"},"finish_reason":"stop"}]}""";

    private static Mock<ICredentialResolver> ResolverReturning(RequestCredentials? credentials)
    {
        var mock = new Mock<ICredentialResolver>();
        mock
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<RequestCredentials?>(credentials));
        return mock;
    }

    private static ProviderCatalog Catalog() => new([
        new ProviderDefinition
        {
            Id = "test-provider", DisplayName = "Test provider",
            BaseUrl = "https://test.example.com/v1/", Protocol = EProviderProtocol.OpenAICompatible
        }
    ]);

    private static OpenAICompatibleProvider CreateProvider(
        CapturingHttpMessageHandler handler,
        ICredentialResolver? resolver,
        Action<OpenAICompatibleProviderOptions>? configureOptions = null)
    {
        var options = new OpenAICompatibleProviderOptions
        {
            BaseUrl = "https://test.example.com/v1/",
            ApiKey = "fake-api-key",
            Enabled = true
        };
        configureOptions?.Invoke(options);
        return new OpenAICompatibleProvider(
            new HttpClient(handler), options, Catalog(), "test-provider",
            NullLogger.Instance, resolver);
    }

    private static ChatCompletionRequest SampleRequest(string model = "test-model") => new()
    {
        Model = model,
        Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
    };

    [Fact]
    public async Task ResolverApiKey_UsedForCall()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var resolver = ResolverReturning(new RequestCredentials { ApiKey = "resolver-key" });
        var provider = CreateProvider(handler, resolver.Object);

        // Act
        await provider.ChatAsync(SampleRequest());

        // Assert
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("resolver-key");
    }

    [Theory]
    [InlineData("null-result", null)]
    [InlineData("all-unset", "")]
    [InlineData("empty-key", "!empty!")]
    [InlineData("whitespace-key", "   ")]
    public async Task ResolverWithoutApiKey_FallsBackToConfiguredKey(string kind, string? sentinel)
    {
        // Arrange
        RequestCredentials? credentials = kind switch
        {
            "null-result" => null,
            "all-unset" => new RequestCredentials(),
            "empty-key" => new RequestCredentials { ApiKey = string.Empty },
            _ => new RequestCredentials { ApiKey = "   " }
        };
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var provider = CreateProvider(handler, ResolverReturning(credentials).Object);

        // Act
        await provider.ChatAsync(SampleRequest());

        // Assert
        _ = sentinel;
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("fake-api-key");
    }

    [Fact]
    public async Task ResolverBaseUrl_ChangesRequestHost()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var resolver = ResolverReturning(new RequestCredentials { BaseUrl = "https://override.example.com/v1/" });
        var provider = CreateProvider(handler, resolver.Object);

        // Act
        await provider.ChatAsync(SampleRequest());

        // Assert
        handler.LastRequest!.RequestUri!.Host.Should().Be("override.example.com");
    }

    [Fact]
    public async Task FixedCredentials_ShortCircuitsResolver()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var resolver = ResolverReturning(new RequestCredentials { ApiKey = "resolver-key" });
        var provider = CreateProvider(handler, resolver.Object);
        provider.FixedCredentials = new RequestCredentials { ApiKey = "fixed-key" };

        // Act
        await provider.ChatAsync(SampleRequest());

        // Assert
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("fixed-key");
        resolver.Verify(
            r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void FixedCredentials_SecondWrite_ThrowsInvalidOperation()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var provider = CreateProvider(handler, resolver: null);
        provider.FixedCredentials = new RequestCredentials { ApiKey = "fixed-key" };

        // Act
        Action act = () => provider.FixedCredentials = new RequestCredentials { ApiKey = "another-key" };

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task ThrowingResolver_SurfacesProviderMissingConfiguration()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var resolver = new Mock<ICredentialResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("resolver boom"));
        var provider = CreateProvider(handler, resolver.Object);

        // Act
        Func<Task> act = () => provider.ChatAsync(SampleRequest());

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.ProviderMissingConfiguration);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var resolver = new Mock<ICredentialResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new OperationCanceledException(cts.Token));
        var provider = CreateProvider(handler, resolver.Object);

        // Act
        Func<Task> act = () => provider.ChatAsync(SampleRequest(), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EmptyConfiguredKey_CompletesWhenResolverSuppliesOne_ThrowsWhenNothingDoes()
    {
        // Arrange — gate regression: an empty configured key is valid only with a per-call key.
        using var successHandler = new CapturingHttpMessageHandler(ChatJson);
        var resolver = ResolverReturning(new RequestCredentials { ApiKey = "resolver-key" });
        var emptyKeyProvider = CreateProvider(
            successHandler, resolver.Object, o => o.ApiKey = string.Empty);

        // Act
        await emptyKeyProvider.ChatAsync(SampleRequest());

        // Assert — the call completed with the resolver key.
        successHandler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("resolver-key");

        // Arrange — with no resolver and an empty key, the effective-configuration gate still fails.
        using var failingHandler = new CapturingHttpMessageHandler(ChatJson);
        var noResolverProvider = CreateProvider(
            failingHandler, resolver: null, o => o.ApiKey = string.Empty);

        // Act
        Func<Task> act = () => noResolverProvider.ChatAsync(SampleRequest());

        // Assert
        (await act.Should().ThrowAsync<AiException>()).Which.Code.Should().Be(AiErrorCodes.NoApiKey);
    }

    [Fact]
    public async Task ConsumerConfigureHeadersOverride_AppliedWhenNoCredentialsSupplied()
    {
        // Arrange — proves the no-override path still invokes the single-argument ConfigureHeaders virtual.
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var options = new OpenAICompatibleProviderOptions
        {
            BaseUrl = "https://test.example.com/v1/", ApiKey = "fake-api-key", Enabled = true
        };
        var provider = new HeaderCustomizingProvider(
            new HttpClient(handler), options, Catalog(), "test-provider",
            NullLogger.Instance, credentialResolver: null);

        // Act
        await provider.ChatAsync(SampleRequest());

        // Assert
        handler.LastRequest!.Headers.GetValues("X-Custom").Should().Equal("custom-value");
    }

    [Fact]
    public async Task ConsumerConfigureHeadersKeyOverride_AppliedWhenCredentialsSupplied()
    {
        // Arrange — a call carrying an API key routes through the two-argument ConfigureHeaders overload.
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var options = new OpenAICompatibleProviderOptions
        {
            BaseUrl = "https://test.example.com/v1/", ApiKey = "fake-api-key", Enabled = true
        };
        var provider = new KeyAwareHeaderCustomizingProvider(
            new HttpClient(handler), options, Catalog(), "test-provider",
            NullLogger.Instance, ResolverReturning(new RequestCredentials { ApiKey = "resolver-key" }).Object);

        // Act
        await provider.ChatAsync(SampleRequest());

        // Assert — bespoke headers and the effective key are both applied on the override path.
        handler.LastRequest!.Headers.GetValues("X-Custom").Should().Equal("custom-value");
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("resolver-key");
    }

    [Fact]
    public async Task ChatAsync_WithRetry_ResolvesCredentialsExactlyOnce()
    {
        // Arrange — 429 then 200 forces one retry; the resolver must not be re-consulted mid-flight,
        // and both attempts must go out with the credentials resolved for the original call.
        using var handler = new ScriptedStatusHttpMessageHandler(
            ChatJson, System.Net.HttpStatusCode.TooManyRequests, System.Net.HttpStatusCode.OK);
        var options = new OpenAICompatibleProviderOptions
        {
            BaseUrl = "https://test.example.com/v1/",
            ApiKey = "fake-api-key",
            Enabled = true,
            MaxRetryCount = 1,
            RetryDelay = TimeSpan.FromMilliseconds(1)
        };
        var resolver = ResolverReturning(new RequestCredentials { ApiKey = "resolver-key" });
        var provider = new OpenAICompatibleProvider(
            new HttpClient(handler), options, Catalog(), "test-provider",
            NullLogger.Instance, resolver.Object);

        // Act
        var response = await provider.ChatAsync(SampleRequest());

        // Assert
        response.Content.Should().Be("hi");
        handler.CallCount.Should().Be(2);
        handler.AuthorizationValues.Should().Equal("resolver-key", "resolver-key");
        resolver.Verify(
            r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
