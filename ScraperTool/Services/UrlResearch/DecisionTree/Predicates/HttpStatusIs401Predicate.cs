using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Predicates;

/// <summary>
/// Evaluates to true when the last HTTP response status was 401 (Unauthorized).
/// Checks the lastHttpStatus property in state.
/// </summary>
public sealed class HttpStatusIs401Predicate : IDecisionPredicate
{
    public string Key => "httpStatusIs401";

    public bool Evaluate(DecisionPredicateContext context)
    {
        if (!context.State.Properties.TryGetValue("lastHttpStatus", out var statusObj))
        {
            return false;
        }

        if (statusObj is int statusCode)
        {
            return statusCode == 401;
        }

        if (statusObj is string statusString
            && int.TryParse(statusString, out var parsedStatus))
        {
            return parsedStatus == 401;
        }

        return false;
    }
}
