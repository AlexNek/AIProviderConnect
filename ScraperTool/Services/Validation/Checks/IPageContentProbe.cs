using ScraperTool.Models;

namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// Fetches page bodies and judges their content: not-found detection,
/// login-surface analysis, and login-content probing.
/// </summary>
public interface IPageContentProbe
{
    /// <summary>
    /// Fetches the page at <paramref name="url"/>, checks it for 404/not-found indicators,
    /// and — when the field is loginUrl — also judges whether the page is an authentication
    /// surface. The two questions are settled in one call so the caller never fetches twice
    /// (invariant 2: the loginUrl judgment reuses the body read for the error-page check).
    /// </summary>
    /// <returns>A <see cref="PageContentReadResult"/> carrying the body and, when applicable, the login verdict.</returns>
    Task<PageContentReadResult> ReadBodyForErrorPageAsync(
        string url,
        string fileName,
        string field,
        IValidationIssueSink sink,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches the page at <paramref name="url"/> and checks whether it contains
    /// login/sign-in elements. Used when loginUrl equals website.
    /// </summary>
    /// <returns>
    /// <c>Success</c> is false when the fetch failed; <c>HasLoginContent</c>
    /// is only meaningful when <c>Success</c> is true.
    /// </returns>
    Task<(bool Success, bool HasLoginContent)> CheckLoginContentAtUrlAsync(
        string url,
        CancellationToken ct = default);

    /// <summary>
    /// Runs the full login-equality check (fetch + content analysis) and emits the appropriate
    /// findings to the sink. Returns true when the check settled the field (i.e., the caller
    /// should return without further checks); false when the caller should continue.
    /// </summary>
    Task<bool> TrySettleLoginUrlEqualityAsync(
        string loginUrl,
        string fileName,
        IValidationIssueSink sink,
        CancellationToken ct);
}
