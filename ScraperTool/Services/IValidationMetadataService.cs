using ScraperTool.Models;

namespace ScraperTool.Services;

/// <summary>
/// Manages validation sidecar files (.validation.json) for provider definitions.
/// Single responsibility: all read/write operations for validation metadata.
/// </summary>
public interface IValidationMetadataService
{
    /// <summary>
    /// Deletes the validation sidecar file for a provider.
    /// </summary>
    void Delete(string providerFilePath);

    /// <summary>
    /// Reads validation metadata from a sidecar .validation.json file.
    /// </summary>
    ValidationMetadata? Read(string providerFilePath);

    /// <summary>
    /// Saves validation metadata after an automatic validation run.
    /// </summary>
    /// <param name="providerFilePath">Path of the provider file the sidecar belongs to.</param>
    /// <param name="issues">Issues reported by the validation run.</param>
    /// <param name="validatedBy">Name of the validator that produced the issues.</param>
    /// <param name="notes">Optional notes stored alongside the issues.</param>
    /// <param name="force">
    /// When true, overwrites even manually validated sidecars.
    /// Used by the validator after an explicit revalidation run.
    /// </param>
    Task SaveFromValidationAsync(
        string providerFilePath,
        IReadOnlyList<ValidationIssue> issues,
        string validatedBy = ValidatorConstants.AutoValidator,
        string? notes = null,
        bool force = false);

    /// <summary>
    /// Updates validation metadata (e.g., manual validation mark).
    /// </summary>
    Task UpdateAsync(
        string providerFilePath,
        ValidationLevel level,
        string validatedBy,
        string? notes = null);
}
