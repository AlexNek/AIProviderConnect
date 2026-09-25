namespace ScraperTool.Services.UrlResearch.Models;

/// <summary>
/// Per-field research semantics for the decision-tree pipeline: which other fields' pages
/// are worth scanning, what extra words mark a link as relevant, whether a related page can
/// itself be the answer, what to search for when the provider's pages did not answer, and
/// which value legitimately means "this provider has none".
/// <para>
/// Like <see cref="DecisionKeywordSets"/>, this is a pure model with no algorithm knowledge:
/// the words and the routing live in <c>Config/field-definitions.json</c> so a rule can be
/// corrected from the catalog without a recompile, and so no field or provider is
/// special-cased in code.
/// </para>
/// </summary>
public sealed class FieldResearchProfile
{
    /// <summary>
    /// The field kind this profile describes, matching the tree file name.
    /// </summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>
    /// Relevance words the field name does not already contain. Link ranking always derives
    /// tokens from the field name first; these add the vocabulary a provider might use
    /// instead (a sign-in link labelled "Subscribe" matches nothing in "loginUrl").
    /// </summary>
    public IReadOnlyList<string> RelevanceTerms { get; init; } = [];

    /// <summary>
    /// Names of sibling fields whose pages are scanned as extra link sources, in the order
    /// they should be read. The order matters: content scanning fills a bounded URL quota in
    /// sibling order, so a noisy page listed first can crowd out the page holding the answer.
    /// </summary>
    public IReadOnlyList<string> SiblingFields { get; init; } = [];

    /// <summary>
    /// Whether a sibling page is a plausible answer for the field and should therefore be
    /// queued for classification ahead of scanned links. When a field's sibling pages only
    /// ever link to the answer, classifying them costs a fetch and a model call for a
    /// guaranteed non-answer, and the profile sets this to false.
    /// </summary>
    public bool SiblingPageMayBeAnswer { get; init; } = true;

    /// <summary>
    /// Query the web-search fallback runs when the provider's own pages did not answer, with
    /// <c>{providerName}</c> left for template resolution. A field with no query is researched
    /// by the tree's own wording.
    /// </summary>
    public string? SearchQueryTemplate { get; init; }

    /// <summary>
    /// The value that legitimately means "this provider has none of these", e.g. <c>-</c>. Only
    /// a completed search may report it; a run that stopped early has not established the
    /// absence. Null means the field has no not-applicable value.
    /// </summary>
    public string? NotApplicableValue { get; init; }
}
