using AiCleverness.Models;
using AiCleverness.Models.DecisionTree;
using AiCleverness.Runtime.Transcript;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Transcript;

/// <summary>
/// Enriches the decision-tree transcript "Selected path" section with a readable summary
/// line above each technical path entry. Action nodes use data captured from
/// <c>DecisionAction</c>, classify nodes use the answer captured from
/// <c>DecisionClassification</c>, and predicate condition nodes use the true/false outcome
/// rendered in the path entry (predicate conditions emit no builder callback).
/// </summary>
/// <remarks>
/// Create a fresh instance per execution via
/// <c>DecisionTreeExecutionOptions.TranscriptBuilderFactory</c>; the captured state is
/// mutable and must not be shared between executions.
/// </remarks>
public sealed class ReadablePathTranscriptDecorator : TranscriptBuilderDecorator
{
    private const string TechnicalLineIndent = "   ";
    private const string TransitionMarker = " -- `";

    private readonly Dictionary<string, Queue<CapturedActionData>> _actionCaptures =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _classificationAnswers =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public override string DecisionAction(
        string nodeId,
        string actionKey,
        string? nodeName,
        DecisionActionStatus status,
        string? outcomeSummary,
        string? error,
        string? producedData)
    {
        if (!string.IsNullOrEmpty(nodeId))
        {
            if (!_actionCaptures.TryGetValue(nodeId, out var queue))
            {
                queue = new Queue<CapturedActionData>();
                _actionCaptures[nodeId] = queue;
            }

            queue.Enqueue(new CapturedActionData(nodeName, actionKey, status, outcomeSummary, error));
        }

        return base.DecisionAction(nodeId, actionKey, nodeName, status, outcomeSummary, error, producedData);
    }

    /// <inheritdoc />
    public override string DecisionClassification(
        string nodeId,
        string answer,
        string? observation,
        string? confidence,
        int attempt)
    {
        if (!string.IsNullOrEmpty(nodeId))
            _classificationAnswers[nodeId] = answer;

        return base.DecisionClassification(nodeId, answer, observation, confidence, attempt);
    }

    /// <inheritdoc />
    public override string DecisionResult(
        DecisionTreeOutcome outcome,
        bool succeeded,
        string? verdict,
        string? error,
        ResourceUsage usage,
        IReadOnlyList<string> path,
        int omittedSectionCount = 0,
        IReadOnlyList<KeyValuePair<string, string>>? stateProperties = null)
    {
        var enrichedPath = EnrichPath(path);
        return base.DecisionResult(
            outcome,
            succeeded,
            verdict,
            error,
            usage,
            enrichedPath,
            omittedSectionCount,
            stateProperties);
    }

    private IReadOnlyList<string> EnrichPath(IReadOnlyList<string> path)
    {
        var enriched = new List<string>(path.Count);
        foreach (var entry in path)
        {
            var summary = TryBuildSummary(entry);
            enriched.Add(summary is null
                ? entry
                : $"{summary}{Environment.NewLine}{TechnicalLineIndent}{entry}");
        }

        return enriched;
    }

    private string? TryBuildSummary(string entry)
    {
        var nodeId = ExtractNodeId(entry);
        if (nodeId is null)
            return null;

        if (_actionCaptures.TryGetValue(nodeId, out var queue) && queue.Count > 0)
            return BuildActionSummary(queue.Dequeue());

        if (_classificationAnswers.TryGetValue(nodeId, out var answer))
            return $"{TitleCase(nodeId)} \u2192 {answer}";

        // Predicate condition nodes emit no DecisionClassification callback; their only
        // outcome signal is the rendered path entry, and a predicate outcome is always
        // "true" or "false".
        var outcome = ExtractTransitionOutcome(entry);
        if (outcome is "true" or "false")
            return $"{TitleCase(nodeId)} \u2192 {outcome}";

        return null;
    }

    private static string BuildActionSummary(CapturedActionData data)
    {
        var name = string.IsNullOrWhiteSpace(data.NodeName) ? data.ActionKey : data.NodeName;
        return $"{name} \u2014 {DescribeOutcome(data)}";
    }

    private static string DescribeOutcome(CapturedActionData data)
    {
        if (!string.IsNullOrWhiteSpace(data.OutcomeSummary))
            return data.OutcomeSummary;

        return data.Status switch
        {
            DecisionActionStatus.Success => "completed",
            DecisionActionStatus.TransientFailure => WithError("failed", data.Error),
            DecisionActionStatus.PermanentFailure => WithError("failed (permanent)", data.Error),
            _ => "completed"
        };
    }

    private static string WithError(string statusText, string? error)
        => string.IsNullOrWhiteSpace(error) ? statusText : $"{statusText}: {error}";

    private static string? ExtractNodeId(string entry)
    {
        var start = entry.IndexOf('`');
        if (start < 0)
            return null;

        var end = entry.IndexOf('`', start + 1);
        if (end < 0)
            return null;

        var nodeId = entry[(start + 1)..end];
        return string.IsNullOrEmpty(nodeId) ? null : nodeId;
    }

    private static string? ExtractTransitionOutcome(string entry)
    {
        var marker = entry.IndexOf(TransitionMarker, StringComparison.Ordinal);
        if (marker < 0)
            return null;

        var start = marker + TransitionMarker.Length;
        var end = entry.IndexOf('`', start);
        if (end <= start)
            return null;

        var outcome = entry[start..end];
        return string.IsNullOrEmpty(outcome) ? null : outcome;
    }

    private static string TitleCase(string nodeId)
    {
        var parts = nodeId.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return nodeId;

        for (var i = 0; i < parts.Length; i++)
            parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i][1..];

        return string.Join(' ', parts);
    }
}
