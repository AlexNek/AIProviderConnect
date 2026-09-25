using System.Text.Json;

using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Adapters;

/// <summary>
/// Adapts decision-tree execution events to <see cref="IProgress{T}"/>
/// (via <see cref="ExecutionSessionTracker"/>) so the ScraperTool UI log
/// displays real-time tree execution progress — node visits and LLM
/// classification results — instead of a single 80-second silence.
/// </summary>
/// <remarks>
/// Must be registered as a singleton so that the same instance handles
/// events from multiple event types within one execution session and
/// can track per-node visit counts (retry detection), action errors, and
/// the bounded data summaries actions report about what they produced.
/// </remarks>
public sealed class DecisionTreeProgressAdapter :
    IExecutionEventHandler<DecisionNodeVisitedBusEvent>,
    IExecutionEventHandler<DecisionClassificationCompletedBusEvent>,
    IExecutionEventHandler<DecisionActionCompletedBusEvent>
{
    private readonly ExecutionSessionTracker _tracker;
    private readonly Dictionary<string, int> _visitCounts = new();
    private readonly Dictionary<string, string> _actionErrors = new();
    private readonly Dictionary<string, string> _actionKeys = new();

    // Keyed by execution as well as node so a summary can never be reported against
    // the same node id belonging to a different execution.
    private readonly Dictionary<(string ExecutionId, string NodeId), DecisionActionDataSummary> _dataSummaries = new();

    private string? _currentExecutionId;

    public DecisionTreeProgressAdapter(ExecutionSessionTracker tracker)
    {
        _tracker = tracker;
    }

    public Task HandleAsync(DecisionNodeVisitedBusEvent event_, CancellationToken cancellationToken = default)
    {
        var session = _tracker.Current;
        if (session?.Progress is null)
            return Task.CompletedTask;

        ResetIfNewExecution(event_.ExecutionId);

        // Phase 1: Skip Classify nodes — the ClassificationCompleted handler
        // already produces a line with answer, observation, and attempt number.
        if (event_.NodeType == EDecisionNodeType.Classify)
            return Task.CompletedTask;

        // Phase 2: Track per-node visit counts for retry detection.
        _visitCounts.TryGetValue(event_.NodeId, out var visitCount);
        _visitCounts[event_.NodeId] = visitCount + 1;

        var isRetry = event_.NodeType == EDecisionNodeType.Action
                      && visitCount > 0
                      && IsFailureOutcome(event_.OutcomeJson);

        // Phase 3: Append error detail from the action-completed event.
        var errorSuffix = "";
        if (_actionErrors.TryGetValue(event_.NodeId, out var error))
            errorSuffix = $" — {error}";

        // Phase 4: Append action detail for scan/extraction actions. What the action
        // itself reported about its data wins over the static per-action text, and is
        // consumed here so a later visit to the same node cannot reuse it.
        var detailSuffix = "";
        if (_dataSummaries.Remove((event_.ExecutionId, event_.NodeId), out var dataSummary))
            detailSuffix = FormatDataSummary(dataSummary);
        else if (_actionKeys.TryGetValue(event_.NodeId, out var actionKey))
            detailSuffix = FormatActionDetail(actionKey);

        var retryPrefix = isRetry ? "↻ retry, " : "";
        var outcome = FormatOutcome(event_.OutcomeJson);
        var message = $"    [{event_.NodeType}] {event_.NodeId} → {retryPrefix}{outcome} ({event_.Duration.TotalMilliseconds:F0}ms){errorSuffix}{detailSuffix}";
        session.Progress.Report(message);
        return Task.CompletedTask;
    }

    public Task HandleAsync(DecisionClassificationCompletedBusEvent event_, CancellationToken cancellationToken = default)
    {
        var session = _tracker.Current;
        if (session?.Progress is null)
            return Task.CompletedTask;

        ResetIfNewExecution(event_.ExecutionId);

        // Phase 4: Clean up misleading LLM observations.
        var observation = string.IsNullOrWhiteSpace(event_.Observation)
            ? string.Empty
            : LooksLikeRawHttpError(event_.Observation)
                ? " — LLM could not determine the answer"
                : $" — {event_.Observation}";

        var message = $"    [Classify] {event_.NodeId} → {event_.Answer}{observation} (attempt {event_.Attempt})";
        session.Progress.Report(message);
        return Task.CompletedTask;
    }

    public Task HandleAsync(DecisionActionCompletedBusEvent event_, CancellationToken cancellationToken = default)
    {
        // Phase 3: Store the error message and action key keyed by NodeId so the
        // subsequent DecisionNodeVisitedBusEvent can append them to the progress line.
        if (_tracker.Current?.Progress is not null)
            ResetIfNewExecution(event_.ExecutionId);

        if (!string.IsNullOrWhiteSpace(event_.Error))
            _actionErrors[event_.NodeId] = event_.Error!;

        _actionKeys[event_.NodeId] = event_.ActionKey;

        // A completion that produced no data must drop the summary an earlier attempt at
        // this node reported, otherwise the node visit would credit it with data it does
        // not have.
        var summaryKey = (event_.ExecutionId, event_.NodeId);
        if (event_.DataSummary is null)
            _dataSummaries.Remove(summaryKey);
        else
            _dataSummaries[summaryKey] = event_.DataSummary;

        return Task.CompletedTask;
    }

    private void ResetIfNewExecution(string executionId)
    {
        if (_currentExecutionId != executionId)
        {
            _visitCounts.Clear();
            _actionErrors.Clear();
            _actionKeys.Clear();
            _dataSummaries.Clear();
            _currentExecutionId = executionId;
        }
    }

    private static bool IsFailureOutcome(string? outcomeJson)
    {
        if (string.IsNullOrWhiteSpace(outcomeJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(outcomeJson);
            if (doc.RootElement.TryGetProperty("status", out var statusProp)
                && statusProp.ValueKind == JsonValueKind.String)
            {
                var status = statusProp.GetString();
                return status is "transientFailure" or "TransientFailure"
                    or "permanentFailure" or "PermanentFailure";
            }
        }
        catch (JsonException)
        {
            var trimmed = outcomeJson.Trim();
            return trimmed is "transientFailure" or "TransientFailure"
                or "permanentFailure" or "PermanentFailure";
        }

        return false;
    }

    /// <summary>
    /// Formats the outcome JSON into a human-readable label.
    /// Maps internal status values to user-friendly labels.
    /// </summary>
    private static string FormatOutcome(string? outcomeJson)
    {
        if (string.IsNullOrWhiteSpace(outcomeJson))
            return string.Empty;

        // Try JSON object first (e.g. {"status":"success"})
        try
        {
            using var doc = JsonDocument.Parse(outcomeJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("status", out var statusProp)
                && statusProp.ValueKind == JsonValueKind.String)
            {
                return FormatStatus(statusProp.GetString());
            }
        }
        catch (JsonException)
        {
            // Not valid JSON — fall through to plain-string handling
        }

        // Plain string value (e.g. "success", "transientFailure")
        return FormatStatus(outcomeJson.Trim());
    }

    /// <summary>
    /// Maps internal action status values to user-friendly labels.
    /// </summary>
    private static string FormatStatus(string? status)
    {
        return status switch
        {
            "success" or "Success" => "success",
            "transientFailure" or "TransientFailure" => "failed",
            "permanentFailure" or "PermanentFailure" => "failed",
            null or "" => string.Empty,
            _ => status
        };
    }

    /// <summary>
    /// Returns a short descriptive suffix for scan/extraction actions so the
    /// user can see what URLs were discovered in the progress log.
    /// </summary>
    private static string FormatActionDetail(string actionKey)
    {
        return actionKey switch
        {
            "scanSiblingContent" => " — extracted URLs from sibling pages",
            "scanCandidateContent" => " — extracted URLs from candidate page content",
            "scanProviderLinks" => " — scanned page links",
            _ => ""
        };
    }

    /// <summary>
    /// Formats the bounded summary an action reported about the data it produced. Only the
    /// values the library supplies are shown — the event never carries the data itself — and
    /// the ellipsis marks that more items exist than previews are listed.
    /// </summary>
    private static string FormatDataSummary(DecisionActionDataSummary summary)
    {
        var ellipsis = summary.ItemCount > summary.ContentPreviews.Count ? ", …" : string.Empty;
        var noun = summary.ItemCount == 1 ? "item" : "items";
        return $" — extracted {summary.ItemCount} {noun}: {string.Join(", ", summary.ContentPreviews)}{ellipsis}";
    }

    /// <summary>
    /// Detects raw HTTP error strings that the LLM may have produced
    /// in its observation (e.g. "HTTP 404") and replaces them with
    /// a meaningful label.
    /// </summary>
    private static bool LooksLikeRawHttpError(string observation)
    {
        return observation.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase)
               && observation.Length > 5
               && char.IsDigit(observation[5]);
    }
}
