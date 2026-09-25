using ScraperTool.Data.Entities;

namespace ScraperTool.Data.Repositories;

/// <summary>
/// Read/write repository for validation issues.
/// </summary>
public interface IValidationIssueRepository
{
    /// <summary>
    /// Deletes the specified validation issue entries.
    /// </summary>
    Task DeleteAsync(IReadOnlyList<ValidationIssueEntry> entries);

    /// <summary>
    /// Gets all validation issues, ordered by file name and code.
    /// </summary>
    Task<IReadOnlyList<ValidationIssueEntry>> GetAllAsync();

    /// <summary>
    /// Replaces all validation issues with the provided collection.
    /// </summary>
    Task ReplaceAllAsync(IEnumerable<ValidationIssueEntry> issues);

    /// <summary>
    /// Updates AI suggestion fields on existing entries.
    /// </summary>
    Task UpdateSuggestionsAsync(IReadOnlyList<ValidationIssueEntry> updates);
}
