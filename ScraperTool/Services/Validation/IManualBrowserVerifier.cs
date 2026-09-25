namespace ScraperTool.Services.Validation;

/// <summary>
/// Opens a visible browser window so the user can solve a Cloudflare challenge
/// interactively, then re-checks whether the URL is reachable. Hides all threading,
/// UI, and browser complexity from the caller.
/// </summary>
public interface IManualBrowserVerifier
{
    /// <summary>
    /// Shows a modeless dialog, launches a visible CloakBrowser for the user,
    /// and returns the verification outcome after the user clicks "Done" or "Cancel".
    /// </summary>
    /// <param name="url">The Cloudflare-protected URL to verify.</param>
    /// <param name="field">The provider manifest field name (e.g. "website", "loginUrl").</param>
    /// <param name="fileName">The provider manifest file name for display context.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<EManualVerificationResult> TryVerifyAsync(
        string url,
        string field,
        string fileName,
        CancellationToken ct);
}
