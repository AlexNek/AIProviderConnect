using System.Collections.ObjectModel;
using System.Windows;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Views;

using Serilog;

namespace ScraperTool.ViewModels;

public sealed partial class AiSetupViewModel : ObservableObject
{
    private readonly AiAnalysisService _ai;

    private readonly ITransientCredentialProviderFactory _providerFactory;

    private readonly AppSettings _settings;

    [ObservableProperty]
    private string? _apiKey;

    [ObservableProperty]
    private string _fallbackModel = string.Empty;

    [ObservableProperty]
    private string _fallbackModelPriceText = string.Empty;

    [ObservableProperty]
    private bool _isTesting;

    private List<AIModel>? _loadedAIModels;

    [ObservableProperty]
    private string _primaryModel = string.Empty;

    [ObservableProperty]
    private string _primaryModelPriceText = string.Empty;

    [ObservableProperty]
    private ProviderSelectionItem? _selectedProvider;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    public ObservableCollection<ModelSelectionItem> LoadedModels { get; } = [];

    public ObservableCollection<ProviderSelectionItem> Options { get; } = [];

    private bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.ApiKey)
        && !string.IsNullOrWhiteSpace(_settings.SelectedProviderId);

    public AiSetupViewModel(
        AppSettings settings,
        IProviderCatalog catalog,
        AiAnalysisService ai,
        ITransientCredentialProviderFactory providerFactory)
    {
        _settings = settings;
        _ai = ai;
        _providerFactory = providerFactory;

        foreach (var def in catalog.All)
        {
            Options.Add(ProviderSelectionItem.FromDefinition(def, catalog.GetResearchMetadata(def.Id)));
        }

        var saved = Options.FirstOrDefault(p =>
            string.Equals(
                p.ProviderId,
                settings.SelectedProviderId,
                StringComparison.OrdinalIgnoreCase));
        if (saved is not null)
        {
            _selectedProvider = saved;
        }

        _apiKey = settings.ApiKey;
        _primaryModel = settings.PrimaryModel;
        _primaryModelPriceText = FormatPriceText(
            settings.PrimaryModelPromptPrice,
            settings.PrimaryModelCompletionPrice);
        _fallbackModel = settings.FallbackModel;
        _fallbackModelPriceText = FormatPriceText(
            settings.FallbackModelPromptPrice,
            settings.FallbackModelCompletionPrice);
    }

    [RelayCommand]
    private void BrowseProviders()
    {
        Log.Information("User opened provider grid selector");
        var window = new ProviderGridSelectorWindow
                         {
                             DataContext = this,
                             Owner = GetActiveWindow() ?? Application.Current.MainWindow
                         };
        if (window.ShowDialog() == true && window.SelectedItem is not null)
        {
            SelectedProvider = window.SelectedItem;
        }
    }

    [RelayCommand]
    private void ClearApiKey()
    {
        Log.Information("User cleared API key");
        ApiKey = string.Empty;
    }

    private async Task<AIModel?> FindModelInProviderListingAsync(string modelId)
    {
        if (_loadedAIModels is not null)
            return _loadedAIModels.FirstOrDefault(m =>
                m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));

        var providerId = _settings.SelectedProviderId;
        if (string.IsNullOrWhiteSpace(providerId))
            return null;

        IAIProvider providerInstance;
        try
        {
            providerInstance = _providerFactory.GetProvider(providerId);
        }
        catch
        {
            return null;
        }

        if (!TryGetDiscoveryProvider(providerInstance, out var discoveryProvider))
            return null;

        try
        {
            var models = await discoveryProvider.GetModelsAsync();
            _loadedAIModels = models.ToList();
            LoadedModels.Clear();
            foreach (var m in models.OrderBy(m => m.Id))
                LoadedModels.Add(ModelSelectionItem.FromAIModel(m));
            return _loadedAIModels.FirstOrDefault(m =>
                m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static string FormatPriceText(decimal? prompt, decimal? completion)
    {
        if (!prompt.HasValue && !completion.HasValue)
            return "Price: not available (select model to capture pricing from provider)";
        return $"Price: ${prompt:0.######} in / ${completion:0.######} out per 1M tokens";
    }

    private static Window? GetActiveWindow() =>
        Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive);

    private static bool TryGetDiscoveryProvider(
        IAIProvider provider,
        out IModelDiscoveryProvider discoveryProvider)
    {
        if (provider is IModelDiscoveryProvider candidate && candidate.SupportsModelDiscovery)
        {
            discoveryProvider = candidate;
            return true;
        }

        discoveryProvider = null!;
        return false;
    }

    partial void OnFallbackModelChanged(string value)
    {
        _ = RefreshModelPriceAsync(
            value,
            (p, c) =>
                {
                    _settings.FallbackModelPromptPrice = p;
                    _settings.FallbackModelCompletionPrice = c;
                    FallbackModelPriceText = FormatPriceText(p, c);
                });
    }

    partial void OnPrimaryModelChanged(string value)
    {
        _ = RefreshModelPriceAsync(
            value,
            (p, c) =>
                {
                    _settings.PrimaryModelPromptPrice = p;
                    _settings.PrimaryModelCompletionPrice = c;
                    PrimaryModelPriceText = FormatPriceText(p, c);
                });
    }

    partial void OnStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatus));
    }

    private async Task RefreshModelPriceAsync(
        string modelId,
        Action<decimal?, decimal?> updatePrices)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            updatePrices(null, null);
            return;
        }

        var model = await FindModelInProviderListingAsync(modelId);
        if (model is not null)
        {
            updatePrices(model.PromptPrice, model.CompletionPrice);
        }
        else
        {
            updatePrices(null, null);
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedProvider is null)
        {
            return;
        }

        Log.Information("User saved AI setup settings for {Provider}", SelectedProvider.ProviderId);
        _settings.ApiKey = ApiKey ?? string.Empty;
        _settings.SelectedProviderId = SelectedProvider.ProviderId;
        _settings.PrimaryModel = PrimaryModel;
        _settings.FallbackModel = FallbackModel;
        _settings.Save();
        _ai.UpdateSettings(_settings);
    }

    [RelayCommand]
    private async Task SelectFallbackModelAsync()
    {
        await SelectModelAsync(
            FallbackModel,
            value => FallbackModel = value,
            (prompt, completion) =>
                {
                    _settings.FallbackModelPromptPrice = prompt;
                    _settings.FallbackModelCompletionPrice = completion;
                    FallbackModelPriceText = FormatPriceText(prompt, completion);
                });
    }

    private async Task SelectModelAsync(
        string currentModelId,
        Action<string> setId,
        Action<decimal?, decimal?> setPrices)
    {
        if (!IsConfigured)
        {
            MessageBox.Show(
                "Configure API key first.",
                "Not configured",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (LoadedModels.Count == 0)
        {
            try
            {
                var provider =
                    _providerFactory.GetProvider(_settings.SelectedProviderId ?? string.Empty);
                if (!TryGetDiscoveryProvider(provider, out var discoveryProvider))
                    throw new InvalidOperationException(
                        "Selected provider does not support model discovery.");
                var models = await discoveryProvider.GetModelsAsync();
                _loadedAIModels = models.ToList();
                LoadedModels.Clear();
                foreach (var m in models.OrderBy(m => m.Id))
                    LoadedModels.Add(ModelSelectionItem.FromAIModel(m));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load models");
                MessageBox.Show(
                    $"Failed to load models: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        var window = new ModelGridSelectorWindow
                         {
                             Owner = GetActiveWindow() ?? Application.Current.MainWindow
                         };
        window.LoadModels(LoadedModels, initialSelectionId: currentModelId);
        if (window.ShowDialog() == true && window.SelectedItem is not null)
        {
            setId(window.SelectedItem.Id);
            var model = _loadedAIModels?.FirstOrDefault(m =>
                string.Equals(m.Id, window.SelectedItem.Id, StringComparison.OrdinalIgnoreCase));
            if (model is not null)
                setPrices(model.PromptPrice, model.CompletionPrice);
        }
    }

    [RelayCommand]
    private async Task SelectPrimaryModelAsync()
    {
        await SelectModelAsync(
            PrimaryModel,
            value => PrimaryModel = value,
            (prompt, completion) =>
                {
                    _settings.PrimaryModelPromptPrice = prompt;
                    _settings.PrimaryModelCompletionPrice = completion;
                    PrimaryModelPriceText = FormatPriceText(prompt, completion);
                });
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (SelectedProvider is null)
        {
            StatusText = "Select a provider first.";
            return;
        }

        Log.Information(
            "User pressed Test Connection for {Provider} (apiKeyProvided: {HasKey})",
            SelectedProvider.ProviderId,
            !string.IsNullOrWhiteSpace(ApiKey));

        IsTesting = true;
        StatusText = "Testing...";

        try
        {
            var provider = _providerFactory.GetProvider(
                SelectedProvider.ProviderId,
                ApiKey ?? string.Empty);
            var request = new ChatCompletionRequest
                              {
                                  Model = string.Empty,
                                  Messages =
                                      [
                                          new ChatMessage
                                              {
                                                  Role = EChatRole.User,
                                                  Content = "Reply with exactly one word: ok"
                                              }
                                      ],
                                  Temperature = 0f
                              };
            var response = await provider.ChatAsync(request);
            if (response is not null && !string.IsNullOrWhiteSpace(response.Content))
            {
                Log.Information(
                    "Test connection for {Provider} succeeded",
                    SelectedProvider.ProviderId);
                StatusText = "Connection successful!";
            }
            else
            {
                Log.Warning(
                    "Test connection for {Provider} returned failure (no exception)",
                    SelectedProvider.ProviderId);
                StatusText = "Connection failed. Ensure a model is configured in Settings first.";
            }
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "Test connection failed for provider {Provider}",
                SelectedProvider.ProviderId);
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }
}
