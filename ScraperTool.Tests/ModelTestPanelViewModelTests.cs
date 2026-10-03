using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using FluentAssertions;

using Moq;

using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.ViewModels;

namespace ScraperTool.Tests;

public class ModelTestPanelViewModelTests
{
    private readonly Mock<ITransientCredentialProviderFactory> _factoryMock;
    private readonly AppSettings _settings;
    private readonly Dictionary<string, IReadOnlyList<AIModel>> _modelsByProvider = new();

    // Counts how many times the VM asks the factory to build a provider — i.e. how many
    // model-discovery passes ran. A refresh that skips discovery must not increase it.
    private int _discoveryCallCount;

    public ModelTestPanelViewModelTests()
    {
        _factoryMock = new Mock<ITransientCredentialProviderFactory>();
        _factoryMock
            .Setup(f => f.GetProvider(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string providerId, string _) =>
            {
                _discoveryCallCount++;
                IReadOnlyList<AIModel> models =
                    _modelsByProvider.TryGetValue(providerId, out var m)
                        ? m
                        : Array.Empty<AIModel>();
                return new DiscoveryProvider(models);
            });

        _settings = new AppSettings
        {
            ApiKey = "fake-api-key",
            SelectedProviderId = "p1"
        };
    }

    [Fact]
    public async Task RefreshIfProviderChangedAsync_DoesNotRefetch_WhenProviderAndKeyUnchanged()
    {
        // Arrange — cache the model list for p1, then reopen with nothing changed.
        _modelsByProvider["p1"] = [Model("m1"), Model("m2")];
        var vm = CreateViewModel();
        await vm.InitializeAsync();
        _discoveryCallCount.Should().Be(1, "InitializeAsync performs the first discovery");

        // Act
        await vm.RefreshIfProviderChangedAsync();

        // Assert
        _discoveryCallCount.Should().Be(1,
            "an unchanged provider and key must not trigger a second discovery");
    }

    [Fact]
    public async Task RefreshIfProviderChangedAsync_Refetches_WhenProviderChanged()
    {
        // Arrange — cache p1's list, then point settings at a different provider.
        _modelsByProvider["p1"] = [Model("m1")];
        _modelsByProvider["p2"] = [Model("m9")];
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        _settings.SelectedProviderId = "p2";

        // Act
        await vm.RefreshIfProviderChangedAsync();

        // Assert
        _discoveryCallCount.Should().Be(2, "a changed provider must re-run model discovery");
        vm.LoadedModels.Select(m => m.Id).Should().BeEquivalentTo(new[] { "m9" },
            "the cached list is replaced by the newly configured provider's models");
    }

    [Fact]
    public async Task RefreshIfProviderChangedAsync_PreservesSelection_WhenNewProviderStillExposesIt()
    {
        // Arrange — a saved embedding model that the new provider also offers.
        _settings.EmbeddingModel = "shared";
        _modelsByProvider["p1"] = [Model("shared")];
        _modelsByProvider["p2"] = [Model("shared"), Model("other")];
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        _settings.SelectedProviderId = "p2";

        // Act
        await vm.RefreshIfProviderChangedAsync();

        // Assert
        vm.EmbeddingModelId.Should().Be("shared",
            "a selection the new provider still offers is kept");
        _settings.EmbeddingModel.Should().Be("shared");
    }

    [Fact]
    public async Task RefreshIfProviderChangedAsync_ClearsSelection_WhenNewProviderDoesNotExposeIt()
    {
        // Arrange — a saved embedding model that the new provider does NOT offer.
        _settings.EmbeddingModel = "old-embed";
        _modelsByProvider["p1"] = [Model("old-embed")];
        _modelsByProvider["p2"] = [Model("new-embed")];
        var vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.EmbeddingModelId.Should().Be("old-embed",
            "the saved selection is restored on first load");

        _settings.SelectedProviderId = "p2";

        // Act
        await vm.RefreshIfProviderChangedAsync();

        // Assert
        vm.EmbeddingModelId.Should().BeEmpty(
            "a selection the new provider does not expose is cleared so no test request can send a stale model id");
        _settings.EmbeddingModel.Should().BeEmpty(
            "the reconciled selection is written back to settings");
    }

    [Fact]
    public async Task RefreshIfProviderChangedAsync_RetriesDiscovery_WhenInitialDiscoveryFailed()
    {
        // Arrange — initial discovery fails because credentials are missing.
        // _discoveredProviderId stays null (LoadModelsCoreAsync returns early at the guard).
        _settings.ApiKey = string.Empty;
        var vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.LoadedModels.Should().BeEmpty("initial discovery could not run without credentials");

        // User fixes configuration in AI Setup.
        _settings.ApiKey = "fake-api-key";
        _modelsByProvider["p1"] = [Model("m1"), Model("m2")];

        // Act — reopen the cached panel; refresh must retry discovery.
        await vm.RefreshIfProviderChangedAsync();

        // Assert
        _discoveryCallCount.Should().Be(1,
            "refresh must retry discovery when the initial attempt left nothing cached");
        vm.LoadedModels.Select(m => m.Id).Should().BeEquivalentTo(new[] { "m1", "m2" },
            "the retry populates the model list after configuration was fixed");
    }

    [Fact]
    public async Task RefreshIfProviderChangedAsync_ClearsSelection_WhenModelLosesRequiredCapability()
    {
        // Arrange — p1 exposes "m1" with Embedding; user selects it.
        _settings.EmbeddingModel = "m1";
        _modelsByProvider["p1"] = [Model("m1", EModelCapability.Embedding)];
        var vm = CreateViewModel();
        await vm.InitializeAsync();
        vm.EmbeddingModelId.Should().Be("m1", "the saved selection is restored on first load");

        // Provider changes to p2, which has "m1" but only with TextGeneration — no Embedding.
        _settings.SelectedProviderId = "p2";
        _modelsByProvider["p2"] = [Model("m1", EModelCapability.TextGeneration)];

        // Act
        await vm.RefreshIfProviderChangedAsync();

        // Assert
        vm.EmbeddingModelId.Should().BeEmpty(
            "a model that still exists but lost the required capability must be cleared");
        _settings.EmbeddingModel.Should().BeEmpty(
            "the reconciled selection is written back to settings");
    }

    private ModelTestPanelViewModel CreateViewModel() =>
        new(_factoryMock.Object, _settings, showDashboard: () => { }, persist: NoPersist);

    // A no-op persistence callback. AppSettings.Save has a static %APPDATA% path and is sealed,
    // so injecting this keeps reconciliation (which persists a cleared selection) hermetic.
    private static void NoPersist()
    {
    }

    private static AIModel Model(string id) => new() { Id = id, DisplayName = id };

    private static AIModel Model(string id, EModelCapability capabilities) =>
        new() { Id = id, DisplayName = id, Capabilities = capabilities };

    // Minimal provider double that is also an IModelDiscoveryProvider, so the VM's
    // `provider is IModelDiscoveryProvider` cast succeeds and returns canned models.
    private sealed class DiscoveryProvider : IAIProvider, IModelDiscoveryProvider
    {
        private readonly IReadOnlyList<AIModel> _models;

        public DiscoveryProvider(IReadOnlyList<AIModel> models) => _models = models;

        public string Id => "test-provider";

        public bool IsEnabled => true;

        public EProviderProtocol Protocol => default;

        public bool SupportsModelDiscovery => true;

        public Task<ChatCompletionResponse> ChatAsync(
            ChatCompletionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Discovery tests never send a chat request.");

        public Task<IReadOnlyList<AIModel>> GetModelsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_models);
    }
}
