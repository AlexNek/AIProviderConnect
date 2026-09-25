using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed class ValidationResult
{
    public bool HasAiSetupNeeded { get; set; }

    public bool HasUrlIssues { get; set; }

    public List<ValidationIssue> Issues { get; } = [];

    public List<ValidationIssue> LastBadIssues { get; set; } = [];

    public int LocalIssues { get; set; }

    public int StructuralIssues { get; set; }

    public int TotalProviderCount { get; set; }

    public int UrlErrors { get; set; }

    public int ValidatedCount { get; set; }

    public List<ValidationMetadata> ValidationStates { get; set; } = [];
}
