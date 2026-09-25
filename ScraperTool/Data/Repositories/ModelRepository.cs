using Microsoft.EntityFrameworkCore;

using ScraperTool.Data.Entities;
using ScraperTool.Data.Enums;

namespace ScraperTool.Data.Repositories;

public sealed class ModelRepository(AppDbContext db) : IModelRepository
{
    public async Task<int> DeleteByDisplayNameAsync(string providerId, string displayName)
    {
        var entries = await db.Models
                          .Include(m => m.Provider)
                          .Where(m =>
                              m.Provider.ProviderId == providerId && m.DisplayName == displayName)
                          .ToListAsync();

        if (entries.Count == 0)
            return 0;

        db.Models.RemoveRange(entries);
        return entries.Count;
    }

    public async Task<IReadOnlyList<ModelEntry>> GetByProviderAsync(int providerEntryId) =>
        await db.Models.Where(m => m.ProviderEntryId == providerEntryId).ToListAsync();

    public async Task SetValidationStateAsync(int modelEntryId, ModelValidationState state)
    {
        var entry = await db.Models.FindAsync(modelEntryId);
        if (entry is null) return;

        entry.ValidationState = state;
        entry.LastValidatedAt = DateTime.UtcNow;
    }

    public async Task UpsertRangeAsync(int providerEntryId, IEnumerable<ModelSaveDto> models)
    {
        var existing = await db.Models
                           .Where(m => m.ProviderEntryId == providerEntryId)
                           .ToListAsync();

        var byModelId = existing
            .GroupBy(m => m.ModelId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var now = DateTime.UtcNow;

        foreach (var dto in models)
        {
            if (!byModelId.TryGetValue(dto.ModelId, out var entries))
            {
                var entry = CreateEntry(providerEntryId, dto, now);
                db.Models.Add(entry);
                byModelId[dto.ModelId] = [entry];
                continue;
            }

            // Always update the first existing entry for this model — don't create duplicates
            var match = entries[0];
            UpdateEntry(match, dto, now);

            // Remove any stale duplicates that may have been created by earlier logic
            if (entries.Count > 1)
            {
                db.Models.RemoveRange(entries.Skip(1));
                entries.RemoveRange(1, entries.Count - 1);
            }
        }
    }

    private static ModelEntry CreateEntry(int providerEntryId, ModelSaveDto dto, DateTime now) =>
        new()
            {
                ModelId = dto.ModelId,
                DisplayName = dto.DisplayName,
                Description = dto.Description,
                OwnedBy = dto.OwnedBy,
                ContextWindow = dto.ContextWindow,
                PromptPrice = dto.PromptPrice,
                CompletionPrice = dto.CompletionPrice,
                ProviderEntryId = providerEntryId,
                CreatedAt = now,
                UpdatedAt = now
            };

    private static void UpdateEntry(ModelEntry entry, ModelSaveDto dto, DateTime now)
    {
        entry.DisplayName = dto.DisplayName;
        entry.Description = dto.Description;
        entry.OwnedBy = dto.OwnedBy;
        entry.ContextWindow = dto.ContextWindow;
        entry.PromptPrice = dto.PromptPrice;
        entry.CompletionPrice = dto.CompletionPrice;
        entry.UpdatedAt = now;
    }
}
