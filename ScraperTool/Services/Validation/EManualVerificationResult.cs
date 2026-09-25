namespace ScraperTool.Services.Validation;

/// <summary>
/// Outcome of a manual browser verification attempt for a Cloudflare-protected URL.
/// </summary>
public enum EManualVerificationResult
{
    /// <summary>The challenge cleared; the URL is confirmed reachable.</summary>
    Verified,

    /// <summary>The Cloudflare challenge is still present after the user attempted to solve it.</summary>
    StillProtected,

    /// <summary>The user cancelled the verification or no UI is available.</summary>
    UserCancelled
}
