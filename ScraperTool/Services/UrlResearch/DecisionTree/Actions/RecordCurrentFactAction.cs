using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Records a known current value fact from validation into the decision state.
/// This is typically the URL value already stored in the provider definition.
/// </summary>
public sealed class RecordCurrentFactAction : IDecisionAction
{
    public string Key => "recordCurrentFact";

    public Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        // Check if there's a current value in the template parameters
        if (context.TemplateParameters.TryGetValue("currentValue", out var currentValue)
            && !string.IsNullOrWhiteSpace(currentValue))
        {
            // Record the fact in state
            context.State.Properties["knownCurrentValue"] = currentValue;

            // Produce evidence data
            var fact = new DecisionData
            {
                Id = $"fact-{context.NodeId}",
                Source = "validation",
                Type = "KnownFact",
                Content = currentValue,
                CreatedAt = DateTimeOffset.UtcNow,
                ActionId = context.NodeId
            };

            return Task.FromResult(new DecisionActionResult(
                new[] { fact },
                new Dictionary<string, string> { ["knownCurrentValue"] = currentValue },
                DecisionActionStatus.Success));
        }

        // No known value — proceed without a fact
        return Task.FromResult(new DecisionActionResult(
            null,
            null,
            DecisionActionStatus.Success));
    }
}
