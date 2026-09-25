namespace ScraperTool.Services;

/// <summary>
/// What the root domain of a subdomain-valued <c>website</c> established about that value.
/// <para>
/// A <c>website</c> sitting on a subdomain of a registrable domain the provider itself uses is a
/// page on the provider's own site — documentation, a console, a forum — while the field asks for
/// the public homepage. The subdomain page cannot tell that case apart from the opposite one,
/// because a page hosted by a provider mentions that provider by construction; judging the stored
/// page therefore certifies almost anything. The root domain can, since whoever answers there
/// decides whether the subdomain is one of its sections or a service of its own.
/// </para>
/// <para>
/// <see cref="NotEvaluated"/> is deliberately not a verdict in favour of the stored value. It
/// covers a root page that could not be read at all, and a definition carrying no word by which
/// the provider could be recognised. The caller keeps the stored value and names the abstention in
/// the message, because <see cref="ValidationStage"/> has no member for "could not be evaluated".
/// </para>
/// </summary>
public enum EWebsiteRootVerdict
{
    /// <summary>
    /// Declared first so that an unconfigured test double defaults to abstaining rather than
    /// inventing evidence.
    /// </summary>
    NotEvaluated = 0,

    /// <summary>
    /// The root domain answers as its own site and names the provider, so the stored subdomain is
    /// one of its pages rather than the homepage the field asks for.
    /// </summary>
    ProviderOwnsRoot,

    /// <summary>
    /// The root domain belongs to somebody else or hands its visitors on, so the service really
    /// does live at the subdomain that is stored.
    /// </summary>
    ForeignRoot
}
