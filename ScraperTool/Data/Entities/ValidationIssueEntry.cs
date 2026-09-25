namespace ScraperTool.Data.Entities;

public sealed class ValidationIssueEntry
{
    public DateTime? AiAttemptedAt { get; set; }

    public required string Code { get; set; }

    public string? CurrentValue { get; set; }

    public string? Field { get; set; }

    public required string FileName { get; set; }

    public int Id { get; set; }

    public required string Message { get; set; }

    public DateTime SavedAt { get; set; }

    public string? SuggestedValue { get; set; }

    public string? SuggestionReason { get; set; }

    public string? SuggestionSeverity { get; set; }

    public IssueSuggestionStatus SuggestionStatus { get; set; }
}
