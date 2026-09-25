using AiCleverness.Abstractions;
using AiCleverness.Models;

namespace ScraperTool.Services.UrlResearch.Validators;

public sealed class RequiredFieldsValidator : IAgentInputValidator
{
    public string Name => "RequiredFields";

    public Task<InputValidationResult> ValidateAsync(
        AgentRequest request,
        IAgentContext context,
        CancellationToken cancellationToken = default)
    {
        var missingFields = new List<string>();

        if (!context.GetProperty<bool>("has_id"))
            missingFields.Add("id");
        if (!context.GetProperty<bool>("has_displayName"))
            missingFields.Add("displayName");
        if (!context.GetProperty<bool>("has_protocol"))
            missingFields.Add("protocol");
        if (!context.GetProperty<bool>("has_baseUrl"))
            missingFields.Add("baseUrl");

        if (missingFields.Count > 0)
            return Task.FromResult(
                new InputValidationResult(
                    false,
                    $"Provider is missing required fields: {string.Join(", ", missingFields)}"));

        return Task.FromResult(new InputValidationResult(true));
    }
}
