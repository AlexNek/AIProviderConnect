using AiCleverness.Abstractions;
using AiCleverness.Models.DecisionTree;

namespace ScraperTool.Services.UrlResearch.DecisionTree.Actions;

/// <summary>
/// Initializes verification state properties from template parameters.
/// Sets hasModelDiscoveryApi, hasModelsPageUrl, and providerCategory in state so that
/// condition predicates can check them during verification tree execution.
/// </summary>
public sealed class InitVerificationStateAction : IDecisionAction
{
    public string Key => "initVerificationState";

    public Task<DecisionActionResult> ExecuteAsync(
        DecisionActionContext context,
        CancellationToken cancellationToken = default)
    {
        // Set hasModelDiscoveryApi from template parameters
        if (context.TemplateParameters.TryGetValue("hasModelDiscoveryApi", out var hasApi)
            && string.Equals(hasApi, "true", StringComparison.OrdinalIgnoreCase))
        {
            context.State.Properties["hasModelDiscoveryApi"] = true;
        }
        else
        {
            context.State.Properties["hasModelDiscoveryApi"] = false;
        }

        // Set hasModelsPageUrl from template parameters
        if (context.TemplateParameters.TryGetValue("modelsPageUrl", out var modelsPageUrl)
            && !string.IsNullOrWhiteSpace(modelsPageUrl))
        {
            context.State.Properties["hasModelsPageUrl"] = true;
        }
        else
        {
            context.State.Properties["hasModelsPageUrl"] = false;
        }

        // Set providerCategory from template parameters
        if (context.TemplateParameters.TryGetValue("providerCategory", out var category)
            && !string.IsNullOrWhiteSpace(category))
        {
            context.State.Properties["providerCategory"] = category;
        }

        // Set isDynamicModelCatalog from template parameters
        if (context.TemplateParameters.TryGetValue("isDynamicModelCatalog", out var isDynamic)
            && string.Equals(isDynamic, "true", StringComparison.OrdinalIgnoreCase))
        {
            context.State.Properties["isDynamicModelCatalog"] = true;
        }
        else
        {
            context.State.Properties["isDynamicModelCatalog"] = false;
        }

        return Task.FromResult(new DecisionActionResult(
            null,
            new Dictionary<string, string>
            {
                ["hasModelDiscoveryApi"] = context.State.Properties["hasModelDiscoveryApi"]!.ToString()!,
                ["hasModelsPageUrl"] = context.State.Properties["hasModelsPageUrl"]!.ToString()!,
                ["isDynamicModelCatalog"] = context.State.Properties["isDynamicModelCatalog"]!.ToString()!
            },
            DecisionActionStatus.Success));
    }
}
