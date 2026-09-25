using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;

using FluentAssertions;

using Moq;

namespace AIProviderConnect.Tests.Providers;

public class NonStreamingModelCatalogOverrideDecoratorTests
{
    private static IModelOverrideStore StoreFor(string providerId, params ModelOverride[] overrides)
    {
        var store = new InMemoryModelOverrideStore();
        store.Add(providerId, overrides);
        return store;
    }

    private static ChatCompletionRequest Request() => new() { Model = "gpt-4o" };

    [Fact]
    public void Constructor_NullArguments_Throws()
    {
        // Arrange
        var inner = new Mock<IAIProvider>().Object;
        var store = new InMemoryModelOverrideStore();

        // Act
        Action nullInner = () => new NonStreamingModelCatalogOverrideDecorator(null!, store);
        Action nullStore = () => new NonStreamingModelCatalogOverrideDecorator(inner, null!);

        // Assert
        nullInner.Should().Throw<ArgumentNullException>();
        nullStore.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void StreamingInner_WrappedInStreamingDecorator_IsIStreamingChatProvider()
    {
        // Arrange — inner supports streaming, so the streaming decorator is selected
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");
        inner.As<IStreamingChatProvider>();

        // Act — the DI layer picks ModelCatalogOverrideDecorator for streaming inners
        var decorator = new ModelCatalogOverrideDecorator(inner.Object, new InMemoryModelOverrideStore());

        // Assert
        decorator.Should().BeAssignableTo<IStreamingChatProvider>();
    }

    [Fact]
    public void NonStreamingInner_WrappedInNonStreamingDecorator_IsNotIStreamingChatProvider()
    {
        // Arrange — inner does NOT support streaming
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");

        // Act — the DI layer picks NonStreamingModelCatalogOverrideDecorator
        var decorator = new NonStreamingModelCatalogOverrideDecorator(inner.Object, new InMemoryModelOverrideStore());

        // Assert
        decorator.Should().NotBeAssignableTo<IStreamingChatProvider>();
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
        var decorator = new NonStreamingModelCatalogOverrideDecorator(inner.Object, new InMemoryModelOverrideStore());

        // Act & Assert
        decorator.Id.Should().Be("test-provider");
        decorator.IsEnabled.Should().BeTrue();
        decorator.Protocol.Should().Be(EProviderProtocol.MessagesApi);
        decorator.SupportsModelDiscovery.Should().BeTrue();
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
        var decorator = new NonStreamingModelCatalogOverrideDecorator(inner.Object, store);

        // Act
        var result = await decorator.GetModelsAsync();

        // Assert
        result.Should().ContainSingle().Which.PromptPrice.Should().Be(2.50m);
    }

    [Fact]
    public async Task ChatAsync_DelegatesToInner()
    {
        // Arrange
        var response = new ChatCompletionResponse { Content = "hi" };
        var request = Request();
        var inner = new Mock<IAIProvider>();
        inner.Setup(p => p.Id).Returns("test-provider");
        inner.Setup(p => p.ChatAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var decorator = new NonStreamingModelCatalogOverrideDecorator(inner.Object, new InMemoryModelOverrideStore());

        // Act
        var result = await decorator.ChatAsync(request);

        // Assert
        result.Should().BeSameAs(response);
    }
}
