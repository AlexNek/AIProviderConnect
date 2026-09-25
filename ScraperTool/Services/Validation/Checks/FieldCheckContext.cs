using System.Text.Json;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Carries the per-field state the field checker needs: the manifest's file name,
/// the field being checked, its URL and parsed URI, the JSON root, a settable slot
/// for the page body (so the content probe's read can be reused by the login
/// judgment), the cancellation token, and the run-level flags that settle
/// applicability and local-provider gating.
/// </summary>
public sealed record FieldCheckContext(
    string FileName,
    string Field,
    string Url,
    Uri Uri,
    JsonElement Root,
    IValidationIssueSink Sink,
    CancellationToken CancellationToken)
{
    /// <summary>
    /// The page body read by <see cref="IPageContentProbe.ReadBodyForErrorPageAsync"/>,
    /// reused by the login-surface judgment (invariant 2: the loginUrl field never
    /// fetches twice).
    /// </summary>
    public string? PageHtml { get; set; }

    /// <summary>
    /// When true, allow local/private host reachability checks; when false, silently skip them.
    /// </summary>
    public bool UseLocalProviders { get; init; }

    /// <summary>
    /// What the self-hosted pre-pass settled about the three fields a locally hosted provider
    /// cannot carry. A set flag means the field has been resolved as not applicable.
    /// </summary>
    public SelfHostedApplicabilityFlags Applicability { get; init; } = new(false, false, false);
}
