using Microsoft.EntityFrameworkCore;

using ScraperTool.Data.Entities;

namespace ScraperTool.Data.Repositories;

public sealed class TokenUsageRepository(AppDbContext db) : ITokenUsageRepository
{
    public async Task AddAsync(TokenUsageEntry entry)
    {
        db.TokenUsages.Add(entry);
    }

    public async Task ClearAsync()
    {
        await db.TokenUsages.ExecuteDeleteAsync();
    }

    public async Task<IReadOnlyList<TokenUsageEntry>> GetAllAsync() =>
        await db.TokenUsages.OrderByDescending(t => t.CreatedAt).ToListAsync();
}
