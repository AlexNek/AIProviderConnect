using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ScraperTool.Models;
using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.UrlResearch.DecisionTree.Actions;
using ScraperTool.Services.UrlResearch.DecisionTree.Adapters;
using ScraperTool.Services.UrlResearch.DecisionTree.Formatting;
using ScraperTool.Services.UrlResearch.DecisionTree.Predicates;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;
using ScraperTool.Services.UrlResearch.DecisionTree.TemplateResolution;
using ScraperTool.Services.UrlResearch.DecisionTree.Transcript;
using ScraperTool.Visualization;

namespace ScraperTool.Services.UrlResearch.DependencyInjection;

public static class UrlResearchServiceCollectionExtensions
{
    /// <summary>
    /// Registers the decision-tree execution infrastructure for URL research.
    /// Host must also register AiCleverness services via AddAiCleverness().
    /// </summary>
    public static IServiceCollection AddUrlResearchDecisionTree(
        this IServiceCollection services,
        AppSettings settings)
    {
        // Register data quality services used by URL research actions and predicates.
        services.AddSingleton<IHtmlTagCleaner, HtmlTagCleaner>();
        services.AddSingleton<IStringListFormatter, StringListFormatter>();
        services.AddSingleton<ITextSummarizer, TextSummarizer>();
        services.AddSingleton<ICandidateRegionQualityAssessor, CandidateRegionQualityAssessor>();
        services.AddSingleton<ICandidateRegionContentSelector, CandidateRegionContentSelector>();
        services.AddSingleton<IModelCatalogCounter, ModelCatalogCounter>();
        services.AddSingleton<ICandidateUrlProvider, CandidateUrlProvider>();

        // Batch-scoped cache shared across decision trees for the same provider.
        // Cleared at the start of each batch by AiUrlFixService.
        services.AddSingleton<ProviderResearchCache>();

        // Register AiCleverness decision-tree execution infrastructure
        services.AddDecisionTreeExecution(options =>
        {
            if (settings.TranscriptMode != ETranscriptMode.None)
            {
                options.TranscriptDirectory = settings.TranscriptDirectory;
                options.TranscriptDebug = settings.TranscriptMode == ETranscriptMode.DebugTranscript;
                options.TranscriptBuilderFactory = () => new ReadablePathTranscriptDecorator();
                if (settings.TranscriptMode == ETranscriptMode.Transcript)
                {
                    options.TranscriptRedactor = text =>
                        string.IsNullOrEmpty(settings.ApiKey)
                            ? text
                            : text.Replace(settings.ApiKey, "[API_KEY_REDACTED]");
                }
            }
            if (!string.IsNullOrWhiteSpace(settings.PrimaryModel)
                && !string.IsNullOrWhiteSpace(settings.FallbackModel))
            {
                options.EnableModelFailover = true;
                options.Model = settings.PrimaryModel;
                options.ModelFallbackChain = new[] { settings.FallbackModel };
            }
        });

        // Register ScraperTool-specific actions. AiCleverness supplies no registration helper for
        // them: actions are passed explicitly to each DecisionTreeExecutor.ExecuteAsync call, so
        // they are registered as IDecisionAction services here and handed to the executor by
        // DecisionTreeResearchService. All actions are stateless — every execution keeps its own
        // DecisionState and DataStore — so a shared instance per action is safe.
        services.AddSingleton<IDecisionAction, RecordCurrentFactAction>();
        services.AddSingleton<IDecisionAction, ScanProviderLinksAction>();
        services.AddSingleton<IDecisionAction, ScanSiblingContentAction>();
        services.AddSingleton<IDecisionAction, ScanCandidateContentAction>();
        services.AddSingleton<IDecisionAction, FetchNextCandidateAction>();
        services.AddSingleton<IDecisionAction, VerifyReachableAction>();
        services.AddSingleton<IDecisionAction, WebSearchAction>();
        services.AddSingleton<IDecisionAction, QueryModelsEndpointAction>();
        services.AddSingleton<IDecisionAction, QueryDocumentedModelsEndpointAction>();
        services.AddSingleton<IDecisionAction, FetchModelsPageAction>();
        services.AddSingleton<IDecisionAction, FetchDocumentationPageAction>();
        services.AddSingleton<IDecisionAction, CompareModelCountAction>();
        services.AddSingleton<IDecisionAction, LlmExtractModelCountAction>();
        services.AddSingleton<IDecisionAction, InitVerificationStateAction>();

        // Register ScraperTool-specific predicates
        services.AddDecisionPredicate<HasCandidatesPredicate>();
        services.AddDecisionPredicate<HasUntriedCandidatesPredicate>();
        services.AddDecisionPredicate<LastVerifySucceededPredicate>();
        services.AddDecisionPredicate<HttpStatusIs401Predicate>();
        services.AddDecisionPredicate<HasModelDiscoveryApiPredicate>();
        services.AddDecisionPredicate<IsModelCountAccuratePredicate>();
        services.AddDecisionPredicate<HasDocumentedModelCountPredicate>();
        services.AddDecisionPredicate<HasModelsPageUrlPredicate>();
        services.AddDecisionPredicate<HasKnownCurrentValuePredicate>();
        services.AddDecisionPredicate<IsSelfHostedProviderPredicate>();
        services.AddDecisionPredicate<IsDynamicModelCatalogPredicate>();
        services.AddDecisionPredicate<PageConfirmsSubscriptionPredicate>();

        // Register execution-session tracker and progress event adapter
        // Adapter must be singleton so the same instance handles all event types
        // within a session and can track visit counts (retry detection) and errors.
        services.AddSingleton<ExecutionSessionTracker>();
        services.AddSingleton<DecisionTreeProgressAdapter>();
        services.AddSingleton<IExecutionEventHandler<DecisionNodeVisitedBusEvent>>(sp => sp.GetRequiredService<DecisionTreeProgressAdapter>());
        services.AddSingleton<IExecutionEventHandler<DecisionClassificationCompletedBusEvent>>(sp => sp.GetRequiredService<DecisionTreeProgressAdapter>());
        services.AddSingleton<IExecutionEventHandler<DecisionActionCompletedBusEvent>>(sp => sp.GetRequiredService<DecisionTreeProgressAdapter>());

        // Register tree visualization
        services.AddSingleton<DecisionTreeDotExporter>();

        // Register template resolution
        services.AddSingleton<DecisionTreeTemplateResolver>();

        // Replace the default decision data policy with the URL research policy
        // that filters navigation noise out of classify-node prompts.
        services.Replace(ServiceDescriptor.Singleton<IDecisionDataPolicy>(sp =>
            new UrlResearchDecisionDataPolicy(
                sp.GetRequiredService<DecisionTreeExecutionOptions>().DecisionDataPolicy)));

        // Register decision-tree research service
        services.AddSingleton<DecisionTreeResearchService>();

        return services;
    }
}
