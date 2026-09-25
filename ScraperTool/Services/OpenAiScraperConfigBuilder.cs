using AIProviderConnect.Models;

using ScraperTool.Models;

namespace ScraperTool.Services;

/// <summary>
/// OpenAI-specific scraper configuration builder.
/// </summary>
public sealed class OpenAiScraperConfigBuilder : IScraperConfigBuilder
{
    public ScraperConfiguration Build(ProviderDefinition provider, ProviderResearchMetadata? research) =>
        new()
            {
                ProviderId = provider.Id,
                ApiPricingUrl = research?.ApiPricingUrl,
                TableXPath = "//table",
                RowOffset = 1,
                ModelCellIndex = 0,
                PromptCellIndex = 1,
                CompletionCellIndex = 2,
                PriceUnit = ScraperTool.Models.EPriceUnit.Per1M
            };

    public bool CanBuild(ProviderDefinition provider) =>
        string.Equals(provider.Id, "openai", StringComparison.OrdinalIgnoreCase);
}
