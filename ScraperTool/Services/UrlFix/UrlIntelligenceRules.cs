using ScraperTool.Services.UrlResearch.Abstractions;

namespace ScraperTool.Services.UrlFix;

public sealed class UrlIntelligenceRules
{
    private readonly IUrlIntelligenceRuleStore _ruleStore;

    public UrlIntelligenceRules(IUrlIntelligenceRuleStore ruleStore)
    {
        _ruleStore = ruleStore;
    }

    public async Task<bool> IsEnabledAsync(UrlIntelligenceRuleKind kind)
    {
        return await _ruleStore.IsRuleEnabledAsync(kind);
    }
}
