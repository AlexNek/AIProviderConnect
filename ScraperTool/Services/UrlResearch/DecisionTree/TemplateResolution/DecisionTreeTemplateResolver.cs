using System.Text;

namespace ScraperTool.Services.UrlResearch.DecisionTree.TemplateResolution;

/// <summary>
/// Resolves template placeholders ({provider}, {fieldKind}, {region}, etc.)
/// in decision-tree JSON before loading. Simple deterministic string substitution,
/// no LLM involved.
/// </summary>
public sealed class DecisionTreeTemplateResolver
{
    /// <summary>
    /// Resolves all template placeholders in the tree JSON.
    /// Placeholders use the format {key} and are replaced with the corresponding
    /// value from the template parameters dictionary.
    /// </summary>
    /// <param name="treeJson">Raw decision-tree JSON with placeholders.</param>
    /// <param name="templateParameters">Key-value pairs for substitution.</param>
    /// <returns>JSON string with all known placeholders replaced.</returns>
    public string Resolve(string treeJson, IReadOnlyDictionary<string, string> templateParameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(treeJson);

        if (templateParameters is null || templateParameters.Count == 0)
            return treeJson;

        var result = treeJson;
        foreach (var (key, value) in templateParameters)
        {
            var placeholder = "{" + key + "}";
            result = result.Replace(placeholder, value, StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// Builds the standard template parameters from provider context values.
    /// </summary>
    /// <param name="providerName">Provider display name.</param>
    /// <param name="providerUrl">Provider website URL.</param>
    /// <param name="fieldKind">Field kind (e.g. subscriptionPricingUrl).</param>
    /// <param name="baseUrl">Provider API base URL (may be null).</param>
    /// <param name="region">Provider region (may be null).</param>
    /// <param name="currentValue">Current field value from provider definition (may be null).</param>
    /// <param name="searchQueryTemplate">Optional search query template (may be null).</param>
    /// <param name="hasModelDiscoveryApi">Whether the provider has a model discovery API.</param>
    /// <param name="modelsPageUrl">Models page URL for fallback fetching (may be null).</param>
    /// <param name="providerCategory">Provider category (e.g. SelfHosted, may be null).</param>
    /// <param name="isDynamicModelCatalog">Whether the provider's model catalog is dynamic (user-managed, unknowable).</param>
    /// <param name="siblingUrls">Optional dictionary of related field URLs from the provider definition (e.g. apiPricingUrl, documentationUrl) that the scan action can use as additional link sources.</param>
    /// <returns>Dictionary of template parameters ready for Resolve().</returns>
    public static Dictionary<string, string> BuildParameters(
        string providerName,
        string providerUrl,
        string fieldKind,
        string? baseUrl = null,
        string? region = null,
        string? currentValue = null,
        string? searchQueryTemplate = null,
        bool hasModelDiscoveryApi = false,
        string? modelsPageUrl = null,
        string? providerCategory = null,
        bool isDynamicModelCatalog = false,
        IReadOnlyDictionary<string, string>? siblingUrls = null)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["providerName"] = providerName ?? string.Empty,
            ["providerUrl"] = providerUrl ?? string.Empty,
            ["fieldKind"] = fieldKind ?? string.Empty,
            ["baseUrl"] = baseUrl ?? string.Empty,
            ["region"] = region ?? string.Empty,
            ["currentValue"] = string.IsNullOrWhiteSpace(currentValue) ? "none" : currentValue,
            ["hasModelDiscoveryApi"] = hasModelDiscoveryApi ? "true" : "false",
            ["isDynamicModelCatalog"] = isDynamicModelCatalog ? "true" : "false"
        };

        if (!string.IsNullOrWhiteSpace(searchQueryTemplate))
        {
            parameters["searchQueryTemplate"] = searchQueryTemplate;
        }

        if (!string.IsNullOrWhiteSpace(modelsPageUrl))
        {
            parameters["modelsPageUrl"] = modelsPageUrl;
        }

        if (!string.IsNullOrWhiteSpace(providerCategory))
        {
            parameters["providerCategory"] = providerCategory;
        }

        if (siblingUrls is { Count: > 0 })
        {
            parameters["siblingUrls"] = string.Join(";",
                siblingUrls.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        return parameters;
    }
}
