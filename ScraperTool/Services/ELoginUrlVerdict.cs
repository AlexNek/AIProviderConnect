namespace ScraperTool.Services;

/// <summary>
/// What reading a stored <c>loginUrl</c> established about it as an authentication surface.
/// <para>
/// Reachability is not evidence for this field: a sign-in page answers any query placed after it,
/// so a login address that was verified once cannot be told apart from one carrying arbitrary
/// junk, and a link that merely mentions signing in is not the page that authenticates. The field
/// therefore gets its own verdict instead of borrowing the generic "URL is reachable" pass.
/// </para>
/// <para>
/// <see cref="NotEvaluated"/> is deliberately not a verdict in favour of the address. It covers a
/// page that could not be read, one that renders its sign-in surface client-side, and one that only
/// links to authentication — all of which say nothing about whether the page <em>is</em> the entry
/// point. The caller keeps the stored value and names the abstention in the message, because
/// <see cref="ValidationStage"/> has no member for "could not be evaluated".
/// </para>
/// </summary>
public enum ELoginUrlVerdict
{
    /// <summary>
    /// Declared first so that an unconfigured test double defaults to abstaining rather than
    /// inventing evidence.
    /// </summary>
    NotEvaluated = 0,

    /// <summary>The page carries a credential form, or the address itself names an authentication endpoint.</summary>
    Confirmed,

    /// <summary>The page was read and offers no authentication surface at all.</summary>
    NotLoginPage
}
