using ScraperTool.Data.Entities;

namespace ScraperTool.Data.Repositories;

public interface ITokenUsageRepository
{
    Task AddAsync(TokenUsageEntry entry);

    Task ClearAsync();

    Task<IReadOnlyList<TokenUsageEntry>> GetAllAsync();
}
