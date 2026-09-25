using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Predicates;

/// <summary>
/// Checks whether the provider has a model discovery API.
/// Reads the hasModelDiscoveryApi template parameter (set by the research service
/// from the provider definition).
/// </summary>
public sealed class HasModelDiscoveryApiPredicate : IDecisionPredicate
{
    public string Key => "hasModelDiscoveryApi";

    public bool Evaluate(DecisionPredicateContext context)
    {
        // This predicate is used in condition nodes where template parameters
        // are not directly available. The research service sets this in state
        // before execution, or it's passed via the provider context.
        if (context.State.Properties.TryGetValue("hasModelDiscoveryApi", out var value)
            && value is bool boolValue)
        {
            return boolValue;
        }

        if (context.State.Properties.TryGetValue("hasModelDiscoveryApi", out var strValue)
            && strValue is string str)
        {
            return string.Equals(str, "true", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
