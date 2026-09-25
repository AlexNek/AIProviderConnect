using ScraperTool.Data.Entities;
using ScraperTool.Data.Enums;

namespace ScraperTool.Data.Repositories;

public interface IModelRepository
{
    Task<int> DeleteByDisplayNameAsync(string providerId, string displayName);

    Task<IReadOnlyList<ModelEntry>> GetByProviderAsync(int providerEntryId);

    Task SetValidationStateAsync(int modelEntryId, ModelValidationState state);

    Task UpsertRangeAsync(int providerEntryId, IEnumerable<ModelSaveDto> models);
}
