namespace ScraperTool.Services;

/// <summary>
/// What reading a stored pricing URL established about the page behind it.
/// <para>
/// Reachability and vocabulary are not evidence for these fields: a documentation or product page
/// mentions "pricing", "cost" and "model" in its navigation and prose without listing a single
/// amount, so the verdict is earned by an <em>amount</em> that the page displays — a currency
/// figure, a per-token/per-1M unit, or a priced table row — and not by how many pricing words the
/// page happens to contain.
/// </para>
/// <para>
/// <see cref="NotEvaluated"/> is deliberately not a verdict in favour of the address. It covers a
/// body too thin to hold a table at all — a page that failed to render, or one whose prices arrive
/// later than the render window — which says nothing about whether the page is the pricing page.
/// The caller keeps the stored value and names the abstention in the message, because
/// <see cref="ValidationStage"/> has no member for "could not be evaluated".
/// </para>
/// </summary>
public enum EPricingContentVerdict
{
    /// <summary>
    /// Declared first so that an unconfigured test double defaults to abstaining rather than
    /// inventing evidence.
    /// </summary>
    NotEvaluated = 0,

    /// <summary>The page displays at least one price — an amount, a per-unit rate, or a priced row.</summary>
    HasPricing,

    /// <summary>The page was read in full and displays no price of any kind on it.</summary>
    NoPricing
}
