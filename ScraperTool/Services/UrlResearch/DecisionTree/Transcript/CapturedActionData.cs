using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Transcript;

/// <summary>
/// Action data captured during a decision-tree execution, keyed by node id, used to
/// build a readable Selected Path summary line in <see cref="ReadablePathTranscriptDecorator"/>.
/// </summary>
internal sealed record CapturedActionData(
    string? NodeName,
    string ActionKey,
    DecisionActionStatus Status,
    string? OutcomeSummary,
    string? Error);
