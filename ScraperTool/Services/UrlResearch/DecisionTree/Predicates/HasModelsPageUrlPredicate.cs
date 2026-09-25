using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Predicates;

/// <summary>
/// Checks whether a models page URL is available for fetching.
/// Reads from state properties set by the research service before execution.
/// </summary>
public sealed class HasModelsPageUrlPredicate : IDecisionPredicate
{
    public string Key => "hasModelsPageUrl";

    public bool Evaluate(DecisionPredicateContext context)
    {
        if (context.State.Properties.TryGetValue("hasModelsPageUrl", out var value))
        {
            if (value is bool boolValue)
                return boolValue;

            if (value is string str)
                return string.Equals(str, "true", StringComparison.OrdinalIgnoreCase);
        }

        // Also check if modelsPageUrl is set in template parameters
        // (the research service copies relevant provider data to state before execution)
        return false;
    }
}
