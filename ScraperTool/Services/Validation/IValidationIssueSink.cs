namespace ScraperTool.Services.Validation;

/// <summary>
/// Collects the issues of one validation run and reports the progress lines that belong to them.
/// <para>
/// Validation used to append to a list of issues and report progress against that same list from
/// every check, which meant each check re-derived the two values progress depends
/// on — how many issues exist now, and which one was just added — and could get them out of step
/// with the list. The sink owns both, so a caller states what happened once and the numbers cannot
/// disagree with the list any more.
/// </para>
/// <para>
/// A sink is created per file validation and passed down as a parameter; it is not a shared
/// service, because the issue list and the progress target it speaks to belong to one run.
/// </para>
/// </summary>
public interface IValidationIssueSink
{
    /// <summary>
    /// Takes the position the issue list is at now, so a later <see cref="HasIssuesSince"/> can say
    /// whether a nested check added anything. The value is opaque on purpose.
    /// </summary>
    int Mark();

    /// <summary>
    /// Whether any issue was appended after the position returned by <see cref="Mark"/>.
    /// </summary>
    bool HasIssuesSince(int mark);

    /// <summary>
    /// Appends an issue without reporting progress. Used when the finding is not the whole story —
    /// a nested check files it and the caller reports the verdict that covers it.
    /// </summary>
    ValidationIssue Append(ValidationIssue issue);

    /// <summary>
    /// Appends the issue a field check files about the value currently stored in the manifest,
    /// carrying <paramref name="field"/> and <paramref name="url"/> as its field and current value.
    /// </summary>
    ValidationIssue Append(
        string fileName,
        string field,
        string url,
        string issueCode,
        string message);

    /// <summary>
    /// Reports a step of the run that is not a verdict — the checks being entered, and the notices
    /// raised before any issue exists. Carries no issue count, as those lines never did.
    /// </summary>
    void Progress(
        string fileName,
        string field,
        string url,
        ValidationStage stage,
        string? message = null);

    /// <summary>
    /// Reports a verdict, with the issue count taken from the list at this moment and the finding it
    /// is about when the caller names one.
    /// </summary>
    void Report(
        string fileName,
        string field,
        string url,
        ValidationStage stage,
        string? message = null,
        ValidationIssue? issue = null);

    /// <summary>
    /// Reports that a field settled with nothing against it, including the case where a finding was
    /// filed that is not a check failure (bot protection keeps its stored URL).
    /// </summary>
    void Pass(string fileName, string field, string url, string? message = null);

    /// <summary>
    /// Reports the failure filed by a nested check: the last issue in the list is both the finding
    /// and, unless the caller words it differently, the line shown for it.
    /// </summary>
    void FailAppended(string fileName, string field, string url, string? message = null);

    /// <summary>
    /// Files the issue for a field and reports the failure that names it — the one thing almost every
    /// field check ends with, kept in one call so the finding and its verdict cannot diverge.
    /// </summary>
    /// <returns>The appended issue.</returns>
    ValidationIssue FailWith(
        string fileName,
        string field,
        string url,
        string issueCode,
        string message,
        string? resultMessage = null);
}
