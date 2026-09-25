using Microsoft.EntityFrameworkCore;

using ScraperTool.Data.Entities;

namespace ScraperTool.Data.Repositories;

public sealed class ValidationIssueRepository(AppDbContext db)
    : IValidationIssueRepository,
      IReadOnlyValidationIssueRepository,
      IValidationIssueWriteRepository
{
    public async Task DeleteAsync(IReadOnlyList<ValidationIssueEntry> entries)
    {
        foreach (var entry in entries)
        {
            var toRemove = await db.ValidationIssues
                               .Where(i =>
                                   i.FileName == entry.FileName && i.Code == entry.Code
                                                                && i.Message == entry.Message)
                               .FirstOrDefaultAsync();
            if (toRemove is not null)
                db.ValidationIssues.Remove(toRemove);
        }
    }

    public async Task<IReadOnlyList<ValidationIssueEntry>> GetAllAsync() =>
        await db.ValidationIssues.OrderBy(i => i.FileName).ThenBy(i => i.Code).ToListAsync();

    public async Task ReplaceAllAsync(IEnumerable<ValidationIssueEntry> issues)
    {
        await db.ValidationIssues.ExecuteDeleteAsync();
        db.ValidationIssues.AddRange(issues);
    }

    public async Task UpdateSuggestionsAsync(IReadOnlyList<ValidationIssueEntry> updates)
    {
        foreach (var update in updates)
        {
            var entry = await db.ValidationIssues
                            .Where(i =>
                                i.FileName == update.FileName && i.Code == update.Code
                                                              && i.Message == update.Message)
                            .FirstOrDefaultAsync();
            if (entry is null) continue;

            entry.SuggestedValue = update.SuggestedValue ?? entry.SuggestedValue;
            entry.SuggestionReason = update.SuggestionReason ?? entry.SuggestionReason;
            entry.SuggestionSeverity = update.SuggestionSeverity ?? entry.SuggestionSeverity;
            if (update.SuggestionStatus != IssueSuggestionStatus.None)
                entry.SuggestionStatus = update.SuggestionStatus;
            if (update.AiAttemptedAt.HasValue)
                entry.AiAttemptedAt = update.AiAttemptedAt;
        }
    }
}
