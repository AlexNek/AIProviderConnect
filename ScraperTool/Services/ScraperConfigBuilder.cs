using AIProviderConnect.Models;

using ScraperTool.Models;

namespace ScraperTool.Services;

/// <summary>
/// Composite scraper configuration builder that delegates to specialized builders.
/// Follows the Chain of Responsibility pattern.
/// </summary>
public sealed class ScraperConfigBuilder : IScraperConfigBuilder
{
    private readonly IReadOnlyList<IScraperConfigBuilder> _builders;

    public ScraperConfigBuilder(IEnumerable<IScraperConfigBuilder> builders)
    {
        _builders = builders.ToList();
    }

    public ScraperConfiguration Build(ProviderDefinition provider, ProviderResearchMetadata? research)
    {
        // Find the first builder that can handle this provider
        var builder = _builders.FirstOrDefault(b => b.CanBuild(provider))
                      ?? throw new InvalidOperationException(
                          $"No scraper config builder found for provider '{provider.Id}'");

        return builder.Build(provider, research);
    }

    public bool CanBuild(ProviderDefinition provider) => true; // Always can build via fallback
}
