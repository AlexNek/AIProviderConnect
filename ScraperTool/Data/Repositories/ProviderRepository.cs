using Microsoft.EntityFrameworkCore;

using ScraperTool.Data.Entities;

namespace ScraperTool.Data.Repositories;

public sealed class ProviderRepository(AppDbContext db) : IProviderRepository
{
    public async Task<IReadOnlyList<ProviderEntry>> GetAllAsync() =>
        await db.Providers.Include(p => p.Models).ToListAsync();

    public async Task<ProviderEntry?> GetByProviderIdAsync(string providerId) =>
        await db.Providers.FirstOrDefaultAsync(p => p.ProviderId == providerId);

    public async Task TouchFetchedAtAsync(string providerId)
    {
        var entry = await db.Providers.FirstOrDefaultAsync(p => p.ProviderId == providerId);
        if (entry is not null)
            entry.LastFetchedAt = DateTime.UtcNow;
    }

    public async Task<ProviderEntry> UpsertAsync(string providerId, string displayName)
    {
        var entry = await db.Providers.FirstOrDefaultAsync(p => p.ProviderId == providerId);
        if (entry is null)
        {
            entry = new ProviderEntry
                        {
                            ProviderId = providerId,
                            DisplayName = displayName,
                            LastFetchedAt = DateTime.UtcNow
                        };
            db.Providers.Add(entry);
        }
        else
        {
            entry.DisplayName = displayName;
        }

        return entry;
    }
}
