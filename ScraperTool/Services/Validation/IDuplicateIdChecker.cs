namespace ScraperTool.Services.Validation;

/// <summary>
/// Checks for duplicate provider IDs across JSON files in a directory.
/// </summary>
public interface IDuplicateIdChecker
{
    /// <summary>
    /// Scans the directory for duplicate provider IDs.
    /// </summary>
    List<ValidationIssue> CheckDuplicateIds(string directoryPath);
}
