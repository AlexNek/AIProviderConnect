using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Predicates;

/// <summary>
/// Checks whether the provider's model catalog is dynamic (user-managed,
/// unknowable by any external method). When true, the minModelCount tree
/// short-circuits to keep — no verification is possible.
/// </summary>
public sealed class IsDynamicModelCatalogPredicate : IDecisionPredicate
{
    public string Key => "isDynamicModelCatalog";

    public bool Evaluate(DecisionPredicateContext context)
    {
        if (context.State.Properties.TryGetValue("isDynamicModelCatalog", out var value)
            && value is bool boolValue)
        {
            return boolValue;
        }

        if (context.State.Properties.TryGetValue("isDynamicModelCatalog", out var strValue)
            && strValue is string str)
        {
            return string.Equals(str, "true", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
