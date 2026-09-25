using ScraperTool.Models;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Fetches a pricing URL and judges whether its page displays the expected
/// pricing content (API per-token amounts or subscription tiers).
/// </summary>
public interface IPricingPageVerifier
{
    /// <summary>
    /// Reads a stored pricing URL and reports what its page proved. The verdict
    /// is returned as well as the issue so that a page which could not be judged
    /// is named as such in the log instead of being folded into a pass.
    /// </summary>
    Task<(EPricingContentVerdict Verdict, string Reason)> VerifyPricingUrlAsync(
        Uri uri,
        string url,
        string fileName,
        string fieldName,
        IValidationIssueSink sink,
        CancellationToken ct = default);
}
