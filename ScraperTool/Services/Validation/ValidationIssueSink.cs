namespace ScraperTool.Services.Validation;

/// <summary>
/// The <see cref="IValidationIssueSink"/> over the issue list and the progress target of one
/// validation run. The host owns the list — it is what the run returns and what the sidecar is
/// written from — and the sink reports against it, so the issue count and the finding a verdict
/// names are always the ones the list actually holds.
/// </summary>
public sealed class ValidationIssueSink : IValidationIssueSink
{
    private readonly List<ValidationIssue> _issues;

    private readonly IProgress<ValidationProgress>? _progress;

    public ValidationIssueSink(List<ValidationIssue> issues, IProgress<ValidationProgress>? progress)
    {
        _issues = issues ?? throw new ArgumentNullException(nameof(issues));
        _progress = progress;
    }

    /// <inheritdoc />
    public int Mark() => _issues.Count;

    /// <inheritdoc />
    public bool HasIssuesSince(int mark) => _issues.Count > mark;

    /// <inheritdoc />
    public ValidationIssue Append(ValidationIssue issue)
    {
        _issues.Add(issue);
        return issue;
    }

    /// <inheritdoc />
    public ValidationIssue Append(
        string fileName,
        string field,
        string url,
        string issueCode,
        string message)
    {
        return Append(
            new ValidationIssue(fileName, issueCode, message)
            {
                Field = field,
                CurrentValue = url
            });
    }

    /// <inheritdoc />
    public void Progress(
        string fileName,
        string field,
        string url,
        ValidationStage stage,
        string? message = null)
    {
        _progress?.Report(new ValidationProgress(fileName, field, url, stage, message));
    }

    /// <inheritdoc />
    public void Report(
        string fileName,
        string field,
        string url,
        ValidationStage stage,
        string? message = null,
        ValidationIssue? issue = null)
    {
        _progress?.Report(
            new ValidationProgress(
                fileName,
                field,
                url,
                stage,
                message,
                IssuesCount: _issues.Count,
                Issue: issue));
    }

    /// <inheritdoc />
    public void Pass(string fileName, string field, string url, string? message = null) =>
        Report(fileName, field, url, ValidationStage.CheckPassed, message);

    /// <inheritdoc />
    public void FailAppended(string fileName, string field, string url, string? message = null)
    {
        // Mirrors the progress?.Report(...) short-circuit this replaces: with no listener the
        // last-issue lookup never happened, so an empty list reported nothing instead of failing.
        if (_progress is null)
            return;

        var issue = _issues[^1];
        Report(fileName, field, url, ValidationStage.CheckFailed, message ?? issue.Message, issue);
    }

    /// <inheritdoc />
    public ValidationIssue FailWith(
        string fileName,
        string field,
        string url,
        string issueCode,
        string message,
        string? resultMessage = null)
    {
        var issue = Append(fileName, field, url, issueCode, message);
        Report(fileName, field, url, ValidationStage.CheckFailed, resultMessage ?? message, issue);
        return issue;
    }
}
