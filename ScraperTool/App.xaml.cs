using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;

using AiCleverness.Abstractions;
using AiCleverness.Models;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Polly;

using ScraperTool.Data;
using ScraperTool.Data.Repositories;
using ScraperTool.Models;
using ScraperTool.Services;
using ScraperTool.Services.UrlFix;
using ScraperTool.Services.UrlResearch;
using ScraperTool.Services.UrlResearch.Abstractions;
using ScraperTool.Services.UrlResearch.Adapters;
using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.UrlResearch.DependencyInjection;
using ScraperTool.Services.UrlResearch.QualityGates;
using ScraperTool.Services.UrlResearch.Validators;
using ScraperTool.Services.Validation;
using ScraperTool.Services.Validation.Checks;

using Serilog;

using WebTools.NET;
using WebTools.NET.Abstractions;
using WebTools.NET.Geo;

namespace ScraperTool;

public sealed partial class App : Application
{
    private const string MutexName = "ScraperTool_SingleInstance_Mutex";

    private const int SW_RESTORE = 9;

    private const string WindowClassName = "ScraperTool_SingleInstance_WindowMessage";

    private Mutex? _mutex;

    public static IServiceProvider Services { get; private set; } = null!;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("**** ScraperTool shutting down ****");
        Log.Debug("OnExit: disposing service providers");
        // Dispose BrowserEngineSwitch first — before the ServiceProvider — so
        // CloakBrowserHandle.CloseAsync still has a live Playwright connection.
        if (Services.GetService<BrowserEngineSwitch>() is { } bs)
            bs.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Services is IAsyncDisposable asyncDisposable)
            asyncDisposable.DisposeAsync().GetAwaiter().GetResult();
        else if (Services is IDisposable disposable)
            disposable.Dispose();
        Log.Debug("OnExit: flushing and closing Serilog");
        Log.CloseAndFlush();

        // Release the mutex
        _mutex?.ReleaseMutex();
        _mutex = null;

        Log.Debug("OnExit: calling base.OnExit");
        base.OnExit(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // All relative paths (Serilog "logs/scraper-.log") resolve against the
        // exe folder, regardless of where the app was launched from.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        // Configure logging FIRST so we can log the mutex check
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        Log.Debug("OnStartup: checking for existing instance");

        // Single-instance check using mutex
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            Log.Information(
                "Another instance is already running. Attempting to activate existing instance.");

            // Try to find window by title using Win32 API
            var hwnd = FindWindow(null, "AI Provider Catalog Researcher");
            if (hwnd != IntPtr.Zero)
            {
                Log.Debug("Found existing window, activating it");
                ActivateWindow(hwnd);
            }
            else
            {
                Log.Debug("Could not find existing window");
            }

            Shutdown();
            return;
        }

        Log.Debug("OnStartup: this is the first instance, continuing startup");
        Log.Information("**** ScraperTool starting up ****");

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Log.Debug("OnStartup: loading AppSettings from {AppDataPath}", appDataPath);
        var settings = AppSettings.Load();

        Log.Debug("OnStartup: configuring DI services (DbContext, repositories, migrator)");
        var services = new ServiceCollection();
        ConfigureServices(services, settings);

        Log.Debug("OnStartup: building ServiceProvider");
        Services = services.BuildServiceProvider();

        Log.Debug("OnStartup: validating AiCleverness DI configuration");
        Task.Run(() => Services.ValidateAiClevernessAsync()).GetAwaiter().GetResult();

        Log.Debug("OnStartup: validating required URL research config files");
        Task.Run(() => Services.GetRequiredService<FileBasedMetadataProvider>().ValidateRequiredConfigAsync())
            .GetAwaiter().GetResult();

        Log.Debug("OnStartup: running database migrations");
        using var scope = Services.CreateScope();
        Task.Run(() => scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>().MigrateAsync())
            .GetAwaiter().GetResult();

        Log.Debug("OnStartup: startup complete, window will be created by StartupUri");
        Log.Information("**** ScraperTool startup complete ****");
    }

    private static void ActivateWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private static void ActivateWindow(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        SetForegroundWindow(hwnd);
    }

    internal static void ConfigureServices(IServiceCollection services, AppSettings settings)
    {
        services.AddLogging(lb => lb.AddSerilog(Log.Logger, dispose: false));
        var dbDir = Path.GetDirectoryName(settings.DatabasePath);
        if (!string.IsNullOrEmpty(dbDir))
            Directory.CreateDirectory(dbDir);
        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlite($"Data Source={settings.DatabasePath}"));

        // HttpClient factory for proper connection management
        services.AddHttpClient(
                HttpConstants.ScraperHttpClientName,
                client =>
                    {
                        client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpConstants.UserAgent);
                        client.DefaultRequestHeaders.Accept.ParseAdd(HttpConstants.AcceptHtml);
                        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
                        client.Timeout = TimeSpan.FromSeconds(12);
                    })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                                                          {
                                                              AutomaticDecompression =
                                                                  DecompressionMethods.GZip |
                                                                  DecompressionMethods.Deflate |
                                                                  DecompressionMethods.Brotli,
                                                              AllowAutoRedirect = true,
                                                              CookieContainer =
                                                                  new CookieContainer()
                                                          })
            .AddTransientHttpErrorPolicy(policy =>
                policy.WaitAndRetryAsync(
                    3,
                    retryAttempt =>
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

        // Named HTTP client for AI provider API calls (no auto-redirect for manual redirect tracking).
        // LLM completions on slow free models can take well over 30s — keep this above
        // the agent runtime's 60s per-turn budget so the runtime governs timeouts.
        services.AddHttpClient(
                HttpConstants.AiApiHttpClientName,
                client => { client.Timeout = TimeSpan.FromSeconds(120); })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                                                          {
                                                              AllowAutoRedirect = false
                                                          });

        // Core services
        services.AddSingleton(settings);
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IDatabaseMigrator, DatabaseMigrator>();
        services.AddScoped<IProviderRepository, ProviderRepository>();
        services.AddScoped<IModelRepository, ModelRepository>();
        services.AddScoped<IValidationIssueRepository, ValidationIssueRepository>();
        services.AddScoped<ITokenUsageRepository, TokenUsageRepository>();
        services.AddScoped<IValidationMetadataService, ValidationMetadataService>();

        services.AddSingleton<IProviderSchemaValidator, ProviderSchemaValidator>();
        services.AddSingleton<IUrlReachabilityChecker>(sp =>
            new UrlReachabilityChecker(
                sp.GetRequiredService<IWebAccessService>(),
                sp.GetRequiredService<IWebContentFetcher>()));
        services.AddSingleton<IContentAnalyzer, ContentAnalyzer>();
        services.AddSingleton<IDuplicateIdChecker, DuplicateIdChecker>();
        services.AddSingleton<IProviderManifestReader, ProviderManifestReader>();
        services.AddSingleton<ISelfHostedApplicabilityEvaluator, SelfHostedApplicabilityEvaluator>();
        services.AddSingleton<ISubscriptionConfiguredChecker, SubscriptionConfiguredChecker>();
        services.AddSingleton<IServiceRetirementProbe>(sp =>
            new ServiceRetirementProbe(sp.GetRequiredService<IWebContentFetcher>()));
        services.AddSingleton<IPageContentProbe>(sp =>
            new PageContentProbe(
                sp.GetRequiredService<IWebContentFetcher>(),
                sp.GetRequiredService<IContentAnalyzer>()));
        services.AddSingleton<IPricingPageVerifier>(sp =>
            new PricingPageVerifier(
                sp.GetRequiredService<IWebContentFetcher>(),
                sp.GetRequiredService<IContentAnalyzer>()));
        services.AddSingleton<IWebsiteOwnershipJudge>(sp =>
            new WebsiteOwnershipJudge(sp.GetRequiredService<IWebContentFetcher>()));
        services.AddSingleton<IApiEndpointProbe>(sp =>
            new ApiEndpointProbe(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IUrlReachabilityChecker>()));
        services.AddSingleton<IManualBrowserVerifier, ManualBrowserVerifier>();
        services.AddSingleton<IUrlFieldChecker>(sp =>
            new UrlFieldChecker(
                sp.GetRequiredService<IUrlReachabilityChecker>(),
                sp.GetRequiredService<IPageContentProbe>(),
                sp.GetRequiredService<IPricingPageVerifier>(),
                sp.GetRequiredService<IWebsiteOwnershipJudge>(),
                sp.GetRequiredService<IApiEndpointProbe>(),
                sp.GetRequiredService<IManualBrowserVerifier>()));

        // Investigation metadata loaded from local JSON config files
        services.AddSingleton<FileBasedMetadataProvider>();
        services.AddSingleton<IUrlIntelligenceRuleStore>(sp =>
            sp.GetRequiredService<FileBasedMetadataProvider>());
        services.AddSingleton<IFieldResearchProfileStore>(sp =>
            sp.GetRequiredService<FileBasedMetadataProvider>());

        // SOLID: Register refactored validator and its dependencies
        services.AddTransient(sp => new ProviderDefinitionValidator(
            sp.GetRequiredService<IProviderSchemaValidator>(),
            sp.GetRequiredService<IDuplicateIdChecker>(),
            sp.GetRequiredService<IValidationMetadataService>(),
            sp.GetRequiredService<IProviderManifestReader>(),
            sp.GetRequiredService<ISelfHostedApplicabilityEvaluator>(),
            sp.GetRequiredService<ISubscriptionConfiguredChecker>(),
            sp.GetRequiredService<IServiceRetirementProbe>(),
            sp.GetRequiredService<IUrlFieldChecker>()));
        services.AddSingleton<ProviderCatalog>();
        services.AddSingleton<AIProviderFactory>();
        services.AddSingleton<IAIProviderFactory>(sp => sp.GetRequiredService<AIProviderFactory>());
        services.AddSingleton<ITransientCredentialProviderFactory>(sp => sp.GetRequiredService<AIProviderFactory>());
        services.AddSingleton<ProviderJsonPatchService>(sp =>
            new ProviderJsonPatchService(sp.GetRequiredService<IManifestPathResolver>().Resolve()));
        services.AddSingleton<AiDefinitionAnalyzer>(sp =>
            new AiDefinitionAnalyzer(
                sp.GetRequiredService<IAIProviderFactory>(),
                sp.GetRequiredService<AiAnalysisService>(),
                sp.GetRequiredService<AppSettings>(),
                sp.GetRequiredService<IHttpClientFactory>()
                    .CreateClient(HttpConstants.ScraperHttpClientName),
                sp.GetRequiredService<ProviderCatalog>()));
        services.AddSingleton<ModelPriceResolver>();
        services.AddSingleton<IAiFixConfiguration>(sp =>
            new AiFixConfigurationAdapter(sp.GetRequiredService<AiDefinitionAnalyzer>()));
        services.AddSingleton<IProviderCatalog>(sp => sp.GetRequiredService<ProviderCatalog>());
        services.AddSingleton<AiUrlFixService>(sp =>
            new AiUrlFixService(
                sp.GetRequiredService<IAiFixConfiguration>(),
                sp.GetRequiredService<ModelPriceResolver>(),
                sp.GetRequiredService<IValidationIssueRepository>(),
                sp.GetRequiredService<ITokenUsageRepository>(),
                sp.GetRequiredService<IUnitOfWork>(),
                sp.GetRequiredService<IProviderCatalog>(),
                sp.GetRequiredService<IUrlResearchService>(),
                sp.GetRequiredService<IGeoRegionProvider>(),
                sp.GetRequiredService<IWebContentFetcher>(),
                sp.GetRequiredService<ProviderResearchCache>(),
                sp.GetRequiredService<IHttpClientFactory>()
                    .CreateClient(HttpConstants.ScraperHttpClientName),
                sp.GetRequiredService<IPricingPageVerifier>()));
        services.AddSingleton<IssueSyncService>();
        services.AddSingleton<ValidationOrchestrationService>();
        services.AddSingleton<SuggestionProcessingService>();

        // New SOLID services
        services.AddSingleton<IManifestPathResolver, ManifestPathResolver>();
        services.AddSingleton<IClipboardService, WpfClipboardService>();
        services.AddTransient<IOperationTimer, OperationTimer>();
        services.AddTransient<IOperationLogger, OperationLogger>();

        // ScraperConfigBuilder: register concrete builders first, then the composite
        // Use keyed registration to avoid circular dependency
        services.AddSingleton<OpenAiScraperConfigBuilder>();
        services.AddSingleton<DefaultScraperConfigBuilder>();
        services.AddSingleton<ScraperConfigBuilder>(sp =>
            new ScraperConfigBuilder(
                new IScraperConfigBuilder[]
                    {
                        sp.GetRequiredService<OpenAiScraperConfigBuilder>(),
                        sp.GetRequiredService<DefaultScraperConfigBuilder>()
                    }));

        // Register the composite as the default IScraperConfigBuilder
        services.AddSingleton<IScraperConfigBuilder>(sp =>
            sp.GetRequiredService<ScraperConfigBuilder>());

        // URL Research: AI provider analysis
        services.AddSingleton<AiAnalysisService>();
        services.AddSingleton<IWebAccessService, WebAccessService>();

        services.AddSingleton<BrowserEngineSwitch>();
        services.AddSingleton<IWebContentFetcher>(sp =>
            sp.GetRequiredService<BrowserEngineSwitch>());
        services.AddSingleton<IWebSearchProvider>(sp =>
            sp.GetRequiredService<BrowserEngineSwitch>());
        services.AddSingleton<IBrowserInteraction>(sp =>
            sp.GetRequiredService<BrowserEngineSwitch>());

        // AiCleverness runtime + provider-neutral LLM client adapter
        services.AddAiClevernessRuntime(options =>
        {
            if (settings.TranscriptMode != ETranscriptMode.None)
            {
                Directory.CreateDirectory(settings.TranscriptDirectory);
                if (settings.TranscriptMode == ETranscriptMode.Transcript)
                {
                    options.TranscriptRedactor = text =>
                        string.IsNullOrEmpty(settings.ApiKey)
                            ? text
                            : text.Replace(settings.ApiKey, "[API_KEY_REDACTED]");
                }
            }
        });
        services.AddAiClevernessLlmClient<AIProviderLlmClient>();

        // Capability-based model selection with built-in profiles + model catalog
        services.AddCapabilityResolver(BuiltInProfiles.GetAll());
        var modelCatalogMapping = new Dictionary<string, IReadOnlyList<ModelDefinition>>
                                      {
                                          [BuiltInProfiles.PrimaryId] =
                                              new[]
                                                  {
                                                      new ModelDefinition
                                                          {
                                                              Name = settings.PrimaryModel,
                                                              ProviderKey =
                                                                  ExtractProviderKey(
                                                                      settings.PrimaryModel)
                                                          }
                                                  },
                                          [BuiltInProfiles.FallbackId] = new[]
                                              {
                                                  new ModelDefinition
                                                      {
                                                          Name = settings.FallbackModel,
                                                          ProviderKey = ExtractProviderKey(
                                                              settings.FallbackModel)
                                                      }
                                              }
                                      };
        services.AddModelCatalog(modelCatalogMapping);
        services.AddModelResolution();

        // Quality gate: validate AI output is valid JSON before returning
        services.AddAgentQualityGate<JsonQualityGate>();

        // Input validators: data quality checks for DataValidationAgent
        services.AddAgentInputValidator<MinModelsPerProviderValidator>(ctx =>
            ctx.AgentName == nameof(DataValidationAgent));
        services.AddAgentInputValidator<RequiredFieldsValidator>(ctx =>
            ctx.AgentName == nameof(DataValidationAgent));
        services.AddAgentInputValidator<PayAsYouGoPricingValidator>(ctx =>
            ctx.AgentName == nameof(DataValidationAgent));

        // Input validators: pricing URL + currency checks for PricingAgent
        services.AddAgentInputValidator<PricingPageFormatValidator>(ctx =>
            ctx.AgentName == nameof(PricingAgent));
        services.AddAgentInputValidator<CurrencyFormatValidator>(ctx =>
            ctx.AgentName == nameof(PricingAgent));

        // AiCleverness observability and diagnostics
        services.AddStartupAnalyzer();
        services.AddMetricsCollector();
        services.AddDiagnosticCollector();
        services.AddInMemoryEventBus();

        // AiCleverness persistence (opt-in for resumability)
        services.AddInMemoryCheckpointStore();
        services.AddInMemoryExecutionJournal();

        // Core WebTools.NET services (IWebAccessService already registered above)
        services.AddWebToolsCore();

        // Decision-tree execution infrastructure for URL research
        services.AddUrlResearchDecisionTree(settings);

        // URL Fix Engine: provides AI-readable rules/hints for URL research
        services.AddSingleton(sp =>
            {
                var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.Add(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36");
                http.DefaultRequestHeaders.Add(
                    "Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
                http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
                http.DefaultRequestHeaders.Add("Connection", "keep-alive");
                http.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
                return http;
            });
        services.AddSingleton<IDeterministicLinkScanner, DeterministicLinkScanner>();
        services.AddSingleton<IGeoRegionProvider>(sp =>
            {
                var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                http.DefaultRequestHeaders.Add(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                return new GeoRegionService(http, sp.GetService<ILogger<GeoRegionService>>());
            });
        services.AddSingleton<IUrlResearchService>(sp =>
            sp.GetRequiredService<DecisionTreeResearchService>());
        services.AddSingleton<DataValidationAgent>();
        services.AddSingleton<PricingAgent>();
        services.AddSingleton<UrlIntelligenceRules>();
    }

    /// <summary>
    /// Extracts a provider key from a model identifier string.
    /// Convention: provider/model-name → provider; standalone → "unknown".
    /// </summary>
    private static string ExtractProviderKey(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return "unknown";

        var slashIndex = modelName.IndexOf('/');
        return slashIndex > 0 ? modelName[..slashIndex] : "unknown";
    }
}
