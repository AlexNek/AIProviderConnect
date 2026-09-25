namespace ScraperTool.Models;

/// <summary>
/// Represents the validation level of a provider definition.
/// </summary>
public enum ValidationLevel
{
    /// <summary>
    /// Provider has never been validated.
    /// </summary>
    NotValidated = 0,

    /// <summary>
    /// Validation failed with errors.
    /// </summary>
    ValidationError = -1,

    /// <summary>
    /// Automatically validated by the validator tool (URLs reachable, structure valid).
    /// </summary>
    AutoValidated = 1,

    /// <summary>
    /// Manually reviewed and confirmed by a human.
    /// </summary>
    ManuallyValidated = 2,

    /// <summary>
    /// Verified by the provider owner or trusted source (future use).
    /// </summary>
    VerifiedByOwner = 3
}
