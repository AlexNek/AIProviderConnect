using System.Collections.ObjectModel;
using System.Windows.Threading;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ScraperTool.Data;
using ScraperTool.Data.Repositories;
using ScraperTool.Models;
using ScraperTool.Services;

namespace ScraperTool.ViewModels;

public sealed partial class ScrapePanelViewModel : ObservableObject
{
    private readonly IProviderCatalog _catalog;

    private readonly IClipboardService _clipboardService;

    private readonly string _manifestPath;

    private readonly IModelRepository _modelRepo;

    private readonly Action _navigateBack;

    private readonly IProviderRepository _providerRepo;

    private readonly PriceScraperService _scraper;

    private readonly IScraperConfigBuilder _scraperConfigBuilder;

    private readonly AppSettings _settings;

    private readonly IOperationLogger _uiLogger;

    private readonly IUnitOfWork _uow;

    [ObservableProperty]
    private bool _isBusy;

    private List<ProviderDefinition> _providers = [];

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private int _totalPricesFound;

    public bool HasResults => Results.Count > 0;

    public ReadOnlyObservableCollection<string> Log => _uiLogger.Entries;

    public ObservableCollection<ProviderPriceResult> Results { get; } = [];

    public IOperationLogger UiLogger => _uiLogger;

    public ScrapePanelViewModel(
        PriceScraperService scraper,
        IProviderCatalog catalog,
        AppSettings settings,
        string manifestPath,
        IProviderRepository providerRepo,
        IModelRepository modelRepo,
        IUnitOfWork uow,
        Action navigateBack,
        IScraperConfigBuilder scraperConfigBuilder,
        IClipboardService clipboardService,
        IOperationLogger? logger = null)
    {
        _scraper = scraper;
        _catalog = catalog;
        _settings = settings;
        _manifestPath = manifestPath;
        _providerRepo = providerRepo;
        _modelRepo = modelRepo;
        _uow = uow;
        _navigateBack = navigateBack;
        _scraperConfigBuilder = scraperConfigBuilder;
        _clipboardService = clipboardService;
        _uiLogger = logger ?? new OperationLogger();
    }

    public async Task InitializeAsync()
    {
        try
        {
            var providers = await _providerRepo.GetAllAsync();
            foreach (var provider in providers)
            {
                // Resolve pricing URL from the provider catalog research metadata
                var apiPricingUrl = _catalog.GetResearchMetadata(provider.ProviderId)?.ApiPricingUrl
                                    ?? string.Empty;

                foreach (var model in provider.Models)
                {
                    Results.Add(
                        new ProviderPriceResult
                            {
                                ProviderId = provider.ProviderId,
                                ModelDisplayName = model.DisplayName,
                                PromptPrice = model.PromptPrice,
                                CompletionPrice = model.CompletionPrice,
                                PriceUnit = ScraperTool.Models.EPriceUnit.Per1M,
                                ApiPricingUrl = apiPricingUrl,
                                Source = $"DB ({provider.LastFetchedAt:g})"
                            });
                }
            }

            TotalPricesFound = Results.Count;

            if (Results.Count > 0)
            {
                _uiLogger.Add($"Loaded {Results.Count} price(s) from database.");
                StatusText = $"{Results.Count} prices from previous run.";
            }

            UpdateResultProps();
        }
        catch (Exception ex)
        {
            _uiLogger.Add($"Failed to load from database: {ex.Message}");
        }
    }

    private ScraperConfiguration BuildConfig(ProviderDefinition provider) =>
        _scraperConfigBuilder.Build(provider, _catalog.GetResearchMetadata(provider.Id));

    [RelayCommand]
    private void ClearResults()
    {
        Serilog.Log.Information("User cleared scrape results");
        Results.Clear();
        TotalPricesFound = 0;
        _uiLogger.Clear();
        StatusText = "Cleared";
        UpdateResultProps();
        _navigateBack();
    }

    [RelayCommand]
    private void CopyLog()
    {
        if (_uiLogger.Entries.Count == 0) return;
        Serilog.Log.Information(
            "User copied scrape log to clipboard ({LineCount} lines)",
            _uiLogger.Entries.Count);
        _clipboardService.SetText(string.Join(Environment.NewLine, _uiLogger.Entries));
        StatusText = "Log copied to clipboard.";
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync(ProviderPriceResult? item)
    {
        if (item is null) return;

        try
        {
            var deleted = await _modelRepo.DeleteByDisplayNameAsync(
                              item.ProviderId,
                              item.ModelDisplayName);
            await _uow.SaveChangesAsync();
            Results.Remove(item);
            TotalPricesFound = Results.Count;
            UpdateResultProps();

            var msg = deleted > 0
                          ? $"Deleted '{item.ModelDisplayName}' from {item.ProviderId} ({deleted} row(s) removed from DB)."
                          : $"Removed '{item.ModelDisplayName}' from list (not found in DB).";

            _uiLogger.Add(msg);
            Serilog.Log.Information(
                "User deleted model price: {Provider}/{Model}, {Count} DB rows",
                item.ProviderId,
                item.ModelDisplayName,
                deleted);
            StatusText = msg;
        }
        catch (Exception ex)
        {
            _uiLogger.Add($"Delete failed: {ex.Message}");
            StatusText = "Delete failed — see log.";
        }
    }

    [RelayCommand]
    private async Task StartWorkAsync()
    {
        if (!ValidateManifestPath()) return;

        Serilog.Log.Information("User started scraping prices");
        _uiLogger.Clear();
        Results.Clear();
        _uiLogger.Add("=== Scrape Prices started ===");
        IsBusy = true;
        StatusText = "Loading provider definitions...";

        try
        {
            _providers = _catalog.All.ToList();
            var totalProviders = _providers.Count;
            var completedProviders = 0;
            TotalPricesFound = 0;

            _uiLogger.Add($"Loaded {_providers.Count} provider definitions.");

            foreach (var provider in _providers)
            {
                var apiPricingUrl = _catalog.GetResearchMetadata(provider.Id)?.ApiPricingUrl;
                if (string.IsNullOrWhiteSpace(apiPricingUrl)
                    || apiPricingUrl == ProviderJsonFields.NotApplicable)
                {
                    _uiLogger.Add($"  [{provider.Id}] Skipped — no pricing URL.");
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    completedProviders++;
                    continue;
                }

                StatusText = $"Scraping {provider.DisplayName}...";
                _uiLogger.Add($"  [{provider.Id}] Scraping {apiPricingUrl} ...");
                await Dispatcher.Yield(DispatcherPriority.Background);

                try
                {
                    var config = BuildConfig(provider);
                    var result = await _scraper.ScrapeWithDiagnosticsAsync(config);
                    var prices = result.Prices;

                    if (prices.Count > 0)
                    {
                        TotalPricesFound += prices.Count;
                        _uiLogger.Add($"    Found {prices.Count} model prices.");
                        if (!string.IsNullOrWhiteSpace(result.Diagnostic))
                            _uiLogger.Add($"    {result.Diagnostic}");
                        await Dispatcher.Yield(DispatcherPriority.Background);

                        var providerEntry = await _providerRepo.UpsertAsync(
                                                provider.Id,
                                                provider.DisplayName);
                        await _uow.SaveChangesAsync();
                        await _providerRepo.TouchFetchedAtAsync(provider.Id);

                        var dtos = prices.Select(p => new ModelSaveDto
                                                          {
                                                              ModelId = p.ModelDisplayName,
                                                              DisplayName = p.ModelDisplayName,
                                                              PromptPrice = p.PromptPrice,
                                                              CompletionPrice = p.CompletionPrice,
                                                          });
                        await _modelRepo.UpsertRangeAsync(providerEntry.Id, dtos);
                        await _uow.SaveChangesAsync();

                        foreach (var price in prices)
                            Results.Add(price);
                    }
                    else
                    {
                        _uiLogger.Add($"    No prices found — {result.Diagnostic}");
                        await Dispatcher.Yield(DispatcherPriority.Background);
                    }
                }
                catch (Exception ex)
                {
                    _uiLogger.Add($"    Error: {ex.Message}");
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }

                completedProviders++;
            }

            StatusText =
                $"Scraping complete. Found {TotalPricesFound} prices across {totalProviders} providers. Saved to database.";
            UpdateResultProps();
        }
        catch (Exception ex)
        {
            _uiLogger.Add($"Fatal error: {ex.Message}");
            StatusText = "Error — see log for details.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateResultProps()
    {
        OnPropertyChanged(nameof(HasResults));
    }

    private bool ValidateManifestPath()
    {
        if (string.IsNullOrWhiteSpace(_manifestPath))
        {
            _uiLogger.Add("Please select the provider manifests folder first.");
            return false;
        }

        if (!System.IO.Directory.Exists(_manifestPath))
        {
            _uiLogger.Add($"Directory not found: {_manifestPath}");
            return false;
        }

        return true;
    }
}
