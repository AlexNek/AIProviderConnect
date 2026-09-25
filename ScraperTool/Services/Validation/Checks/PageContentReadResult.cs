namespace ScraperTool.Services.Validation.Checks;

/// <summary>
/// What <see cref="IPageContentProbe.ReadBodyForErrorPageAsync"/> settled about a page:
/// its body (or null when nothing could be read), and — when the field is loginUrl —
/// the login-surface verdict the probe derived from that body in the same call.
/// </summary>
/// <param name="Body">The page body, or null when the fetch failed or the page carried no text.</param>
/// <param name="LoginVerdict">
/// The login-surface verdict when the field is loginUrl and the body was read;
/// <see cref="ELoginUrlVerdict.NotEvaluated"/> otherwise.
/// </param>
/// <param name="LoginReason">
/// The reason the probe gave for its login verdict, or a default message when the analysis
/// was not run.
/// </param>
public sealed record PageContentReadResult(
    string? Body,
    ELoginUrlVerdict LoginVerdict = ELoginUrlVerdict.NotEvaluated,
    string LoginReason = "the page could not be read");
