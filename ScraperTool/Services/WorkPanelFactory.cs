using AIProviderConnect.Abstractions;
using AIProviderConnect.Services;

using AiCleverness.Abstractions;

using Microsoft.Extensions.DependencyInjection;

using ScraperTool.Data;
using ScraperTool.Data.Repositories;
using ScraperTool.Models;
using ScraperTool.Visualization;

using ScraperTool.ViewModels;

namespace ScraperTool.Services;

public sealed class WorkPanelFactory : IWorkPanelFactory
{
    private readonly AiUrlFixService _aiUrlFix;

    private readonly ProviderBatchAnalysisService _batchAnalysis;

    private readonly ProviderJsonPatchService _jsonPatch;

    private readonly string _manifestPath;

    private readonly ModelPriceResolver _priceResolver;

    private readonly PriceScraperService _scraper;
    private readonly IServiceProvider _sp;

    public WorkPanelFactory(
        PriceScraperService scraper,
        ModelPriceResolver priceResolver,
        AiUrlFixService aiUrlFix,
        ProviderJsonPatchService jsonPatch,
        ProviderBatchAnalysisService batchAnalysis,
        IServiceProvider serviceProvider)
    {
        _scraper = scraper;
        _priceResolver = priceResolver;
        _aiUrlFix = aiUrlFix;
        _jsonPatch = jsonPatch;
        _batchAnalysis = batchAnalysis;
        _sp = serviceProvider;
        _manifestPath = serviceProvider.GetRequiredService<IManifestPathResolver>().Resolve();
    }

    public AiUsageHistoryViewModel CreateAiUsageHistory(Action showDashboard)
    {
        return new AiUsageHistoryViewModel(
            _sp.GetRequiredService<ITokenUsageRepository>(),
            _sp.GetRequiredService<IUnitOfWork>(),
            showDashboard);
    }

    public CheckDataPanelViewModel CreateCheckDataPanel(
        AiDefinitionAnalyzer analyzer,
        Action showDashboard,
        Action openAiSetup)
    {
        var catalog = _sp.GetRequiredService<ProviderCatalog>();
        return new CheckDataPanelViewModel(
            catalog,
            _manifestPath,
            _sp.GetRequiredService<ValidationOrchestrationService>(),
            _sp.GetRequiredService<SuggestionProcessingService>(),
            _sp.GetRequiredService<IssueSyncService>(),
            analyzer,
            showDashboard,
            openAiSetup,
            _sp.GetRequiredService<IClipboardService>(),
            new OperationTimer(),
            new OperationLogger(),
            _sp.GetRequiredService<AppSettings>(),
            _jsonPatch,
            _sp.GetRequiredService<IManifestPathResolver>(),
            _batchAnalysis);
    }

    public ProviderManualEditorViewModel CreateManualEditor(
        Action showDashboard)
    {
        var catalog = _sp.GetRequiredService<ProviderCatalog>();
        return new ProviderManualEditorViewModel(
            catalog,
            _manifestPath,
            showDashboard,
            _sp.GetRequiredService<IClipboardService>(),
            aiUrlFix: _aiUrlFix,
            validator: _sp.GetRequiredService<ProviderDefinitionValidator>(),
            jsonPatch: _jsonPatch,
            settings: _sp.GetRequiredService<AppSettings>());
    }

    public ScrapePanelViewModel CreateScrapePanel(
        IProviderCatalog catalog,
        Action showDashboard)
    {
        return new ScrapePanelViewModel(
            _scraper,
            catalog,
            _sp.GetRequiredService<AppSettings>(),
            _manifestPath,
            _sp.GetRequiredService<IProviderRepository>(),
            _sp.GetRequiredService<IModelRepository>(),
            _sp.GetRequiredService<IUnitOfWork>(),
            showDashboard,
            _sp.GetRequiredService<IScraperConfigBuilder>(),
            _sp.GetRequiredService<IClipboardService>());
    }

    public SettingsViewModel CreateSettings()
    {
        return new SettingsViewModel(
            _sp.GetRequiredService<AppSettings>(),
            _manifestPath);
    }

    public DecisionTreeViewerViewModel CreateDecisionTreeViewer()
    {
        return new DecisionTreeViewerViewModel(
            _sp.GetRequiredService<DecisionTreeDotExporter>(),
            _sp.GetRequiredService<IDecisionTreeLoader>());
    }

    public ViewModels.ModelTestPanelViewModel CreateModelTest(Action showDashboard)
    {
        return new ViewModels.ModelTestPanelViewModel(
            _sp.GetRequiredService<ITransientCredentialProviderFactory>(),
            _sp.GetRequiredService<AppSettings>(),
            showDashboard);
    }
}
