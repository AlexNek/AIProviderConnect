using AiCleverness.Abstractions;
using AiCleverness.Models;

namespace ScraperTool.Services.UrlResearch.Validators;

public sealed class MinModelsPerProviderValidator : IAgentInputValidator
{
    public string Name => "MinModelsPerProvider";

    public Task<InputValidationResult> ValidateAsync(
        AgentRequest request,
        IAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var minModelCount = context.GetProperty<int?>("min_model_count");
        var modelCount = context.GetProperty<int?>("model_count");

        if (minModelCount.HasValue && modelCount.HasValue && modelCount < minModelCount)
            return Task.FromResult(
                new InputValidationResult(
                    false,
                    $"Provider has {modelCount} models but minimum is {minModelCount}"));

        return Task.FromResult(new InputValidationResult(true));
    }
}
