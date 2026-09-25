using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Compares the actual model count (from queryModelsEndpoint) against the stored
/// minModelCount (from template parameters). Sets modelCountComparison in state:
/// "matches", "outdated_low", or "outdated_high".
/// </summary>
public sealed class CompareModelCountAction : IDecisionAction
{
    public string Key => "compareModelCount";

    public Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetModelCountFromState(context.State.Properties, out var actualCount))
        {
            context.State.Properties["modelCountComparison"] = "cannot_verify";
            return Task.FromResult(new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["comparison"] = "cannot_verify" },
                DecisionActionStatus.Success,
                "No model count available in state."));
        }

        if (!context.TemplateParameters.TryGetValue("currentValue", out var storedValue)
            || !int.TryParse(storedValue, out var storedCount)
            || storedCount <= 0)
        {
            context.State.Properties["modelCountComparison"] = "cannot_verify";
            return Task.FromResult(new DecisionActionResult(
                null,
                new Dictionary<string, string> { ["comparison"] = "cannot_verify" },
                DecisionActionStatus.Success,
                "No valid stored minModelCount to compare against."));
        }

        var comparison = actualCount >= storedCount
            ? "matches"
            : "outdated_high";

        context.State.Properties["modelCountComparison"] = comparison;
        context.State.Properties["actualModelCount"] = actualCount;
        context.State.Properties["storedModelCount"] = storedCount;

        var evidence = new DecisionData
        {
            Id = $"count-compare-{context.NodeId}",
            Source = "comparison",
            Type = "CountComparison",
            Content = $"actual={actualCount}, stored={storedCount}, result={comparison}",
            CreatedAt = DateTimeOffset.UtcNow,
            ActionId = context.NodeId,
            Metadata = new Dictionary<string, string>
            {
                ["actualCount"] = actualCount.ToString(),
                ["storedCount"] = storedCount.ToString(),
                ["comparison"] = comparison
            }
        };

        return Task.FromResult(new DecisionActionResult(
            new[] { evidence },
            new Dictionary<string, string>
            {
                ["comparison"] = comparison,
                ["actualCount"] = actualCount.ToString(),
                ["storedCount"] = storedCount.ToString()
            },
            DecisionActionStatus.Success));
    }

    /// <summary>
    /// Extracts the model count from state properties, handling both int (set directly by actions)
    /// and string (stored by the executor when copying action result properties).
    /// </summary>
    private static bool TryGetModelCountFromState(
        Dictionary<string, object?> properties,
        out int count)
    {
        count = 0;
        if (!properties.TryGetValue("modelCount", out var countObj) || countObj is null)
            return false;

        if (countObj is int intVal)
        {
            count = intVal;
            return true;
        }

        if (countObj is string strVal && int.TryParse(strVal, out var parsed))
        {
            count = parsed;
            return true;
        }

        return false;
    }
}
