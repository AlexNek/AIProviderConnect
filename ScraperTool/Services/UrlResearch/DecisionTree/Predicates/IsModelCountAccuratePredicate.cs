using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Predicates;

/// <summary>
/// Checks whether the model count comparison result is "matches"
/// (actual count >= stored minModelCount).
/// </summary>
public sealed class IsModelCountAccuratePredicate : IDecisionPredicate
{
    public string Key => "isModelCountAccurate";

    public bool Evaluate(DecisionPredicateContext context)
    {
        return context.State.Properties.TryGetValue("modelCountComparison", out var value)
               && value is string str
               && string.Equals(str, "matches", StringComparison.Ordinal);
    }
}
