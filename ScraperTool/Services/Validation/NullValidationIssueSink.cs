namespace ScraperTool.Services.Validation;

/// <summary>
/// A no-op implementation of <see cref="IValidationIssueSink"/> for contexts where the caller
/// only needs the verdict from a verifier and does not want issues persisted or progress reported.
/// </summary>
internal sealed class NullValidationIssueSink : IValidationIssueSink
{
    public int Mark() => 0;

    public bool HasIssuesSince(int mark) => false;

    public ValidationIssue Append(ValidationIssue issue) => issue;

    public ValidationIssue Append(
        string fileName,
        string field,
        string url,
        string issueCode,
        string message) => new ValidationIssue(fileName, issueCode, message) { Field = field, CurrentValue = url };

    public void Progress(
        string fileName,
        string field,
        string url,
        ValidationStage stage,
        string? message = null)
    {
    }

    public void Report(
        string fileName,
        string field,
        string url,
        ValidationStage stage,
        string? message = null,
        ValidationIssue? issue = null)
    {
    }

    public void Pass(string fileName, string field, string url, string? message = null)
    {
    }

    public void FailAppended(string fileName, string field, string url, string? message = null)
    {
    }

    public ValidationIssue FailWith(
        string fileName,
        string field,
        string url,
        string issueCode,
        string message,
        string? resultMessage = null) => new ValidationIssue(fileName, issueCode, message) { Field = field, CurrentValue = url };
}
