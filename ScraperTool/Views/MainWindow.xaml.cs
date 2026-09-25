using System.Net.Http;
using System.Windows;

using AIProviderConnect.Services;

using Microsoft.Extensions.DependencyInjection;

using ScraperTool.Data;
using ScraperTool.Data.Repositories;
using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Services.UrlResearch;
using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.Validation.Checks;
using ScraperTool.ViewModels;

using WebTools.NET.Abstractions;

namespace ScraperTool.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeViewModelAsync();
    }

    private async Task InitializeViewModelAsync()
    {
        var scope = App.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var settings = sp.GetRequiredService<AppSettings>();
        var catalog = sp.GetRequiredService<ProviderCatalog>();
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var http = httpFactory.CreateClient(HttpConstants.ScraperHttpClientName);

        var analyzer = sp.GetRequiredService<AiDefinitionAnalyzer>();
        var priceResolver = new ModelPriceResolver(
            sp.GetRequiredService<AppSettings>());
        var browserFetcher = sp.GetRequiredService<IWebContentFetcher>();
        var providerFactory = sp.GetRequiredService<Services.AIProviderFactory>();
        var aiLayoutAnalyzer = new Services.Parsing.AiLayoutAnalyzer(
            providerFactory,
            settings);
        var scraper = new PriceScraperService(http, browserFetcher, aiLayoutAnalyzer);
        var manifestPathResolver = sp.GetRequiredService<IManifestPathResolver>();
        var researchAgent = sp.GetRequiredService<IUrlResearchService>();
        var regionProvider = sp.GetRequiredService<IGeoRegionProvider>();
        var aiUrlFix = new AiUrlFixService(
            new AiFixConfigurationAdapter(analyzer),
            priceResolver,
            sp.GetRequiredService<IValidationIssueRepository>(),
            sp.GetRequiredService<ITokenUsageRepository>(),
            sp.GetRequiredService<IUnitOfWork>(),
            catalog,
            researchAgent,
            regionProvider,
            sp.GetRequiredService<IWebContentFetcher>(),
            sp.GetRequiredService<ProviderResearchCache>(),
            http,
            sp.GetRequiredService<IPricingPageVerifier>());
        var jsonPatch = new ProviderJsonPatchService(manifestPathResolver.Resolve());
        var batchAnalysis = new ProviderBatchAnalysisService(analyzer, catalog, priceResolver, settings.PrimaryModel);

        var factory = new WorkPanelFactory(scraper, priceResolver, aiUrlFix, jsonPatch, batchAnalysis, sp);

        var vm = new MainViewModel(
            analyzer,
            providerFactory,
            catalog,
            settings,
            sp.GetRequiredService<IProviderRepository>(),
            sp.GetRequiredService<IValidationIssueRepository>(),
            factory);

        DataContext = vm;
        await vm.InitializeAsync();
    }
}
