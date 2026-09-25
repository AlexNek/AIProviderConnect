namespace ScraperTool.ViewModels;

/// <summary>
/// A display entry in the decision-tree picker, pairing a human-readable
/// display name with the backing file name used for loading.
/// </summary>
public sealed record DecisionTreeEntry(string DisplayName, string FileName);
