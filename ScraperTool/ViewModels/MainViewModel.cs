using AIProviderConnect.Abstractions;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ScraperTool.Data.Repositories;
using ScraperTool.Models;
using ScraperTool.Services;

using Serilog;

namespace ScraperTool.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AiDefinitionAnalyzer _analyzer;

    private readonly IProviderCatalog _catalog;

    private readonly IValidationIssueRepository _issueRepo;

    private readonly IWorkPanelFactory _panelFactory;

    private readonly ITransientCredentialProviderFactory _providerFactory;

    private readonly IProviderRepository _providerRepo;

    private readonly AppSettings _settings;

    [ObservableProperty]
    private bool _aiConfigured;

    private AiUsageHistoryViewModel? _cachedAiUsageHistory;

    private CheckDataPanelViewModel? _cachedCheckDataPanel;

    private ProviderManualEditorViewModel? _cachedManualEditor;

    private ScrapePanelViewModel? _cachedScrapePanel;

    private DecisionTreeViewerViewModel? _cachedDecisionTreeViewer;

    [ObservableProperty]
    private object? _currentPanel;

    [ObservableProperty]
    private string _providerCountLabel = "No providers in catalog";

    [ObservableProperty]
    private bool _showingDashboard = true;

    [ObservableProperty]
    private bool _showingWorkPanel;

    [ObservableProperty]
    private string _workTitle = string.Empty;

    public MainViewModel(
        AiDefinitionAnalyzer analyzer,
        ITransientCredentialProviderFactory providerFactory,
        IProviderCatalog catalog,
        AppSettings settings,
        IProviderRepository providerRepo,
        IValidationIssueRepository issueRepo,
        IWorkPanelFactory panelFactory)
    {
        _analyzer = analyzer;
        _providerFactory = providerFactory;
        _catalog = catalog;
        _settings = settings;
        _providerRepo = providerRepo;
        _issueRepo = issueRepo;
        _panelFactory = panelFactory;

        _aiConfigured = analyzer.IsAvailable;
    }

    public async Task InitializeAsync()
    {
        await LoadFromDbAsync();
    }

    [RelayCommand]
    private async Task GoToAiUsageHistoryAsync()
    {
        Log.Information("User opened AI Usage History panel");
        if (_cachedAiUsageHistory is null)
        {
            _cachedAiUsageHistory = _panelFactory.CreateAiUsageHistory(() => ShowDashboard());
            OpenWorkPanel("AI Usage History", _cachedAiUsageHistory);
            await _cachedAiUsageHistory.InitializeAsync();
        }
        else
        {
            OpenWorkPanel("AI Usage History", _cachedAiUsageHistory);
        }
    }

    [RelayCommand]
    private async Task GoToCheckDataPanelAsync()
    {
        Log.Information("User opened Check Provider Data panel");
        if (_cachedCheckDataPanel is null)
        {
            _cachedCheckDataPanel = _panelFactory.CreateCheckDataPanel(
                _analyzer,
                () => ShowDashboard(),
                () => OpenAiSetup());
            OpenWorkPanel("Check Provider Data", _cachedCheckDataPanel);
            await _cachedCheckDataPanel.InitializeAsync();
        }
        else
        {
            OpenWorkPanel("Check Provider Data", _cachedCheckDataPanel);
        }
    }

    [RelayCommand]
    private void GoToManualEditor()
    {
        Log.Information("User opened Manual Provider Editor panel");
        if (_cachedManualEditor is null)
        {
            _cachedManualEditor = _panelFactory.CreateManualEditor(
                () => ShowDashboard());
            OpenWorkPanel("Manual Provider Editor", _cachedManualEditor);
            _cachedManualEditor.Initialize();
        }
        else
        {
            OpenWorkPanel("Manual Provider Editor", _cachedManualEditor);
        }
    }

    [RelayCommand]
    private async Task GoToScrapePanelAsync()
    {
        Log.Information("User opened Scrape Prices panel");
        if (_cachedScrapePanel is null)
        {
            _cachedScrapePanel = _panelFactory.CreateScrapePanel(
                _catalog,
                () => ShowDashboard());
            OpenWorkPanel("Scrape Prices", _cachedScrapePanel);
            await _cachedScrapePanel.InitializeAsync();
        }
        else
        {
            OpenWorkPanel("Scrape Prices", _cachedScrapePanel);
        }
    }

    [RelayCommand]
    private void GoToDecisionTreeViewer()
    {
        Log.Information("User opened Decision Tree Viewer panel");
        if (_cachedDecisionTreeViewer is null)
        {
            _cachedDecisionTreeViewer = _panelFactory.CreateDecisionTreeViewer();
            OpenWorkPanel("Decision Tree Viewer", _cachedDecisionTreeViewer);
        }
        else
        {
            OpenWorkPanel("Decision Tree Viewer", _cachedDecisionTreeViewer);
        }
    }

    private async Task LoadFromDbAsync()
    {
        var catalogCount = _catalog.All.Count;
        try
        {
            var dbProviders = await _providerRepo.GetAllAsync();
            var totalPrices = dbProviders.Sum(p => p.Models.Count);
            var issues = (await _issueRepo.GetAllAsync()).Count;
            if (dbProviders.Count > 0)
            {
                ProviderCountLabel =
                    $"{catalogCount} providers in catalog ({dbProviders.Count} scraped, {totalPrices} prices)";
            }
            else
            {
                ProviderCountLabel =
                    $"{catalogCount} providers in catalog | Run 'Scrape Prices' to load data";
            }
        }
        catch
        {
            ProviderCountLabel = $"{catalogCount} providers in catalog";
        }
    }

    [RelayCommand]
    private void OpenAiSetup()
    {
        Log.Information("User opened AI Setup dialog");
        var vm = new AiSetupViewModel(_settings, _catalog, _analyzer.AiService, _providerFactory);
        var dialog = new Views.AiSetupWindow
                         {
                             DataContext = vm,
                             Owner = System.Windows.Application.Current.Windows
                                 .OfType<Views.MainWindow>().FirstOrDefault()
                         };
        if (dialog.ShowDialog() == true)
        {
            AiConfigured = _analyzer.IsAvailable;
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        Log.Information("User opened Settings dialog");
        var vm = _panelFactory.CreateSettings();
        var dialog = new Views.SettingsWindow
                         {
                             DataContext = vm,
                             Owner = System.Windows.Application.Current.Windows
                                 .OfType<Views.MainWindow>().FirstOrDefault()
                         };
        dialog.ShowDialog();
    }

    private void OpenWorkPanel(string title, object panel)
    {
        WorkTitle = title;
        CurrentPanel = panel;
        ShowingDashboard = false;
        ShowingWorkPanel = true;
    }

    [RelayCommand]
    private void ShowDashboard()
    {
        Log.Information("User navigated to dashboard");
        CurrentPanel = null;
        ShowingDashboard = true;
        ShowingWorkPanel = false;
    }
}
