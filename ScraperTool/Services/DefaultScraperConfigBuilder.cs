using AIProviderConnect.Models;

using ScraperTool.Models;

namespace ScraperTool.Services;

/// <summary>
/// Default scraper configuration builder for providers without special configuration.
/// </summary>
public sealed class DefaultScraperConfigBuilder : IScraperConfigBuilder
{
    public ScraperConfiguration Build(ProviderDefinition provider, ProviderResearchMetadata? research) =>
        new()
            {
                ProviderId = provider.Id,
                ApiPricingUrl = research?.ApiPricingUrl,
                PriceUnit = ScraperTool.Models.EPriceUnit.Per1M
            };

    public bool CanBuild(ProviderDefinition provider) => true; // Fallback for all providers
}
