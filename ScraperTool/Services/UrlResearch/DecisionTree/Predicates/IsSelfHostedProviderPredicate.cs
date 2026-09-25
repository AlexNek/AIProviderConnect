using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Predicates;

/// <summary>
/// Evaluates to true when the provider category is "SelfHosted".
/// Self-hosted providers are local applications without web-based login or pricing pages.
/// </summary>
public sealed class IsSelfHostedProviderPredicate : IDecisionPredicate
{
    public string Key => "isSelfHostedProvider";

    public bool Evaluate(DecisionPredicateContext context)
    {
        if (context.State.Properties.TryGetValue("providerCategory", out var categoryObj)
            && categoryObj is string category)
        {
            return string.Equals(category, "SelfHosted", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
