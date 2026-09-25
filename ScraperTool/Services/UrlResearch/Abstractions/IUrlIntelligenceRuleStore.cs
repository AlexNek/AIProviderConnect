using ScraperTool.Services.UrlFix;

namespace ScraperTool.Services.UrlResearch.Abstractions;

/// <summary>
/// Read access to the editable URL-intelligence rules
/// (<c>Config/url-intelligence-rules.json</c>). Editing/persistence of the
/// rules stays on the metadata provider for the settings UI.
/// </summary>
public interface IUrlIntelligenceRuleStore
{
    /// <summary>
    /// Checks whether a specific rule kind is enabled. Rules missing from
    /// the config are treated as enabled.
    /// </summary>
    Task<bool> IsRuleEnabledAsync(
        UrlIntelligenceRuleKind kind,
        CancellationToken ct = default);
}
