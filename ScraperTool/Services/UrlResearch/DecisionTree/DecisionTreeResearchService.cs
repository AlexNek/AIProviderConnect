using ScraperTool.Models;
using System.IO;

using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;
using AiCleverness.Runtime.DecisionTree;

using AIProviderConnect.Models;

using Microsoft.Extensions.Logging;

using ScraperTool.Services.UrlResearch.Abstractions;
using ScraperTool.Services.UrlResearch.DecisionTree.Adapters;
using ScraperTool.Services.UrlResearch.DecisionTree.Quality;
using ScraperTool.Services.UrlResearch.DecisionTree.TemplateResolution;
using ScraperTool.Services.UrlResearch.Models;
using ScraperTool.Services.Validation.Checks;

using DecisionTreeModel = AiCleverness.Models.DecisionTree.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree;

/// <summary>
/// Decision-tree implementation of IUrlResearchService.
/// Loads the appropriate tree JSON from Config/trees/ based on fieldKind,
/// resolves template placeholders, executes the tree via DecisionTreeExecutor,
/// and converts the result to UrlResearchResult.
/// </summary>
public sealed class DecisionTreeResearchService : IUrlResearchService
{
    private readonly IDecisionTreeLoader _treeLoader;

    private readonly DecisionTreeExecutor _executor;

    private readonly IReadOnlyList<IDecisionAction> _actions;

    private readonly ILogger<DecisionTreeResearchService> _logger;

    private readonly string _treesDirectory;

    private readonly DecisionTreeTemplateResolver _templateResolver;

    private readonly ExecutionSessionTracker _sessionTracker;

    private readonly IFieldResearchProfileStore _profileStore;

    public DecisionTreeResearchService(
        DecisionTreeExecutor executor,
        IEnumerable<IDecisionAction> actions,
        IDecisionTreeLoader treeLoader,
        DecisionTreeTemplateResolver templateResolver,
        ILogger<DecisionTreeResearchService> logger,
        ExecutionSessionTracker sessionTracker,
        IFieldResearchProfileStore profileStore)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _actions = (actions ?? throw new ArgumentNullException(nameof(actions))).ToArray();
        _treeLoader = treeLoader ?? throw new ArgumentNullException(nameof(treeLoader));
        _templateResolver = templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sessionTracker = sessionTracker ?? throw new ArgumentNullException(nameof(sessionTracker));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));

        _treesDirectory = Path.Combine(AppContext.BaseDirectory, "Config", "trees");
    }

    public async Task<UrlResearchResult> ResearchAsync(
        ResearchContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var fieldKind = context.Field;
        var treeFilePath = Path.Combine(_treesDirectory, $"{fieldKind}.json");

        if (!File.Exists(treeFilePath))
        {
            _logger.LogWarning(
                "No decision tree found for field kind '{FieldKind}' at '{Path}'. Falling back.",
                fieldKind,
                treeFilePath);

            return new UrlResearchResult(
                null,
                $"No decision tree for field kind '{fieldKind}'.",
                false,
                context.PromptTokens,
                context.CompletionTokens,
                context.Steps.AsReadOnly(),
                []);
        }

        context.Report($"Decision tree: loading {fieldKind}.json");

        // Load and resolve template placeholders
        var rawJson = await File.ReadAllTextAsync(treeFilePath, ct);

        // No models page URL is known — provider definitions don't declare one.
        // The tree will fall through to web search instead of guessing a /models path.

        // Which sibling pages to read, what extra words mark a link as relevant, whether a
        // related page can itself be the answer, and what to search for when the provider's own
        // pages did not answer are per-field decisions. They live in
        // Config/field-definitions.json so no field kind is special-cased in code.
        var profile = await _profileStore.GetProfileAsync(fieldKind, ct);

        var siblingUrls = BuildSiblingUrls(profile, context.Provider, context.Research);

        var templateParameters = DecisionTreeTemplateResolver.BuildParameters(
            providerName: context.Provider.DisplayName,
            providerUrl: context.Research?.Website ?? context.Website ?? string.Empty,
            fieldKind: fieldKind,
            baseUrl: context.Provider.BaseUrl,
            region: context.Region,
            currentValue: context.CurrentValue,
            searchQueryTemplate: profile?.SearchQueryTemplate,
            hasModelDiscoveryApi: context.Provider.HasModelDiscoveryApi,
            providerCategory: context.Provider.Category,
            isDynamicModelCatalog: context.Research?.IsDynamicModelCatalog ?? false,
            siblingUrls: siblingUrls);

        // Only non-default profile values are written, so a field with no profile keeps the
        // behaviour the actions fall back to when the parameter is absent.
        if (profile is not null)
        {
            if (profile.RelevanceTerms.Count > 0)
                templateParameters["relevanceTerms"] = string.Join(";", profile.RelevanceTerms);

            if (!profile.SiblingPageMayBeAnswer)
                templateParameters["siblingPageMayBeAnswer"] = "false";
        }

        // When validation detected a redirect, pass the redirect target URL
        // so the tree can evaluate it as a candidate before scanning the website.
        if (!string.IsNullOrWhiteSpace(context.RedirectTargetUrl))
        {
            templateParameters["redirectTargetUrl"] = context.RedirectTargetUrl;
        }

        // When validation flagged a subdomain website, pass the computed root domain
        // so the tree can evaluate it as the top-priority candidate. Falls back to
        // recomputing from CurrentValue when the property was not carried (e.g. after
        // a database round-trip that does not persist the runtime-only field).
        var rootDomain = context.SuggestedRootDomain;
        if (string.IsNullOrWhiteSpace(rootDomain)
            && context.Issue.Code == ValidationIssueCodes.WebsiteIsSubdomain
            && Uri.TryCreate(context.CurrentValue, UriKind.Absolute, out var websiteUri))
        {
            rootDomain = UrlDomainRules.GetRootDomain(websiteUri);
        }

        if (!string.IsNullOrWhiteSpace(rootDomain))
        {
            templateParameters["suggestedRootDomain"] = rootDomain;
        }

        var resolvedJson = _templateResolver.Resolve(rawJson, templateParameters);

        // Parse the tree model
        DecisionTreeModel tree;
        try
        {
            tree = _treeLoader.Load(resolvedJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load decision tree from '{Path}'", treeFilePath);
            return new UrlResearchResult(
                null,
                $"Failed to load decision tree: {ex.Message}",
                false,
                context.PromptTokens,
                context.CompletionTokens,
                context.Steps.AsReadOnly(),
                []);
        }

        context.Report($"Decision tree: executing {tree.TreeId} v{tree.Version}");

        // Execute the tree, supplying the registered actions explicitly — the executor no longer
        // resolves them from the container.
        _sessionTracker.Begin(tree.TreeId, context.Progress);
        try
        {
            var result = await _executor.ExecuteAsync(_actions, tree, templateParameters, ct);

            // Account token usage
            context.PromptTokens += result.Usage.InputTokens;
            context.CompletionTokens += result.Usage.OutputTokens;

            // Convert DecisionTreeResult to UrlResearchResult
            var suggestedValue = DetermineSuggestedValue(result);

            // A field may legitimately be answered with its "not applicable" marker, but only
            // when the tree actually completed its search (Outcome=Terminal) and found nothing.
            // When the tree ran out of budget (BudgetExhausted), the search was incomplete —
            // suggesting the marker would be a false negative that incorrectly marks the field
            // as absent.
            var notApplicableValue = profile?.NotApplicableValue;
            if (suggestedValue is null
                && !string.IsNullOrEmpty(notApplicableValue)
                && result.Outcome == DecisionTreeOutcome.Terminal
                && result.Verdict is null or "null")
            {
                suggestedValue = notApplicableValue;
            }

            var reason = BuildReason(result);

            // The tree answers with a page it could classify, not with the address that best
            // serves the field: a per-endpoint documentation page that lists token prices reads as
            // "api_pricing" to the classifier, and a console root that renders a sign-in form reads
            // as "login_page". Both won over a stored address that said plainly what it was, so the
            // proposal that reached the user was the worse of the two. The comparison is weak on
            // purpose — it fires only when the winner's own address carries none of the field's
            // vocabulary while the stored address carries some — because what a path says about a
            // page is a hint, never proof, and deciding between two addresses that both say
            // something needs the pages themselves.
            if (result.Verdict == "winner"
                && suggestedValue is not null
                && StoredAddressStillStands(context.Issue.Code)
                && !string.Equals(suggestedValue, context.RedirectTargetUrl, StringComparison.OrdinalIgnoreCase)
                && FieldUrlRelevance.WinnerSaysNothingWhileStoredDoes(
                    suggestedValue,
                    context.CurrentValue,
                    fieldKind,
                    profile?.RelevanceTerms ?? Array.Empty<string>()))
            {
                // Answering with the stored address, rather than with no address at all, is what
                // makes the outcome visible: the caller reads a proposal equal to the current
                // value as "the stored value was confirmed" and dismisses the issue, where an
                // empty result would be read as "this field has no value".
                context.Report(
                    $"  Winner rejected: '{suggestedValue}' says nothing about {fieldKind} while the stored '{context.CurrentValue}' does — keeping the stored value");
                reason = $"{reason}; winner rejected as less relevant than the stored value";
                suggestedValue = context.CurrentValue;
            }

            var steps = BuildSteps(context, result);

            // Success = the research completed successfully (tree ran to completion),
            // regardless of whether a suggestion was produced. This allows the caller
            // to distinguish "confirmed no valid value" (Success=true, no suggestion)
            // from "research failed" (Success=false, e.g. exception, timeout).
            // Control-flow verdicts like "keep" and "skip" mean the tree completed
            // but cannot provide a value — these are successful research outcomes.
            var hasSuggestion = !string.IsNullOrWhiteSpace(suggestedValue);
            var researchSucceeded = result.Outcome == DecisionTreeOutcome.Terminal
                                    || result.Outcome == DecisionTreeOutcome.ActionFailed
                                    || hasSuggestion;

            context.Report($"Decision tree: {result.Outcome} — verdict={result.Verdict ?? "null"}");

            // Report key state properties for diagnostic visibility
            if (result.StateProperties is { Count: > 0 })
            {
                var diagnostics = new List<string>();
                if (result.StateProperties.TryGetValue("verifiedWinnerUrl", out var verifiedObj) && verifiedObj is string verifiedUrl)
                    diagnostics.Add($"verifiedUrl={verifiedUrl}");
                if (result.StateProperties.TryGetValue("lastFetchedUrl", out var urlObj) && urlObj is string fetchedUrl)
                    diagnostics.Add($"url={fetchedUrl}");
                if (result.StateProperties.TryGetValue("modelCount", out var countObj) && countObj is not null)
                    diagnostics.Add($"modelCount={countObj}");
                if (result.StateProperties.TryGetValue("modelCountMethod", out var countMethodObj) && countMethodObj is string countMethod && !string.IsNullOrWhiteSpace(countMethod))
                    diagnostics.Add($"countMethod={countMethod}");
                if (result.StateProperties.TryGetValue("modelCountSourceUrl", out var countUrlObj) && countUrlObj is string countUrl && !string.IsNullOrWhiteSpace(countUrl))
                    diagnostics.Add($"countUrl={countUrl}");
                if (result.StateProperties.TryGetValue("quote", out var quoteObj) && quoteObj is string quote && !string.IsNullOrWhiteSpace(quote))
                    diagnostics.Add($"quote=\"{quote}\"");
                if (result.StateProperties.TryGetValue("candidateRegions", out var regionsObj) && regionsObj is string regions)
                    diagnostics.Add($"regions={regions}");
                if (diagnostics.Count > 0)
                    context.Report($"  State: {string.Join(", ", diagnostics)}");
            }

            return new UrlResearchResult(
                suggestedValue,
                reason,
                researchSucceeded,
                context.PromptTokens,
                context.CompletionTokens,
                steps,
                []);
        }
        catch (OperationCanceledException)
        {
            context.Report("Decision tree: cancelled");
            return new UrlResearchResult(
                null,
                "Decision tree execution was cancelled.",
                false,
                context.PromptTokens,
                context.CompletionTokens,
                context.Steps.AsReadOnly(),
                []);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Decision tree execution failed for '{FieldKind}'", fieldKind);
            context.Report($"Decision tree: failed — {ex.Message}");
            return new UrlResearchResult(
                null,
                $"Decision tree execution failed: {ex.Message}",
                false,
                context.PromptTokens,
                context.CompletionTokens,
                context.Steps.AsReadOnly(),
                []);
        }
        finally
        {
            _sessionTracker.End();
        }
    }

    private static string? DetermineSuggestedValue(DecisionTreeResult result)
    {
        // A tree can produce a valid verdict/classification even if an earlier action
        // failed (e.g. fetch-models-page failed but web-search fallback found the answer).
        // Don't discard results just because Outcome != Terminal.

        if (result.Verdict is null or "null")
            return null;

        if (result.Verdict == "-")
            return "-";

        // For URL discovery trees the verdict is "winner" and the actual URL
        // lives in StateProperties. Prefer "verifiedWinnerUrl" (set by
        // VerifyReachableAction when reachability was confirmed) over
        // "lastFetchedUrl" because the latter can be overwritten by a subsequent
        // fetch-next-candidate call that runs before the tree terminates.
        if (result.Verdict == "winner")
        {
            if (result.StateProperties is { Count: > 0 })
            {
                if (result.StateProperties.TryGetValue("verifiedWinnerUrl", out var verifiedObj)
                    && verifiedObj is string verifiedUrl
                    && verifiedUrl.Length > 0)
                {
                    return verifiedUrl;
                }

                if (result.StateProperties.TryGetValue("lastFetchedUrl", out var urlObj)
                    && urlObj is string url
                    && url.Length > 0)
                {
                    return url;
                }
            }

            return null;
        }

        // For non-URL fields like minModelCount, the verdict may be "skip" but
        // the actual value lives in the last classification answer (e.g. a number).
        if (result.Classifications.Count > 0)
        {
            var lastClassification = result.Classifications[^1];
            if (!string.IsNullOrWhiteSpace(lastClassification.Answer)
                && lastClassification.Answer != "unknown")
            {
                return lastClassification.Answer;
            }
        }

        // Control-flow verdicts that signal "no change needed" or "skipped".
        // These are not values — the actual data (if any) lives in state properties.
        if (result.Verdict is "keep" or "skip")
            return null;

        // "update" verdict: the current value needs updating. The new value lives
        // in state properties (e.g. modelCount for minModelCount trees).
        if (result.Verdict == "update")
        {
            return TryExtractValueFromState(result.StateProperties);
        }

        // Only return the raw verdict for successful terminal outcomes.
        if (result.Succeeded)
            return result.Verdict;

        return null;
    }

    /// <summary>
    /// Whether the validator's finding leaves the stored address standing, which is what makes it
    /// worth comparing a winner against. A stored address that did not answer — not found, not
    /// reachable, timed out, errored, service gone — has nothing to be compared against: any page
    /// that answers is an improvement on it, however little its address says about the field.
    /// </summary>
    private static bool StoredAddressStillStands(string issueCode) => issueCode switch
    {
        ValidationIssueCodes.UrlNotFound => false,
        ValidationIssueCodes.UrlNotReachable => false,
        ValidationIssueCodes.UrlTimeout => false,
        ValidationIssueCodes.UrlError => false,
        ValidationIssueCodes.ServiceRetired => false,
        _ => true
    };

    /// <summary>
    /// Extracts a value from state properties. Handles both int (set directly by actions)
    /// and string (stored by the executor when copying action result properties).
    /// </summary>
    private static string? TryExtractValueFromState(IReadOnlyDictionary<string, object>? stateProperties)
    {
        if (stateProperties is not { Count: > 0 })
            return null;

        // Try modelCount first (for minModelCount trees)
        if (stateProperties.TryGetValue("modelCount", out var countObj) && countObj is not null)
        {
            if (countObj is int intVal)
                return intVal.ToString();
            if (countObj is string strVal && !string.IsNullOrWhiteSpace(strVal))
                return strVal;
        }

        return null;
    }

    internal static string BuildReason(DecisionTreeResult result)
    {
        var outcomeLabel = result.Outcome switch
        {
            DecisionTreeOutcome.Terminal => "completed",
            DecisionTreeOutcome.ActionFailed => "completed with fallback",
            DecisionTreeOutcome.Unknown => "inconclusive",
            DecisionTreeOutcome.BudgetExhausted => "budget exceeded",
            DecisionTreeOutcome.Cancelled => "cancelled",
            DecisionTreeOutcome.ValidationFailed => "validation failed",
            _ => result.Outcome.ToString()
        };

        var parts = new List<string>();
        parts.Add($"Decision tree: {outcomeLabel}");

        if (result.Verdict is not null)
        {
            parts.Add($"verdict={result.Verdict}");
        }

        if (result.Classifications.Count > 0)
        {
            var lastAnswer = result.Classifications[^1].Answer;
            parts.Add($"classification returned {lastAnswer}");
        }

        return string.Join("; ", parts);
    }

    private static List<string> BuildSteps(ResearchContext context, DecisionTreeResult result)
    {
        var steps = new List<string>(context.Steps);

        steps.Add($"Decision tree: {result.ExecutionId}");
        steps.Add($"Outcome: {result.Outcome}");

        if (result.Verdict is not null)
        {
            steps.Add($"Verdict: {result.Verdict}");
        }

        foreach (var classification in result.Classifications)
        {
            steps.Add($"Classification: {classification.Answer} (observation: {classification.Observation ?? "-"})");
        }

        return steps;
    }

    /// <summary>
    /// Resolves the sibling fields declared by the field's profile into the provider's
    /// non-empty, non-sentinel URLs, keeping the configured order — the scan action can use
    /// these as additional link sources instead of relying solely on the homepage.
    /// </summary>
    private static Dictionary<string, string> BuildSiblingUrls(
        FieldResearchProfile? profile,
        ProviderDefinition provider,
        ProviderResearchMetadata? research)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (profile is null)
            return result;

        foreach (var field in profile.SiblingFields)
        {
            var url = ResolveFieldValue(provider, research, field);
            if (!string.IsNullOrWhiteSpace(url) && url != ProviderJsonFields.NotApplicable)
            {
                result[field] = url;
            }
        }

        return result;
    }

    /// <summary>
    /// Looks a field name up in the provider's runtime definition or research metadata. This is a
    /// mapping of declared data, not a policy decision: which fields are worth reading while
    /// researching a given target is stated by the profile in Config/field-definitions.json.
    /// </summary>
    private static string? ResolveFieldValue(
        ProviderDefinition provider,
        ProviderResearchMetadata? research,
        string fieldName) => fieldName switch
    {
        ProviderJsonFields.Website => research?.Website,
        ProviderJsonFields.LoginUrl => research?.LoginUrl,
        ProviderJsonFields.ApiPricingUrl => research?.ApiPricingUrl,
        ProviderJsonFields.SubscriptionPricingUrl => research?.SubscriptionPricingUrl,
        ProviderJsonFields.DocumentationUrl => research?.DocumentationUrl,
        ProviderJsonFields.BaseUrl => provider.BaseUrl,
        _ => null
    };
}
