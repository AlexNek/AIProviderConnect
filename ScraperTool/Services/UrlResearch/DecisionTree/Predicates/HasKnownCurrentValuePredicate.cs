using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Predicates;

/// <summary>
/// Checks whether a known current value has been recorded in state
/// (typically by the recordCurrentFact action at the start of a tree).
/// </summary>
public sealed class HasKnownCurrentValuePredicate : IDecisionPredicate
{
    public string Key => "hasKnownCurrentValue";

    public bool Evaluate(DecisionPredicateContext context)
    {
        return context.State.Properties.TryGetValue("knownCurrentValue", out var value)
               && value is string str
               && !string.IsNullOrWhiteSpace(str);
    }
}
