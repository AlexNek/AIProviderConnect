using ScraperTool.Models;
using AiCleverness.Abstractions;
using AiCleverness.Models;

namespace ScraperTool.Services.UrlResearch.Validators;

public sealed class PricingPageFormatValidator : IAgentInputValidator
{
    public string Name => "PricingPageFormat";

    public Task<InputValidationResult> ValidateAsync(
        AgentRequest request,
        IAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var pricingUrl = context.GetProperty<string>("pricing_url");
        if (string.IsNullOrWhiteSpace(pricingUrl)
            || pricingUrl == ProviderJsonFields.NotApplicable)
            return Task.FromResult(new InputValidationResult(true));

        if (!Uri.TryCreate(pricingUrl, UriKind.Absolute, out _))
            return Task.FromResult(
                new InputValidationResult(false, $"Pricing URL '{pricingUrl}' is not valid"));

        return Task.FromResult(new InputValidationResult(true));
    }
}
