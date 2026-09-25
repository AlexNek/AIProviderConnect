using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using ScraperTool.Data;
using ScraperTool.Data.Entities;
using ScraperTool.Data.Repositories;
using ScraperTool.Models;
using ScraperTool.Services.UrlResearch;
using ScraperTool.Services.UrlResearch.DecisionTree;
using ScraperTool.Services.Validation;
using ScraperTool.Services.Validation.Checks;

using WebTools.NET.Abstractions;

namespace ScraperTool.Services;

public sealed class AiUrlFixService
{
    private readonly IAiFixConfiguration _config;

    private readonly IProviderCatalog _catalog;

    private readonly IWebContentFetcher _fetcher;

    private readonly HttpClient _httpClient;

    private readonly IValidationIssueRepository _issueRepo;

    private readonly ModelPriceResolver _priceResolver;

    private readonly IGeoRegionProvider _regionProvider;

    private readonly ProviderResearchCache _researchCache;

    private readonly IUrlResearchService _researchAgent;

    private readonly ITokenUsageRepository _tokenRepo;

    private readonly IUnitOfWork _uow;

    private readonly IPricingPageVerifier _pricingVerifier;

    public bool IsAvailable => _config.HasApiKey && _config.HasProviderSelection && _config.IsModelConfigured;

    public AiUrlFixService(
        IAiFixConfiguration config,
        ModelPriceResolver priceResolver,
        IValidationIssueRepository issueRepo,
        ITokenUsageRepository tokenRepo,
        IUnitOfWork uow,
        IProviderCatalog catalog,
        IUrlResearchService researchAgent,
        IGeoRegionProvider regionProvider,
        IWebContentFetcher fetcher,
        ProviderResearchCache researchCache,
        HttpClient httpClient,
        IPricingPageVerifier pricingVerifier)
    {
        _config = config;
        _priceResolver = priceResolver;
        _issueRepo = issueRepo;
        _tokenRepo = tokenRepo;
        _uow = uow;
        _catalog = catalog;
        _researchAgent = researchAgent;
        _regionProvider = regionProvider;
        _fetcher = fetcher;
        _researchCache = researchCache;
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _pricingVerifier = pricingVerifier ?? throw new ArgumentNullException(nameof(pricingVerifier));
    }

    /// <summary>
    /// Runs the full AI URL-fix loop over the supplied issues.
    /// Reports progress via <paramref name="onProgress"/>.
    /// </summary>
    public async Task<AiUrlFixRunResult> RunAsync(
        IReadOnlyList<ValidationIssue> issuesToFix,
        string modelName,
        IProgress<AiUrlFixProgress>? onProgress,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var providers = _catalog.All.ToDictionary(p => p.Id, p => p);
        const int batchSize = 5;
        var totalBatches = (issuesToFix.Count + batchSize - 1) / batchSize;
        var totalSuggestions = 0;
        var totalPromptTokens = 0;
        var totalCompletionTokens = 0;
        var totalCost = 0m;
        var costSourceLabel = string.Empty;
        var allSuggestions = new List<AiSuggestion>();

        if (!_config.HasApiKey || !_config.HasProviderSelection
                                           || !_config.IsModelConfigured)
        {
            var sb = new StringBuilder("AI setup incomplete: missing");

            var count = 0;
            if (!_config.HasApiKey)
            {
                sb.Append(" API key");
                count++;
            }

            if (!_config.HasProviderSelection)
            {
                sb.Append(count++ > 0 ? " and provider selection" : " provider selection");
            }

            if (!_config.IsModelConfigured)
            {
                var noPrimary = string.IsNullOrWhiteSpace(_config.PrimaryModel);
                var noFallback = string.IsNullOrWhiteSpace(_config.FallbackModel);
                var model = noPrimary && noFallback ? "primary and fallback model names"
                            : noPrimary ? "primary model name"
                            : "fallback model name";
                sb.Append(count++ > 0 ? " and " : " ").Append(model);
            }

            if (!_config.HasApiKey && !_config.HasProviderSelection)
                sb.Append(". Open AI Setup and configure the API key and select a provider");
            else if (!_config.HasApiKey)
                sb.Append(". Open AI Setup and configure the API key");
            else if (!_config.HasProviderSelection)
                sb.Append(". Open AI Setup and select a provider");

            if (!_config.IsModelConfigured)
            {
                var noPrimary = string.IsNullOrWhiteSpace(_config.PrimaryModel);
                var noFallback = string.IsNullOrWhiteSpace(_config.FallbackModel);
                sb.Append(
                    noPrimary && noFallback
                        ? ". Open Settings and select models"
                        : ". Open Settings and select a model");
            }

            var msg = sb.ToString();
            Report(onProgress, $"    ✖ {msg}");

            sw.Stop();
            return new AiUrlFixRunResult(0, issuesToFix.Count, 0, 0, 0m, "", "", sw.Elapsed, []);
        }

        Report(
            onProgress,
            $"Processing {issuesToFix.Count} issue(s) in {totalBatches} batch(es)...");

        var providerSessions =
            new Dictionary<string, ResearchSession>(StringComparer.OrdinalIgnoreCase);

        // The repair target is a provider field, not a finding. Several findings can describe one
        // wrong value, and the decision tree is picked by field, so asking it twice returns the
        // same verdict twice — which showed up as two identical suggestions to review. The first
        // finding for a field is researched and every later one is given that same answer.
        var fieldAnswers =
            new Dictionary<(string FileName, string Field), FieldAnswer>();

        var consecutiveAllFailed = 0;

        for (var batchIdx = 0; batchIdx < totalBatches; batchIdx++)
        {
            ct.ThrowIfCancellationRequested();

            var batch = issuesToFix.Skip(batchIdx * batchSize).Take(batchSize).ToList();
            await MarkBatchInDbAsync(batch, null, IssueSuggestionStatus.Pending, DateTime.UtcNow);

            var batchResult = await ProcessBatchAsync(
                                  batch,
                                  batchIdx,
                                  totalBatches,
                                  sw,
                                  providers,
                                  providerSessions,
                                  fieldAnswers,
                                  modelName,
                                  onProgress,
                                  ct);

            // Circuit breaker: if a batch produced ZERO suggestions and ALL items failed,
            // and this has happened multiple batches in a row, stop — every subsequent batch
            // will fail with the same configuration error.
            if (batchResult.Suggestions == 0 && batch.Count == batch.Count(i =>
                    i.SuggestionStatus == (int)IssueSuggestionStatus.Failed))
            {
                consecutiveAllFailed++;
                if (consecutiveAllFailed >= 2)
                {
                    Report(
                        onProgress,
                        $"  ⚠ Stopping early: all items in the last {consecutiveAllFailed} batch(es) failed — "
                        +
                        "likely a configuration or connectivity issue. Fix the problem and retry failed items.");
                    break;
                }
            }
            else
            {
                consecutiveAllFailed = 0;
            }

            totalSuggestions += batchResult.Suggestions;
            allSuggestions.AddRange(batchResult.NewSuggestions);

            var (inPrice, outPrice, source) = await _priceResolver.ResolveAsync(modelName);
            var inputCost = batchResult.PromptTokens * inPrice / 1_000_000m;
            var outputCost = batchResult.CompletionTokens * outPrice / 1_000_000m;
            totalCost += inputCost + outputCost;
            totalPromptTokens += batchResult.PromptTokens;
            totalCompletionTokens += batchResult.CompletionTokens;
            costSourceLabel = source == PriceSource.Confirmed ? "" :
                              source == PriceSource.Estimated ? " (est.)" : "";

            await PersistTokenUsageAsync(
                batchResult.PromptTokens,
                batchResult.CompletionTokens,
                inputCost + outputCost,
                source,
                modelName,
                "AiUrlFix");
        }

        sw.Stop();
        var failedCount =
            issuesToFix.Count(i => i.SuggestionStatus == (int)IssueSuggestionStatus.Failed);

        return new AiUrlFixRunResult(
            totalSuggestions,
            failedCount,
            totalPromptTokens,
            totalCompletionTokens,
            totalCost,
            costSourceLabel,
            modelName,
            sw.Elapsed,
            allSuggestions);
    }

    internal async Task MarkBatchInDbAsync(
        List<ValidationIssue> batch,
        string? value,
        IssueSuggestionStatus status,
        DateTime now)
    {
        var dbUpdates = batch.Select(i => new ValidationIssueEntry
                                              {
                                                  FileName = i.FileName,
                                                  Code = i.Code,
                                                  Message = i.Message,
                                                  SavedAt = now,
                                                  SuggestedValue = value ?? i.SuggestedValue,
                                                  SuggestionReason = i.SuggestionReason,
                                                  SuggestionSeverity = i.SuggestionSeverity,
                                                  SuggestionStatus = status,
                                                  AiAttemptedAt = now
                                              }).ToList();

        foreach (var issue in batch)
        {
            issue.SuggestionStatus = (int)status;
            issue.AiAttemptedAt = now;
        }

        await _issueRepo.UpdateSuggestionsAsync(dbUpdates);
        await _uow.SaveChangesAsync();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private sealed record BatchInternalResult(
        int Suggestions,
        int PromptTokens,
        int CompletionTokens,
        List<AiSuggestion> NewSuggestions);

    /// <summary>
    /// An answer this run already produced for one provider field, held so that further findings
    /// about the same field are recorded against it instead of asking the same tree again and
    /// presenting the reviewer the same suggestion twice. Only real answers are held: a research
    /// run that failed or timed out is not a verdict, so another finding for that field still gets
    /// its own attempt.
    /// </summary>
    private sealed record FieldAnswer(
        string? SuggestedValue,
        string? Reason,
        IssueSuggestionStatus Status);

    private static int CountEffectiveSuggestions(IReadOnlyList<AiSuggestion> suggestions)
    {
        var count = 0;
        foreach (var s in suggestions)
        {
            if (!string.Equals(
                    s.Field,
                    ProviderJsonFields.RegionalEndpoints,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            if (s.SuggestedValue is null)
            {
                count++;
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(s.SuggestedValue);
                count += doc.RootElement.ValueKind == JsonValueKind.Object
                             ? doc.RootElement.EnumerateObject().Count()
                             : 1;
            }
            catch (Exception)
            {
                // Not valid JSON — count as a single field change.
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Extracts the root domain from a URL (e.g., "openai.com" from "https://platform.openai.com").
    /// </summary>
    private static string? ExtractRootDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host;
        var parts = host.Split('.');

        if (parts.Length < 2)
            return host;

        // Handle common multi-part TLDs (e.g., .co.uk, .com.au)
        if (parts.Length >= 3 && parts[^2].Length <= 3 && parts[^1].Length <= 2)
            return string.Join(".", parts[^3..]);

        return string.Join(".", parts[^2..]);
    }

    private static string FormatElapsed(TimeSpan ts)
    {
        return ts.TotalSeconds < 1
                   ? $"{ts.TotalMilliseconds:F0}ms"
                   : $"{ts.Minutes}m {ts.Seconds}s";
    }

    /// <summary>
    /// Validates that a suggested URL's domain is related to the provider by ID or known domains.
    /// When <paramref name="redirectTargetUrl"/> is set, its domain is treated as known-related —
    /// the provider's own URL redirects there.
    /// </summary>
    private static bool IsDomainRelatedToSuggestion(
        string suggestedUrl,
        string providerId,
        ProviderDefinition provider,
        string? website,
        string? redirectTargetUrl)
    {
        if (!Uri.TryCreate(suggestedUrl, UriKind.Absolute, out var suggestedUri))
            return false;

        var suggestedHost = suggestedUri.Host.ToLowerInvariant();
        var suggestedLabels = suggestedHost.Split('.');
        var suggestedSld = suggestedLabels.Length >= 2 ? suggestedLabels[^2] : suggestedHost;

        // Known provider domains to check against
        var knownDomains = new List<string>();

        if (!string.IsNullOrWhiteSpace(website)
            && Uri.TryCreate(website, UriKind.Absolute, out var webUri))
            knownDomains.Add(webUri.Host.ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(provider.BaseUrl)
            && Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var baseUri))
            knownDomains.Add(baseUri.Host.ToLowerInvariant());

        // When the provider's URL redirects to a different domain, that domain is related.
        if (!string.IsNullOrWhiteSpace(redirectTargetUrl)
            && Uri.TryCreate(redirectTargetUrl, UriKind.Absolute, out var redirectUri))
            knownDomains.Add(redirectUri.Host.ToLowerInvariant());

        // Check against known domains
        foreach (var known in knownDomains)
        {
            if (suggestedHost == known) return true;
            if (suggestedHost.EndsWith("." + known) || known.EndsWith("." + suggestedHost))
                return true;

            var knownStripped = known.StartsWith("www.") ? known[4..] : known;
            var suggestedStripped =
                suggestedHost.StartsWith("www.") ? suggestedHost[4..] : suggestedHost;
            if (knownStripped == suggestedStripped) return true;
            if (suggestedStripped.EndsWith("." + knownStripped)
                || knownStripped.EndsWith("." + suggestedStripped)) return true;
        }

        // Provider ID / display name match against SLD or any label
        var normalizedId = providerId.ToLowerInvariant();
        var normalizedName = provider.DisplayName?.ToLowerInvariant()
                                 .Replace(" ", "").Replace("-", "").Replace("_", "") ?? "";

        if (suggestedSld == normalizedId) return true;
        if (!string.IsNullOrWhiteSpace(normalizedName) && suggestedSld == normalizedName)
            return true;
        if (suggestedLabels.Any(l => l == normalizedId)) return true;
        if (!string.IsNullOrWhiteSpace(normalizedName)
            && suggestedLabels.Any(l => l == normalizedName)) return true;

        return false;
    }

    /// <summary>
    /// Determines whether the current URL is semantically correct for its field,
    /// even though the validation check failed due to an environmental issue (auth, timeout, etc.).
    /// Prevents the AI from replacing a correct URL with a wrong alternative.
    /// </summary>
    private static bool IsSemanticallyCorrect(
        string field,
        string issueCode,
        string currentValue,
        ProviderDefinition? provider,
        string? website)
    {
        if (string.IsNullOrWhiteSpace(currentValue) || provider is null)
            return false;

        if (!Uri.TryCreate(currentValue, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host.ToLowerInvariant();

        // loginUrl with 401/403: authentication is expected, but the URL still needs
        // AI verification — the domain could be completely wrong (e.g. google.com/login
        // for Perplexity). The AI research prompt includes the field definition hint that
        // "authentication required is normal" so it won't wrongly replace a correct URL.

        // apiPricingUrl/subscriptionPricingUrl with timeout or auth error where the URL path contains 'pricing'
        // on the provider's root domain: the URL is the correct pricing page,
        // the error is environmental.
        if ((string.Equals(
                 field,
                 ProviderJsonFields.ApiPricingUrl,
                 StringComparison.OrdinalIgnoreCase)
             || string.Equals(
                 field,
                 ProviderJsonFields.SubscriptionPricingUrl,
                 StringComparison.OrdinalIgnoreCase))
            && (string.Equals(
                    issueCode,
                    ValidationIssueCodes.UrlTimeout,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    issueCode,
                    ValidationIssueCodes.UrlRequiresAuth,
                    StringComparison.OrdinalIgnoreCase)))
        {
            // Check that the URL is on the provider's own domain and path suggests pricing
            var providerDomain = ExtractRootDomain(website ?? provider.Id);
            var isOnProviderDomain = string.IsNullOrWhiteSpace(providerDomain)
                                     || host == providerDomain.ToLowerInvariant()
                                     || host.EndsWith("." + providerDomain.ToLowerInvariant());

            if (isOnProviderDomain && uri.AbsolutePath.Contains(
                    "pricing",
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether a suggested URL's path is structurally incompatible with the field kind.
    /// A pricing URL should never contain /blog/, /news/, /video/, etc. — these paths
    /// indicate content pages, not pricing pages. A baseUrl should be an API endpoint
    /// (empty, /v1, /api, etc.) — not a content page like /pricing or /docs.
    /// This is a safety net that catches LLM misclassification even when the domain is correct.
    /// An absent field is judged compatible: there is no field kind for the path to contradict.
    /// </summary>
    internal static bool IsPathCompatibleWithField(string suggestedUrl, string? field)
    {
        if (!Uri.TryCreate(suggestedUrl, UriKind.Absolute, out var uri))
            return false;

        var path = uri.AbsolutePath.ToLowerInvariant();

        // Pricing URLs — reject content page paths.
        if (string.Equals(field, ProviderJsonFields.ApiPricingUrl, StringComparison.OrdinalIgnoreCase)
            || string.Equals(field, ProviderJsonFields.SubscriptionPricingUrl, StringComparison.OrdinalIgnoreCase))
        {
            // These path segments indicate content pages, not pricing pages.
            if (ContainsPathSegment(path, "blog")
                || ContainsPathSegment(path, "news")
                || ContainsPathSegment(path, "article")
                || ContainsPathSegment(path, "articles")
                || ContainsPathSegment(path, "video")
                || ContainsPathSegment(path, "image")
                || ContainsPathSegment(path, "images")
                || ContainsPathSegment(path, "press")
                || ContainsPathSegment(path, "careers")
                || ContainsPathSegment(path, "about"))
            {
                return false;
            }
        }

        // Base URL — must look like an API endpoint, not a content page.
        // Valid paths: empty, /, /v1, /v2, /api, /api/v1, etc.
        // Invalid: /pricing, /docs, /blog, /login, /models, /about, etc.
        if (string.Equals(field, ProviderJsonFields.BaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsPathSegment(path, "pricing")
                || ContainsPathSegment(path, "blog")
                || ContainsPathSegment(path, "docs")
                || ContainsPathSegment(path, "documentation")
                || ContainsPathSegment(path, "login")
                || ContainsPathSegment(path, "signin")
                || ContainsPathSegment(path, "models")
                || ContainsPathSegment(path, "about")
                || ContainsPathSegment(path, "playground")
                || ContainsPathSegment(path, "dashboard"))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns true if the URL path contains the given segment
    /// (bounded by / or at the end of the path).
    /// </summary>
    private static bool ContainsPathSegment(string path, string segment)
    {
        var delimited = "/" + segment + "/";
        var endsWith = "/" + segment;
        return path.Contains(delimited, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that the suggested base URL is an actual API endpoint rather than a web page.
    /// Fetches the URL directly and checks the response:
    /// - HTML content with a 2xx status → web page → reject.
    /// - JSON or non-HTML content → API endpoint → accept.
    /// - Error status (401, 403, 404, etc.) → trust the research tree → accept.
    /// - Network failure → trust the research tree → accept.
    /// </summary>
    private async Task<bool> ProbeBaseUrlIsApiAsync(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl);
            // Override the scraper client's Accept: text/html default —
            // an API endpoint should receive Accept: application/json.
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead);

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

            // Non-HTML response (JSON, plain text, etc.) → consistent with an API.
            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return true;

            // HTML response with 2xx → this is a web page, not an API endpoint.
            if (response.IsSuccessStatusCode)
                return false;

            // HTML response with error status (401, 403, 404, etc.) → the server
            // returned an HTML error page, but the base URL might still be an API.
            // Trust the research tree's verdict.
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Network failure — trust the research tree's verdict rather than
            // rejecting a potentially valid baseUrl due to transient network issues.
            return true;
        }
    }

    private async Task PersistTokenUsageAsync(
        int promptTokens,
        int completionTokens,
        decimal cost,
        PriceSource source,
        string modelName,
        string operation)
    {
        var providerId = _config.SelectedProviderId;
        if (string.IsNullOrEmpty(providerId))
            return;

        var providerDef = _catalog.Get(providerId);
        var providerName = providerDef?.DisplayName ?? providerId;

        var entry = new TokenUsageEntry
                        {
                            ModelName = modelName,
                            ProviderName = providerName,
                            Operation = operation,
                            PromptTokens = promptTokens,
                            CompletionTokens = completionTokens,
                            Cost = cost,
                            CostSource = source.ToString(),
                            CreatedAt = DateTime.UtcNow
                        };

        await _tokenRepo.AddAsync(entry);
        await _uow.SaveChangesAsync();
    }

    private async Task<BatchInternalResult> ProcessBatchAsync(
        List<ValidationIssue> batch,
        int batchIdx,
        int totalBatches,
        Stopwatch sw,
        Dictionary<string, ProviderDefinition> providers,
        Dictionary<string, ResearchSession> providerSessions,
        Dictionary<(string FileName, string Field), FieldAnswer> fieldAnswers,
        string modelName,
        IProgress<AiUrlFixProgress>? onProgress,
        CancellationToken ct)
    {
        Report(
            onProgress,
            $"  Batch {batchIdx + 1}/{totalBatches} ({batch.Count} URLs) — starting research...");
        Report(
            onProgress,
            $"  Batch {batchIdx + 1}: {string.Join(", ", batch.Select(DescribeIssueField))}");

        var now = DateTime.UtcNow;
        var newSuggestions = new List<AiSuggestion>();
        var dbUpdates = new List<ValidationIssueEntry>();
        var totalPrompt = 0;
        var totalCompletion = 0;
        var failedCount = 0;
        var userRegion = await _regionProvider.DetectRegionAsync(ct);

        // Clear the shared research cache so each batch starts with fresh data.
        _researchCache.Clear();

        // Providers flagged as retired: once a ServiceRetired finding is encountered for a
        // provider, all remaining issues for that provider in this batch are silently skipped.
        var retiredProviderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var issue in batch)
        {
            ct.ThrowIfCancellationRequested();

            var providerId = Path.GetFileNameWithoutExtension(issue.FileName);
            providers.TryGetValue(providerId, out var provider);
            var research = _catalog.GetResearchMetadata(providerId);
            var issueField = issue.Field;

            // A previous run's verdict must not describe this one's summary wording.
            issue.ResearchCompletedWithoutValue = false;

            Report(
                onProgress,
                $"    Researching {issue.FileName} ({issueField})...");

            if (string.IsNullOrEmpty(issueField))
            {
                Report(
                    onProgress,
                    $"    \u2716 {issue.FileName}: unknown field in issue — skipping: {issue.Message}");
                failedCount++;
                continue;
            }

            if (provider is null)
            {
                Report(
                    onProgress,
                    $"    \u2716 {issue.FileName}: provider definition not found — skipping");
                failedCount++;
                continue;
            }

            // A previous ServiceRetired finding for this provider already reported the error;
            // skip remaining issues for the same provider without creating new errors.
            if (retiredProviderIds.Contains(providerId))
                continue;

            if (!providerSessions.TryGetValue(providerId, out var session))
            {
                session = new ResearchSession();
                providerSessions[providerId] = session;
            }

            var context = new ResearchContext
                              {
                                  ProviderId = providerId,
                                  Provider = provider,
                                  Issue = issue,
                                  Field = issueField,
                                  CurrentValue = issue.CurrentValue ?? "",
                                  RedirectTargetUrl = issue.RedirectTargetUrl,
                                  SuggestedRootDomain = issue.SuggestedRootDomain,
                                  Website = research?.Website,
                                  Research = research,
                                  Session = session,
                                  Region = userRegion,
                                  Progress = onProgress is not null
                                                 ? new Progress<string>(m =>
                                                     {
                                                         var app = System.Windows.Application
                                                             .Current;
                                                         app?.Dispatcher.Invoke(() =>
                                                             onProgress.Report(
                                                                 new AiUrlFixProgress(m)));
                                                     })
                                                 : null,
                                  ModelName = modelName
                              };

            // Semantic pre-check: if the current URL is semantically correct for its field
            // and the error is environmental (auth, timeout), dismiss without AI research.
            if (IsSemanticallyCorrect(issueField, issue.Code, context.CurrentValue, provider, research?.Website))
            {
                Report(
                    onProgress,
                    $"    \u2713 {issue.FileName}: current {issueField} is semantically correct — environmental issue only, no change needed");
                issue.SuggestionReason =
                    "Current URL is semantically correct; error is environmental";
                issue.SuggestionSeverity = "urlFix";
                issue.SuggestionStatus = (int)IssueSuggestionStatus.Dismissed;
                issue.AiAttemptedAt = now;

                dbUpdates.Add(
                    new ValidationIssueEntry
                        {
                            FileName = issue.FileName,
                            Code = issue.Code,
                            Message = issue.Message,
                            SavedAt = now,
                            Field = issueField,
                            CurrentValue = context.CurrentValue,
                            SuggestedValue = null,
                            SuggestionReason =
                                "Current URL is semantically correct; error is environmental",
                            SuggestionSeverity = "urlFix",
                            SuggestionStatus = IssueSuggestionStatus.Dismissed,
                            AiAttemptedAt = now
                        });
                continue;
            }

            // ServiceRetired: the validator detected that the page content announces the service
            // has been retired/discontinued. Record the error and stop — no suggestion, no research,
            // and no further issues for this provider in this batch.
            if (string.Equals(issue.Code, ValidationIssueCodes.ServiceRetired, StringComparison.OrdinalIgnoreCase))
            {
                Report(
                    onProgress,
                    $"    \u2716 {issue.FileName}: service retired — {issueField} cannot be repaired");
                issue.SuggestionReason = "Service retired/deprecated";
                issue.SuggestionSeverity = "urlFix";
                issue.SuggestionStatus = (int)IssueSuggestionStatus.Failed;
                issue.AiAttemptedAt = now;
                failedCount++;

                retiredProviderIds.Add(providerId);

                dbUpdates.Add(
                    new ValidationIssueEntry
                    {
                        FileName = issue.FileName,
                        Code = issue.Code,
                        Message = issue.Message,
                        SavedAt = now,
                        Field = issueField,
                        CurrentValue = issue.CurrentValue ?? "",
                        SuggestedValue = null,
                        SuggestionReason = "Service retired/deprecated",
                        SuggestionSeverity = "urlFix",
                        SuggestionStatus = IssueSuggestionStatus.Failed,
                        AiAttemptedAt = now
                    });

                continue;
            }

            // A field has one repair value, so it gets one answer per run. Anything that already
            // settled this field is recorded here rather than researched again, which keeps the
            // review list to a single suggestion even when several findings point at one field.
            if (fieldAnswers.TryGetValue((issue.FileName, issueField), out var earlier))
            {
                issue.SuggestedValue = earlier.SuggestedValue;
                issue.SuggestionReason = earlier.Reason;
                issue.SuggestionSeverity = "urlFix";
                issue.SuggestionStatus = (int)earlier.Status;
                issue.AiAttemptedAt = now;

                Report(
                    onProgress,
                    earlier.Status == IssueSuggestionStatus.Pending
                        ? $"    \u2713 {issue.FileName}: {issueField} was answered earlier in this run — one suggestion per field: {earlier.SuggestedValue}"
                        : $"    \u2713 {issue.FileName}: {issueField} was answered earlier in this run — no change needed");

                dbUpdates.Add(
                    new ValidationIssueEntry
                        {
                            FileName = issue.FileName,
                            Code = issue.Code,
                            Message = issue.Message,
                            SavedAt = now,
                            Field = issueField,
                            CurrentValue = context.CurrentValue,
                            SuggestedValue = earlier.SuggestedValue,
                            SuggestionReason = earlier.Reason,
                            SuggestionSeverity = "urlFix",
                            SuggestionStatus = earlier.Status,
                            AiAttemptedAt = now
                        });

                continue;
            }

            UrlResearchResult result;
            try
            {
                result = await _researchAgent.ResearchAsync(context, ct);
            }
            catch (OperationCanceledException ex)
            {
                var reason = string.IsNullOrEmpty(ex.Message) ? "user cancelled" : ex.Message;
                Report(onProgress, $"    \u2716 {issue.FileName}: {reason}");
                failedCount++;
                continue;
            }
            catch (Exception ex)
            {
                Report(
                    onProgress,
                    $"    \u2716 {issue.FileName}: research failed — {ex.GetType().Name}: {ex.Message}");
                failedCount++;
                continue;
            }

            totalPrompt += result.TotalPromptTokens;
            totalCompletion += result.TotalCompletionTokens;

            if (!result.Success || string.IsNullOrWhiteSpace(result.SuggestedValue))
            {
                // Distinguish "no valid value" (research succeeded and confirmed the field
                // cannot be satisfied) from actual failures (timeouts, rate limits, research broke).
                var isNotApplicable = result.Success
                                      && string.IsNullOrWhiteSpace(result.SuggestedValue);

                if (isNotApplicable)
                {
                    // Numeric fields (e.g. minModelCount) require a JSON number — the "-"
                    // marker is only valid for string fields (URLs). Suggesting "-" for a
                    // numeric field would create a suggest→apply→revalidate loop because
                    // the validator rejects non-numeric values.
                    var isNumericField = string.Equals(
                        issueField, ProviderJsonFields.MinModelCount,
                        StringComparison.OrdinalIgnoreCase);

                    if (isNumericField)
                    {
                        Report(
                            onProgress,
                            $"    — {issue.FileName}: could not determine numeric value ({result.Reason})");
                        issue.SuggestedValue = null;
                        issue.SuggestionReason = result.Reason;
                        issue.SuggestionSeverity = "urlFix";
                        issue.SuggestionStatus = (int)IssueSuggestionStatus.Failed;

                        // The tree ran to a conclusion and that conclusion was "no number
                        // found" — recorded as a failure because nothing can be applied, but
                        // reported as an answer rather than as a breakdown.
                        issue.ResearchCompletedWithoutValue = true;

                        issue.AiAttemptedAt = now;
                        failedCount++;

                        dbUpdates.Add(
                            new ValidationIssueEntry
                                {
                                    FileName = issue.FileName,
                                    Code = issue.Code,
                                    Message = issue.Message,
                                    SavedAt = now,
                                    Field = issueField,
                                    CurrentValue = issue.CurrentValue ?? "",
                                    SuggestedValue = null,
                                    SuggestionReason = result.Reason,
                                    SuggestionSeverity = "urlFix",
                                    SuggestionStatus = IssueSuggestionStatus.Failed,
                                    AiAttemptedAt = now
                                });

                        continue;
                    }

                    // No valid value confirmed — suggest the "-" marker so the user can approve it.
                    var naValue = ProviderJsonFields.NotApplicable;
                    Report(
                        onProgress,
                        $"    — {issue.FileName}: no valid value — suggesting '{naValue}' ({result.Reason})");
                    issue.SuggestedValue = naValue;
                    issue.SuggestionReason = result.Reason;
                    issue.SuggestionSeverity = "urlFix";
                    issue.SuggestionStatus = (int)IssueSuggestionStatus.Pending;
                    issue.AiAttemptedAt = now;

                    newSuggestions.Add(
                        new AiSuggestion
                            {
                                ProviderId = providerId,
                                DisplayName = issue.FileName,
                                Field = issueField,
                                CurrentValue = issue.CurrentValue ?? "",
                                SuggestedValue = naValue,
                                Reason = result.Reason ?? "Research confirmed no valid value for this field",
                                Severity = "urlFix"
                            });

                    fieldAnswers[(issue.FileName, issueField)] =
                        new FieldAnswer(naValue, result.Reason, IssueSuggestionStatus.Pending);

                    dbUpdates.Add(
                        new ValidationIssueEntry
                            {
                                FileName = issue.FileName,
                                Code = issue.Code,
                                Message = issue.Message,
                                SavedAt = now,
                                Field = issueField,
                                CurrentValue = issue.CurrentValue ?? "",
                                SuggestedValue = naValue,
                                SuggestionReason = result.Reason,
                                SuggestionSeverity = "urlFix",
                                SuggestionStatus = IssueSuggestionStatus.Pending,
                                AiAttemptedAt = now
                            });
                }
                else
                {
                    Report(
                        onProgress,
                        $"    \u2716 {issue.FileName}: no valid suggestion ({result.Reason})");
                    issue.SuggestedValue = null;
                    issue.SuggestionReason = result.Reason;
                    issue.SuggestionSeverity = "urlFix";
                    issue.SuggestionStatus = (int)IssueSuggestionStatus.Failed;
                    issue.AiAttemptedAt = now;
                    failedCount++;

                    dbUpdates.Add(
                        new ValidationIssueEntry
                            {
                                FileName = issue.FileName,
                                Code = issue.Code,
                                Message = issue.Message,
                                SavedAt = now,
                                Field = issueField,
                                CurrentValue = issue.CurrentValue ?? "",
                                SuggestedValue = null,
                                SuggestionReason = result.Reason,
                                SuggestionSeverity = "urlFix",
                                SuggestionStatus = IssueSuggestionStatus.Failed,
                                AiAttemptedAt = now
                            });
                }

                continue;
            }

            // If research confirmed the current URL is actually valid (e.g. sub-path test passed),
            // don't generate a suggestion — the issue should be dismissed instead.
            var currentValue = issue.CurrentValue ?? "";
            if (string.Equals(
                    result.SuggestedValue.TrimEnd('/'),
                    currentValue.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                Report(
                    onProgress,
                    $"    \u2713 {issue.FileName}: current URL confirmed valid — no change needed");
                issue.SuggestionReason = result.Reason;
                issue.SuggestionSeverity = "urlFix";
                issue.SuggestionStatus = (int)IssueSuggestionStatus.Dismissed;
                issue.AiAttemptedAt = now;

                fieldAnswers[(issue.FileName, issueField)] =
                    new FieldAnswer(null, result.Reason, IssueSuggestionStatus.Dismissed);

                dbUpdates.Add(
                    new ValidationIssueEntry
                        {
                            FileName = issue.FileName,
                            Code = issue.Code,
                            Message = issue.Message,
                            SavedAt = now,
                            Field = issueField,
                            CurrentValue = currentValue,
                            SuggestedValue = null,
                            SuggestionReason = result.Reason,
                            SuggestionSeverity = "urlFix",
                            SuggestionStatus = IssueSuggestionStatus.Dismissed,
                            AiAttemptedAt = now
                        });

                // Still add regional endpoint suggestions if found
                if (result.Suggestions is { Count: > 0 })
                {
                    newSuggestions.AddRange(result.Suggestions);
                    var regionalCount = CountEffectiveSuggestions(result.Suggestions);
                    Report(
                        onProgress,
                        $"    \u2713 {issue.FileName}: {regionalCount} regional endpoint suggestion(s)");
                }

                continue;
            }

            var isVerified = await VerifySuggestionAsync(issue, result, onProgress, ct);

            var status = isVerified ? IssueSuggestionStatus.Pending : IssueSuggestionStatus.Failed;

            if (isVerified)
            {
                newSuggestions.Add(
                    new AiSuggestion
                        {
                            ProviderId = providerId,
                            DisplayName = issue.FileName,
                            Field = issueField,
                            CurrentValue = context.CurrentValue,
                            SuggestedValue = result.SuggestedValue,
                            Reason = result.Reason ?? "",
                            Severity = "urlFix"
                        });
                Report(
                    onProgress,
                    $"    \u2713 {issue.FileName}: {result.SuggestedValue} (suggested)");

                fieldAnswers[(issue.FileName, issueField)] =
                    new FieldAnswer(
                        result.SuggestedValue,
                        result.Reason,
                        IssueSuggestionStatus.Pending);
            }

            // Add regional endpoint suggestions from research
            if (result.Suggestions is { Count: > 0 })
            {
                newSuggestions.AddRange(result.Suggestions);
                var regionalCount = CountEffectiveSuggestions(result.Suggestions);
                Report(
                    onProgress,
                    $"    \u2713 {issue.FileName}: {regionalCount} regional endpoint suggestion(s)");
            }
            else if (!isVerified)
            {
                Report(
                    onProgress,
                    $"    \u2716 {issue.FileName}: suggestion '{result.SuggestedValue}' not reachable — stored as failed");
            }

            issue.SuggestedValue = result.SuggestedValue;
            issue.SuggestionReason = result.Reason;
            issue.SuggestionSeverity = "urlFix";
            issue.SuggestionStatus = (int)status;
            issue.AiAttemptedAt = now;

            dbUpdates.Add(
                new ValidationIssueEntry
                    {
                        FileName = issue.FileName,
                        Code = issue.Code,
                        Message = issue.Message,
                        SavedAt = now,
                        Field = issueField,
                        CurrentValue = context.CurrentValue,
                        SuggestedValue = result.SuggestedValue,
                        SuggestionReason = result.Reason,
                        SuggestionSeverity = "urlFix",
                        SuggestionStatus = status,
                        AiAttemptedAt = now
                    });
        }

        if (dbUpdates.Count > 0)
        {
            await _issueRepo.UpdateSuggestionsAsync(dbUpdates);
            await _uow.SaveChangesAsync();
        }

        var dismissedCount = batch.Count(i =>
            i.SuggestionStatus == (int)IssueSuggestionStatus.Dismissed
            && i.AiAttemptedAt == now);

        var batchSummary = $"  \u2713 Batch {batchIdx + 1}: {newSuggestions.Count} suggestion(s) found";
        if (dismissedCount > 0)
            batchSummary += $", {dismissedCount} no valid value";
        if (failedCount > 0)
            batchSummary += $", {failedCount} failed";
        batchSummary += $" ({FormatElapsed(sw.Elapsed)})";

        Report(onProgress, batchSummary);

        return new BatchInternalResult(
            newSuggestions.Count,
            totalPrompt,
            totalCompletion,
            newSuggestions);
    }

    private static void Report(IProgress<AiUrlFixProgress>? progress, string message) =>
        progress?.Report(new AiUrlFixProgress(message));

    /// <summary>
    /// Names the field an issue concerns. One provider file can carry several issues, and a
    /// batch header that repeats only the file name reads as the same item listed twice.
    /// </summary>
    private static string DescribeIssueField(ValidationIssue issue)
        => string.IsNullOrWhiteSpace(issue.Field) ? issue.FileName : $"{issue.FileName} ({issue.Field})";

    private async Task<bool> VerifySuggestionAsync(
        ValidationIssue issue,
        UrlResearchResult result,
        IProgress<AiUrlFixProgress>? onProgress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(result.SuggestedValue))
            return false;

        var field = issue.Field;

        // The NotApplicable sentinel "-" is a valid suggestion for URL fields
        // that support it (pricing URLs, loginUrl on self-hosted providers).
        // Accept it directly — domain/reachability checks do not apply.
        if (result.SuggestedValue == ProviderJsonFields.NotApplicable)
            return true;

        // Non-URL fields (e.g. minModelCount) produce numeric or string values, not URLs.
        // Domain similarity and reachability checks only apply to URL fields.
        if (IsUrlField(field))
        {
            // Domain similarity guard: reject suggestions on completely unrelated domains.
            // This is a safety net in case the research agent's check was bypassed.
            var providerId = Path.GetFileNameWithoutExtension(issue.FileName);
            var provider = _catalog.Get(providerId);
            var website = _catalog.GetResearchMetadata(providerId)?.Website;
            if (provider is not null && !IsDomainRelatedToSuggestion(
                    result.SuggestedValue,
                    providerId,
                    provider,
                    website,
                    issue.RedirectTargetUrl))
            {
                Report(
                    onProgress,
                    $"    ✖ {issue.FileName}: suggested domain has no relationship to provider '{providerId}' — rejecting: {result.SuggestedValue}");
                return false;
            }

            // Path compatibility guard: reject suggestions whose URL path is structurally
            // wrong for the field (e.g. /blog/ for a pricing URL). This catches LLM
            // misclassification even when the domain is correct.
            if (!IsPathCompatibleWithField(result.SuggestedValue, field))
            {
                Report(
                    onProgress,
                    $"    ✖ {issue.FileName}: suggested URL path is structurally incompatible with '{field}' — rejecting: {result.SuggestedValue}");
                return false;
            }

            // Pricing content verification: for pricing URL fields, the page must actually
            // contain pricing content (amounts, per-token rates, etc.). The decision tree's
            // classifier can make false positives (e.g. /sandbox cost estimator classified as
            // API pricing), so we run the same deterministic content check that flagged the
            // original URL as invalid. If the suggestion also fails, reject it.
            if (field is not null
                && (string.Equals(field, ProviderJsonFields.ApiPricingUrl, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(field, ProviderJsonFields.SubscriptionPricingUrl, StringComparison.OrdinalIgnoreCase)))
            {
                if (Uri.TryCreate(result.SuggestedValue, UriKind.Absolute, out var suggestionUri))
                {
                    var (verdict, reason) = await _pricingVerifier.VerifyPricingUrlAsync(
                        suggestionUri,
                        result.SuggestedValue,
                        issue.FileName,
                        field,
                        sink: new NullValidationIssueSink(),
                        ct: ct);

                    if (verdict != EPricingContentVerdict.HasPricing)
                    {
                        Report(
                            onProgress,
                            $"    ✖ {issue.FileName}: suggested {field} contains no pricing content — {reason}: {result.SuggestedValue}");
                        return false;
                    }
                }
            }

            // baseUrl fields: verify the URL is an actual API endpoint by probing
            // {baseUrl}/v1/models. If the probe returns JSON, it's an API. If HTML,
            // it's a web page — reject.
            if (string.Equals(
                    field,
                    ProviderJsonFields.BaseUrl,
                    StringComparison.OrdinalIgnoreCase))
            {
                var isApi = await ProbeBaseUrlIsApiAsync(result.SuggestedValue);
                if (!isApi)
                {
                    Report(
                        onProgress,
                        $"    ✖ {issue.FileName}: suggested baseUrl does not serve API responses — rejecting: {result.SuggestedValue}");
                    return false;
                }

                return true;
            }

            // The research agent uses IWebContentFetcher (Playwright) to browse pages,
            // fetch content, and search the web. If it returned Success=true, it already
            // confirmed the URL exists through its research process (fetched content,
            // verified via search results, etc.). Trust that result — do NOT re-verify
            // with CheckReachabilityAsync because many valid login/website pages return
            // 403 to direct navigation but work fine as SPAs.
            if (result.Success)
                return true;

            // Only for failed research results, do a final reachability check
            var check = await _fetcher.CheckReachabilityAsync(result.SuggestedValue);
            if (check.Reachable)
                return true;

            Report(
                onProgress,
                $"    ℹ {issue.FileName}: suggestion '{result.SuggestedValue}' not reachable (HTTP {check.HttpStatus}) — marked failed");
            return false;
        }

        // Non-URL fields: trust the research result if it succeeded.
        return result.Success;
    }

    /// <summary>
    /// Returns true if the field is expected to contain a URL value.
    /// An absent field is not a URL field.
    /// </summary>
    private static bool IsUrlField(string? field)
    {
        return string.Equals(field, ProviderJsonFields.Website, StringComparison.OrdinalIgnoreCase)
            || string.Equals(field, ProviderJsonFields.LoginUrl, StringComparison.OrdinalIgnoreCase)
            || string.Equals(field, ProviderJsonFields.ApiPricingUrl, StringComparison.OrdinalIgnoreCase)
            || string.Equals(field, ProviderJsonFields.SubscriptionPricingUrl, StringComparison.OrdinalIgnoreCase)
            || string.Equals(field, ProviderJsonFields.DocumentationUrl, StringComparison.OrdinalIgnoreCase)
            || string.Equals(field, ProviderJsonFields.BaseUrl, StringComparison.OrdinalIgnoreCase);
    }
}
