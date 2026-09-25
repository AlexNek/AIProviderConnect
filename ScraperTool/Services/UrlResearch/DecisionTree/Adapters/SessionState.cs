namespace ScraperTool.Services.UrlResearch.DecisionTree.Adapters;

/// <summary>Immutable snapshot of the current decision-tree execution session.</summary>
public sealed record SessionState(string TreeId, IProgress<string>? Progress);
