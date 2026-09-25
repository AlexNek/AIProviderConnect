using AiCleverness.Abstractions;
using AiCleverness.Models;

namespace ScraperTool.Services.UrlResearch.Validators;

public sealed class CurrencyFormatValidator : IAgentInputValidator
{
    public string Name => "CurrencyFormat";

    public Task<InputValidationResult> ValidateAsync(
        AgentRequest request,
        IAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var currency = context.GetProperty<string>("currency");
        if (!string.IsNullOrWhiteSpace(currency) && currency.Length != 3)
            return Task.FromResult(
                new InputValidationResult(
                    false,
                    $"Currency '{currency}' should be a 3-letter ISO code (e.g., USD, EUR)"));

        return Task.FromResult(new InputValidationResult(true));
    }
}
