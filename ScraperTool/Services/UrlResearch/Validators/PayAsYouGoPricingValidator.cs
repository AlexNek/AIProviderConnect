using AiCleverness.Abstractions;
using AiCleverness.Models;

namespace ScraperTool.Services.UrlResearch.Validators;

public sealed class PayAsYouGoPricingValidator : IAgentInputValidator
{
    public string Name => "PayAsYouGoPricing";

    public Task<InputValidationResult> ValidateAsync(
        AgentRequest request,
        IAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var payAsYouGo = context.GetProperty<string>("pay_as_you_go_description");
        var hasModels = context.GetProperty<bool>("has_models");

        if (hasModels && string.IsNullOrWhiteSpace(payAsYouGo))
            return Task.FromResult(
                new InputValidationResult(
                    false,
                    "Provider has models but no pay-as-you-go pricing description"));

        return Task.FromResult(new InputValidationResult(true));
    }
}
