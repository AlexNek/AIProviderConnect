using ScraperTool.Data.Entities;

namespace ScraperTool.Data.Repositories;

/// <summary>
/// Read-only view of the validation issue repository.
/// </summary>
public interface IReadOnlyValidationIssueRepository
{
    /// <summary>
    /// Gets all validation issues, ordered by file name and code.
    /// </summary>
    Task<IReadOnlyList<ValidationIssueEntry>> GetAllAsync();
}
