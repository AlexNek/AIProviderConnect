using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;

using FluentAssertions;

using Moq;

namespace AIProviderConnect.Tests.Providers;

public class ModelCatalogOverrideDecoratorTests
{
    private static IModelOverrideStore StoreFor(string providerId, params ModelOverride[] overrides)
    {
        var store = new InMemoryModelOverrideStore();
        store.Add(providerId, overrides);
        return store;
    }

    private static ChatCompletionRequest Request() => new() { Model = "gpt-4o" };

    private static async IAsyncEnumerable<StreamingChatChunk> Chunks()
    {
        yield return new StreamingChatChunk { Content = "hello" };
        await Task.CompletedTask;
    }

    [Fact]
    public void Constructor_NullArguments_Throws()
    {
        // Arrange
        var inner = new Mock<IAIProvider>().Object;
        var store = new InMemoryModelOverrideStore();

        // Act
        Action nullInner = () => new ModelCatalogOverrideDecorator(null!, store);
        Action nullStore = () => new ModelCatalogOverrideDecorator(inner, null!);

        // Assert
        nullInner.Should().Throw<ArgumentNullException>();
        nullStore.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IdentityMembers_DelegateToInner()
    {
        // Arrange
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");
        inner.Setup(p => p.IsEnabled).Returns(true);
        inner.Setup(p => p.Protocol).Returns(EProviderProtocol.MessagesApi);
        inner.As<IModelDiscoveryProvider>()
            .Setup(p => p.SupportsModelDiscovery).Returns(true);
        var decorator = new ModelCatalogOverrideDecorator(inner.Object, new InMemoryModelOverrideStore());

        // Act & Assert
        decorator.Id.Should().Be("test-provider");
        decorator.IsEnabled.Should().BeTrue();
        decorator.Protocol.Should().Be(EProviderProtocol.MessagesApi);
        decorator.SupportsModelDiscovery.Should().BeTrue();
    }

    [Fact]
    public void SupportsModelDiscovery_InnerWithoutDiscoveryButOverridesExist_ReturnsTrue()
    {
        // Arrange — inner is plain IAIProvider (no IModelDiscoveryProvider), overrides in store
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");
        var store = StoreFor(
            "test-provider",
            new ModelOverride { Id = "override-model", DisplayName = "Override" });
        var decorator = new ModelCatalogOverrideDecorator(inner.Object, store);

        // Act & Assert
        decorator.SupportsModelDiscovery.Should().BeTrue();
    }

    [Fact]
    public void SupportsModelDiscovery_InnerWithoutDiscoveryAndNoOverrides_ReturnsFalse()
    {
        // Arrange — inner is plain IAIProvider, empty store
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");
        var decorator = new ModelCatalogOverrideDecorator(inner.Object, new InMemoryModelOverrideStore());

        // Act & Assert
        decorator.SupportsModelDiscovery.Should().BeFalse();
    }

    [Fact]
    public async Task GetModelsAsync_MergesOverridesOverInnerLiveList()
    {
        // Arrange
        var live = new List<AIModel>
        {
            new() { Id = "gpt-4o", DisplayName = "GPT-4o", ProviderId = "test-provider" }
        };
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");
        inner.As<IModelDiscoveryProvider>()
            .Setup(d => d.GetModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(live);

        var store = StoreFor(
            "test-provider",
            new ModelOverride { Id = "gpt-4o", PromptPrice = 2.50m });
        var decorator = new ModelCatalogOverrideDecorator(inner.Object, store);

        // Act
        var result = await decorator.GetModelsAsync();

        // Assert
        result.Should().ContainSingle().Which.PromptPrice.Should().Be(2.50m);
    }

    [Fact]
    public async Task GetModelsAsync_InnerWithoutDiscovery_ReturnsOverrideOnlyModels()
    {
        // Arrange — inner implements only IAIProvider (no IModelDiscoveryProvider).
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");

        var store = StoreFor(
            "test-provider",
            new ModelOverride { Id = "only-model", DisplayName = "Only" });
        var decorator = new ModelCatalogOverrideDecorator(inner.Object, store);

        // Act
        var result = await decorator.GetModelsAsync();

        // Assert
        result.Should().ContainSingle().Which.Id.Should().Be("only-model");
    }

    [Fact]
    public async Task GetModelsAsync_DiscoveryNotSupported_TreatedAsEmptyBase()
    {
        // Arrange
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");
        inner.As<IModelDiscoveryProvider>()
            .Setup(d => d.GetModelsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiException(AiErrorCodes.ModelDiscoveryNotSupported, "no api"));

        var store = StoreFor(
            "test-provider",
            new ModelOverride { Id = "supplied" });
        var decorator = new ModelCatalogOverrideDecorator(inner.Object, store);

        // Act
        var result = await decorator.GetModelsAsync();

        // Assert
        result.Should().ContainSingle().Which.Id.Should().Be("supplied");
    }

    [Fact]
    public async Task GetModelsAsync_OtherDiscoveryException_Propagates()
    {
        // Arrange
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");
        inner.As<IModelDiscoveryProvider>()
            .Setup(d => d.GetModelsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiException(AiErrorCodes.Unauthorized, "bad key"));

        var store = StoreFor("test-provider", new ModelOverride { Id = "supplied" });
        var decorator = new ModelCatalogOverrideDecorator(inner.Object, store);

        // Act
        Func<Task> act = () => decorator.GetModelsAsync();

        // Assert
        await act.Should().ThrowAsync<AiException>()
            .Where(e => e.Code == AiErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task ChatAsync_DelegatesToInner_AndReturnsSameResponse()
    {
        // Arrange
        var response = new ChatCompletionResponse { Content = "hi" };
        var request = Request();
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");
        inner.Setup(p => p.ChatAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var decorator = new ModelCatalogOverrideDecorator(inner.Object, new InMemoryModelOverrideStore());

        // Act
        var result = await decorator.ChatAsync(request);

        // Assert
        result.Should().BeSameAs(response);
    }

    [Fact]
    public async Task StreamAsync_DelegatesToInnerStreamingProvider()
    {
        // Arrange
        var request = Request();
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");
        inner.As<IStreamingChatProvider>()
            .Setup(s => s.StreamAsync(request, It.IsAny<CancellationToken>()))
            .Returns(Chunks());
        var decorator = new ModelCatalogOverrideDecorator(inner.Object, new InMemoryModelOverrideStore());

        // Act
        var chunks = new List<StreamingChatChunk>();
        await foreach (var chunk in decorator.StreamAsync(request))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().ContainSingle().Which.Content.Should().Be("hello");
    }

    [Fact]
    public void StreamAsync_InnerWithoutStreaming_Throws()
    {
        // Arrange
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");
        var decorator = new ModelCatalogOverrideDecorator(inner.Object, new InMemoryModelOverrideStore());

        // Act
        Action act = () => decorator.StreamAsync(Request());

        // Assert
        act.Should().Throw<AiException>().Where(e => e.Code == AiErrorCodes.ConfigurationError);
    }
}
