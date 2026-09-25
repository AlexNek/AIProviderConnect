namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Checks a single URL field from the provider manifest: gates (blank, applicability
/// suppression, sentinel, URI parse, private-host), pricing branch, reachability
/// branch with redirect caps, content probes, field-specific judgments, and HTTP
/// status classification.
/// </summary>
public interface IUrlFieldChecker
{
    /// <summary>
    /// Runs the full field check for one URL field. Gates (blank, applicability
    /// suppression, sentinel, URI parse, private-host) are handled internally;
    /// the host retains the blank-value, sentinel, and URI-parse continues.
    /// The context carries the run-level flags (UseLocalProviders, Applicability).
    /// </summary>
    Task CheckFieldAsync(FieldCheckContext context);
}
