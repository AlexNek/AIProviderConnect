using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

using WebTools.NET.Abstractions;
using WebTools.NET.Models;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Verifies that the last fetched URL is reachable (HTTP 2xx/3xx).
/// Records the result in state for the is-reachable condition predicate.
/// </summary>
public sealed class VerifyReachableAction : IDecisionAction
{
    private readonly IWebContentFetcher _fetcher;

    public string Key => "verifyReachable";

    public VerifyReachableAction(IWebContentFetcher fetcher)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
    }

    public async Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        // Reset reachability state BEFORE any early returns. The executor may
        // discard state mutations made on failure paths, so a stale "true"
        // from a previous iteration could leak through if we don't reset here.
        context.State.Properties["lastVerifySucceeded"] = false;

        // Determine the URL to verify. Prefer lastFetchedUrl (set by
        // fetch-next-candidate after fetching a candidate page). Fall back
        // to knownCurrentValue (set by record-current-fact) for trees that
        // go directly to verify-reachable without fetching a candidate —
        // e.g. the baseUrl tree verifying an existing value.
        string? url = null;

        if (context.State.Properties.TryGetValue("lastFetchedUrl", out var fetchedObj)
            && fetchedObj is string fetchedUrl
            && !string.IsNullOrWhiteSpace(fetchedUrl))
        {
            url = fetchedUrl;
        }
        else if (context.State.Properties.TryGetValue("knownCurrentValue", out var knownObj)
                 && knownObj is string knownValue
                 && !string.IsNullOrWhiteSpace(knownValue))
        {
            url = knownValue;
        }

        if (url is null)
        {
            return new DecisionActionResult(
                null,
                null,
                DecisionActionStatus.PermanentFailure,
                "No URL to verify.");
        }

        // For the baseUrl field, neither shape of candidate is answered by fetching it as a web
        // page: an API base's root typically 404s (which would wrongly reject the correct value),
        // and a site or documentation page takes a browser render to say nothing this field can
        // use. Both are let through unscored to probe-models-endpoint, which asks
        // {base}/v1/models and reads the reply — the authoritative test for an API base.
        var isBaseUrlField = context.TemplateParameters.TryGetValue("fieldKind", out var fieldKindValue)
                             && string.Equals(fieldKindValue, "baseUrl", StringComparison.Ordinal);
        if (isBaseUrlField)
        {
            var isApiBase = ScanSiblingContentAction.IsApiLikeUrl(url);

            context.State.Properties["lastVerifySucceeded"] = true;

            // A page is not recorded as the winner: the field cannot be answered with a page
            // address, and the probe that decides may fail and leave it behind as the suggestion.
            if (isApiBase)
                context.State.Properties["verifiedWinnerUrl"] = url;

            return new DecisionActionResult(
                new[]
                {
                    new DecisionData
                    {
                        Id = $"reach-{Guid.NewGuid():N}",
                        Source = url,
                        Type = "Reachability",
                        Content = isApiBase ? "api-base-deferred-to-probe" : "page-candidate-deferred-to-probe",
                        CreatedAt = DateTimeOffset.UtcNow,
                        ActionId = context.NodeId,
                        Metadata = new Dictionary<string, string>
                        {
                            ["reachable"] = "true",
                            ["error"] = ""
                        }
                    }
                },
                new Dictionary<string, string>
                {
                    ["verifyResult"] = isApiBase ? "reachable" : "page-deferred-to-probe"
                },
                DecisionActionStatus.Success);
        }

        try
        {
            // Use a lightweight fetch to check reachability
            var result = await _fetcher.FetchAsAsync(url, EContentFormat.Markdown, ct: cancellationToken);

            var succeeded = result.Success && !string.IsNullOrWhiteSpace(result.Content);
            context.State.Properties["lastVerifySucceeded"] = succeeded;

            // When verification succeeds, record the verified URL separately so
            // that downstream logic (DetermineSuggestedValue) can use it even if
            // lastFetchedUrl is later overwritten by another fetch-next-candidate call.
            if (succeeded)
                context.State.Properties["verifiedWinnerUrl"] = url;

            var reachabilityData = new DecisionData
            {
                Id = $"reach-{Guid.NewGuid():N}",
                Source = url,
                Type = "Reachability",
                Content = succeeded ? "reachable" : (result.ErrorMessage ?? "unreachable"),
                CreatedAt = DateTimeOffset.UtcNow,
                ActionId = context.NodeId,
                Metadata = new Dictionary<string, string>
                {
                    ["reachable"] = succeeded.ToString(),
                    ["error"] = result.ErrorMessage ?? ""
                }
            };

            return new DecisionActionResult(
                new[] { reachabilityData },
                new Dictionary<string, string>
                {
                    ["verifyResult"] = succeeded ? "reachable" : "unreachable"
                },
                succeeded ? DecisionActionStatus.Success : DecisionActionStatus.PermanentFailure,
                succeeded ? null : result.ErrorMessage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.State.Properties["lastVerifySucceeded"] = false;
            return new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["verifyResult"] = "error" },
                DecisionActionStatus.TransientFailure,
                ex.Message);
        }
    }
}
