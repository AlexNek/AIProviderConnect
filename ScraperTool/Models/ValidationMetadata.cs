namespace ScraperTool.Models;

/// <summary>
/// Represents the validation state of a provider definition.
/// Stored in sidecar .validation.json files alongside provider JSON files.
/// </summary>
public sealed record ValidationMetadata
{
    /// <summary>
    /// Gets or sets the timestamp of the last validation run.
    /// </summary>
    public DateTime? LastValidatedAt { get; set; }

    /// <summary>
    /// Gets or sets the validation level indicating how thoroughly this provider has been validated.
    /// </summary>
    public ValidationLevel Level { get; set; } = ValidationLevel.NotValidated;

    /// <summary>
    /// Gets or sets optional notes about the validation (e.g., "Manually reviewed pricing page").
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the provider ID this metadata belongs to.
    /// </summary>
    public required string ProviderId { get; set; }

    /// <summary>
    /// Gets or sets who performed the validation (e.g., "auto", "manual", "user@example.com").
    /// </summary>
    public string? ValidatedBy { get; set; }

    /// <summary>
    /// Gets or sets the validation errors found during the last validation run.
    /// Empty array if validation passed or not yet run.
    /// </summary>
    public string[] ValidationErrors { get; set; } = Array.Empty<string>();
}

