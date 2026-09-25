using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed record ValidationProgress(
    string FileName,
    string Field,
    string Url,
    ValidationStage Stage,
    string? ResultMessage = null,
    int IssuesCount = 0,
    ValidationIssue? Issue = null);
