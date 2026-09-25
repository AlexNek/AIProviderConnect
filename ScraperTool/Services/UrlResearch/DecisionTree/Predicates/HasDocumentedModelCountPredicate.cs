using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Predicates;

/// <summary>
/// Checks whether the documented-endpoint probe established a model count.
/// <para>
/// A provider that documents no public catalog is the normal case, not an error, so the
/// probing action reports "nothing found" as a completed step and the tree routes on this
/// predicate instead of treating an absent catalog as a failed action.
/// </para>
/// </summary>
public sealed class HasDocumentedModelCountPredicate : IDecisionPredicate
{
    public string Key => "hasDocumentedModelCount";

    public bool Evaluate(DecisionPredicateContext context)
        => context.State.Properties.TryGetValue("modelCountMethod", out var value)
           && value is string method
           && string.Equals(method, "documented-endpoint", StringComparison.Ordinal);
}
