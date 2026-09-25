using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Predicates;

/// <summary>
/// Evaluates to true when the last verify-reachable action succeeded.
/// </summary>
public sealed class LastVerifySucceededPredicate : IDecisionPredicate
{
    public string Key => "lastVerifySucceeded";

    public bool Evaluate(DecisionPredicateContext context)
    {
        if (!context.State.Properties.TryGetValue("lastVerifySucceeded", out var value))
        {
            return false;
        }

        return value is bool succeeded && succeeded;
    }
}
