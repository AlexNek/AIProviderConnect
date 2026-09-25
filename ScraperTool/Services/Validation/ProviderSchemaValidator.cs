using ScraperTool.Models;
using System.Text.Json;

using AIProviderConnect.Models;

namespace ScraperTool.Services.Validation;

/// <summary>
/// Default implementation of <see cref="IProviderSchemaValidator"/>.
/// </summary>
public sealed class ProviderSchemaValidator : IProviderSchemaValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly string[] KnownProtocols =
        [
            "Native", "OpenAICompatible", "AnthropicCompatible",
            "GeminiCompatible", "GitHubModelsCompatible", "HybridGateway"
        ];

    public List<ValidationIssue> ValidateSchema(string json, string fileName)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(json))
        {
            issues.Add(new ValidationIssue(fileName, "EmptyFile", "File is empty."));
            return issues;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        CheckRequiredFields(root, fileName, issues);
        CheckProtocol(root, fileName, issues);
        CheckUrlFormat(root, fileName, issues);
        CheckDeserialization(json, fileName, issues);
        CheckMinModelCount(root, fileName, issues);

        return issues;
    }

    private static void CheckDeserialization(
        string json,
        string fileName,
        List<ValidationIssue> issues)
    {
        try
        {
            var def = JsonSerializer.Deserialize<ProviderDefinition>(
                json,
                JsonOptions);
            if (def is null && issues.Count == 0)
            {
                issues.Add(
                    new ValidationIssue(
                        fileName,
                        ValidationIssueCodes.DeserializationNull,
                        "Deserialized to null."));
            }
        }
        catch (JsonException ex)
        {
            Serilog.Log.Warning(ex, "JSON deserialization failed for {FileName}", fileName);
            issues.Add(
                new ValidationIssue(
                    fileName,
                    ValidationIssueCodes.DeserializationError,
                    "JSON deserialization failed."));
        }
    }

    private static void CheckMinModelCount(
        JsonElement root,
        string fileName,
        List<ValidationIssue> issues)
    {
        if (root.TryGetProperty(ProviderJsonFields.MinModelCount, out var countProp)
            && countProp.ValueKind == JsonValueKind.Number
            && countProp.GetInt32() <= 0)
        {
            // Dynamic catalog providers have user-managed model lists; minModelCount 0 is correct.
            if (root.TryGetProperty(ProviderJsonFields.IsDynamicModelCatalog, out var dynProp)
                && dynProp.ValueKind == JsonValueKind.True)
            {
                return;
            }

            var invalidCount = countProp.GetInt32();
            issues.Add(
                new ValidationIssue(
                    fileName,
                    ValidationIssueCodes.MinModelCountInvalid,
                    $"Field 'minModelCount' must be greater than 0 (current: {invalidCount}). A provider must have at least one model — verify this is a valid provider with available models.")
                { Field = ProviderJsonFields.MinModelCount, CurrentValue = invalidCount.ToString() });
        }
    }

    private static void CheckProtocol(
        JsonElement root,
        string fileName,
        List<ValidationIssue> issues)
    {
        if (root.TryGetProperty(ProviderJsonFields.Protocol, out var protocolProp)
            && protocolProp.ValueKind == JsonValueKind.String)
        {
            var protocol = protocolProp.GetString()!;
            if (!KnownProtocols.Contains(protocol))
            {
                issues.Add(
                    new ValidationIssue(
                        fileName,
                        ValidationIssueCodes.InvalidProtocol,
                        $"Unknown protocol '{protocol}'. Expected: {string.Join(", ", KnownProtocols)}"));
            }
        }
    }

    private static void CheckRequiredFields(
        JsonElement root,
        string fileName,
        List<ValidationIssue> issues)
    {
        var required = new[]
                           {
                               ProviderJsonFields.Id, ProviderJsonFields.DisplayName,
                               ProviderJsonFields.Protocol, ProviderJsonFields.Website,
                               ProviderJsonFields.LoginUrl,
                               ProviderJsonFields.ApiPricingUrl,
                               ProviderJsonFields.DocumentationUrl,
                               ProviderJsonFields.BaseUrl
                           };

        foreach (var key in required)
        {
            if (!root.TryGetProperty(key, out var prop) || prop.ValueKind == JsonValueKind.Null)
            {
                issues.Add(
                    new ValidationIssue(
                        fileName,
                        ValidationIssueCodes.MissingRequired,
                        $"Required field '{key}' is missing."));
            }
            else if (prop.ValueKind == JsonValueKind.String
                     && string.IsNullOrWhiteSpace(prop.GetString()))
            {
                issues.Add(
                    new ValidationIssue(
                        fileName,
                        ValidationIssueCodes.EmptyField,
                        $"Required field '{key}' is empty."));
            }
        }

        // Dynamic catalog providers have user-managed model lists; minModelCount is not required.
        var isDynamicCatalog = root.TryGetProperty(ProviderJsonFields.IsDynamicModelCatalog, out var dynProp)
                               && dynProp.ValueKind == JsonValueKind.True;

        if (!isDynamicCatalog
            && (!root.TryGetProperty(ProviderJsonFields.MinModelCount, out var mcProp)
                || mcProp.ValueKind != JsonValueKind.Number))
        {
            issues.Add(
                new ValidationIssue(
                    fileName,
                    ValidationIssueCodes.MissingRequired,
                    $"Required field '{ProviderJsonFields.MinModelCount}' is missing or not a number.")
                {
                    Field = ProviderJsonFields.MinModelCount
                });
        }
    }

    private static void CheckUrlFormat(
        JsonElement root,
        string fileName,
        List<ValidationIssue> issues)
    {
        var isSelfHosted = root.TryGetProperty(ProviderJsonFields.Category, out var catProp)
                           && catProp.ValueKind == JsonValueKind.String
                           && string.Equals(catProp.GetString(), "SelfHosted", StringComparison.OrdinalIgnoreCase);

        foreach (var field in ProviderDefinitionValidator.UrlFields)
        {
            if (root.TryGetProperty(field, out var urlProp)
                && urlProp.ValueKind == JsonValueKind.String)
            {
                var url = urlProp.GetString()!;
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                // "-" (NotApplicable) rules differ per field:
                //   subscriptionPricingUrl — valid for ANY provider. A subscription is optional:
                //     pay-as-you-go and free providers (e.g. research platforms) have no plans page.
                //   apiPricingUrl / loginUrl — valid only for self-hosted providers, which are local
                //     applications without web-based pricing or login.
                if (url == ProviderJsonFields.NotApplicable)
                {
                    if (field == ProviderJsonFields.SubscriptionPricingUrl)
                        continue;

                    var isSelfHostedExemptField = field == ProviderJsonFields.ApiPricingUrl
                                                  || field == ProviderJsonFields.LoginUrl;
                    if (isSelfHostedExemptField && isSelfHosted)
                        continue;

                    issues.Add(
                        new ValidationIssue(
                            fileName,
                            ValidationIssueCodes.InvalidUrl,
                            $"Field '{field}' uses '-' (N/A) but is only allowed for self-hosted providers."));
                    continue;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    issues.Add(
                        new ValidationIssue(
                            fileName,
                            ValidationIssueCodes.InvalidUrl,
                            $"Field '{field}' is not a valid absolute URL: {url}"));
                }
            }
        }
    }
}
