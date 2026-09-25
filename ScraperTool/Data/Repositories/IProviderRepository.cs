using ScraperTool.Data.Entities;

namespace ScraperTool.Data.Repositories;

public interface IProviderRepository
{
    Task<IReadOnlyList<ProviderEntry>> GetAllAsync();

    Task<ProviderEntry?> GetByProviderIdAsync(string providerId);

    Task TouchFetchedAtAsync(string providerId);

    Task<ProviderEntry> UpsertAsync(string providerId, string displayName);
}
