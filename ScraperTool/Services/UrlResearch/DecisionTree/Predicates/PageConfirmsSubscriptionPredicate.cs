using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Predicates;

/// <summary>
/// Confirms the classifier's positive subscription answer against the page that was actually
/// fetched, so a small model cannot turn a per-token price list into a "subscription pricing"
/// page. The subscriptionPricingUrl tree routes the LLM's <c>subscription_pricing</c> and
/// <c>both</c> answers through this gate before accepting them. The answer is overridden to not
/// applicable ("-") only when the fetched page positively declares usage-based billing ("pay as
/// you go") AND never mentions the word "subscription" — the signature of a per-token price list.
/// A page that mentions "subscription", or declares neither signal, is left to the classifier.
/// <para>
/// The decision is read from the live fetched page held in <see cref="ProviderResearchCache"/>,
/// never from the provider's stored pricing fields, which can be wrong or stale. If no fetched
/// content is available the predicate does not override the classifier (it returns true), so the
/// run falls back to the prior verify-reachable path instead of guessing "-".
/// </para>
/// </summary>
public sealed class PageConfirmsSubscriptionPredicate : IDecisionPredicate
{
    private const string SubscriptionWord = "subscription";
    private const string PayAsYouGoPhrase = "pay as you go";
    private const string PayAsYouGoHyphenated = "pay-as-you-go";

    private readonly ProviderResearchCache _cache;

    public string Key => "pageConfirmsSubscription";

    public PageConfirmsSubscriptionPredicate(ProviderResearchCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public bool Evaluate(DecisionPredicateContext context)
    {
        if (!context.State.Properties.TryGetValue("lastFetchedUrl", out var urlObj)
            || urlObj is not string url
            || string.IsNullOrWhiteSpace(url))
        {
            // Nothing was fetched to judge — do not override the classifier.
            return true;
        }

        var cached = _cache.GetPageFetch(url);
        if (cached is not { Success: true } || string.IsNullOrWhiteSpace(cached.MarkdownContent))
        {
            // The fetched page content is unavailable — do not override the classifier.
            return true;
        }

        var content = cached.MarkdownContent;
        var declaresPayAsYouGo = ContainsPayAsYouGo(content);
        var mentionsSubscription = content.Contains(SubscriptionWord, StringComparison.OrdinalIgnoreCase);

        // Not applicable only when the page declares usage-based billing and never mentions a
        // subscription; every other page is left to the classifier's answer.
        if (declaresPayAsYouGo && !mentionsSubscription)
            return false;

        return true;
    }

    private static bool ContainsPayAsYouGo(string content) =>
        content.Contains(PayAsYouGoPhrase, StringComparison.OrdinalIgnoreCase)
        || content.Contains(PayAsYouGoHyphenated, StringComparison.OrdinalIgnoreCase);
}
