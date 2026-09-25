using System.IO;

using AIProviderConnect.Services;

using ScraperTool.Data;
using ScraperTool.Data.Entities;
using ScraperTool.Data.Repositories;
using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed class IssueSyncService
{
    private readonly ProviderCatalog _catalog;

    private readonly IValidationIssueRepository _issueRepo;

    private readonly ProviderJsonPatchService _jsonPatch;

    private readonly IValidationMetadataService _metadataService;

    private readonly IUnitOfWork _uow;

    private readonly ProviderDefinitionValidator _validator;

    public IssueSyncService(
        IValidationIssueRepository issueRepo,
        IUnitOfWork uow,
        ProviderCatalog catalog,
        ProviderJsonPatchService jsonPatch,
        ProviderDefinitionValidator validator,
        IValidationMetadataService metadataService)
    {
        _issueRepo = issueRepo;
        _uow = uow;
        _catalog = catalog;
        _jsonPatch = jsonPatch;
        _validator = validator;
        _metadataService = metadataService;
    }

    public async Task ApplyApprovedSuggestionsAsync(
        IEnumerable<AiSuggestion> approved,
        IEnumerable<AiSuggestion> rejected,
        string manifestPath,
        AppSettings settings,
        List<ValidationIssue> lastBadIssues,
        ICollection<ValidationIssue> validationIssues,
        Action<string> onLog,
        Action<string> onStatus)
    {
        var now = DateTime.UtcNow;
        var approvedList = approved.ToList();
        var rejectedList = rejected.ToList();

        if (approvedList.Count > 0)
        {
            var patch = _jsonPatch.Apply(approvedList);
            foreach (var err in patch.Errors)
                onLog($"  {err}");

            var modifiedProviderIds = approvedList.Select(s => s.ProviderId).Distinct().ToList();
            var errorsBefore = _catalog.LoadErrors.Count;
            var reloaded = _catalog.ReloadFromDisk(manifestPath, modifiedProviderIds);
            if (reloaded > 0)
                onLog($"  Reloaded {reloaded} provider(s) into catalog.");

            foreach (var error in _catalog.LoadErrors.Skip(errorsBefore))
                onLog($"  Reload warning: {error}");

            onLog(
                $"Approved {approvedList.Count} suggestion(s) across {patch.UpdatedFileCount} file(s).");
            onStatus($"Applied changes to {patch.UpdatedFileCount} provider file(s).");

            foreach (var providerId in modifiedProviderIds)
            {
                var filePath = Path.Combine(manifestPath, $"{providerId}.json");
                if (File.Exists(filePath))
                {
                    await _validator.ValidateFileAsync(
                        filePath,
                        useLocalProviders: settings.UseLocalProviders,
                        revalidationDays: settings.RevalidationDays);
                    onLog($"  Re-validated {providerId}.json — sidecar updated");
                }
            }
        }

        var toDelete = new List<ValidationIssueEntry>();
        foreach (var s in approvedList)
        {
            var match = lastBadIssues.FirstOrDefault(i =>
                Path.GetFileNameWithoutExtension(i.FileName).Equals(
                    s.ProviderId,
                    StringComparison.OrdinalIgnoreCase) &&
                MatchesField(i, s.Field));
            if (match is not null)
            {
                toDelete.Add(
                    new ValidationIssueEntry
                        {
                            FileName = match.FileName,
                            Code = match.Code,
                            Message = match.Message,
                            SavedAt = now
                        });
                lastBadIssues.Remove(match);
                var uiMatch = validationIssues.FirstOrDefault(i =>
                    string.Equals(i.FileName, match.FileName, StringComparison.OrdinalIgnoreCase) &&
                    i.Code == match.Code &&
                    i.Message == match.Message);
                if (uiMatch is not null)
                    validationIssues.Remove(uiMatch);
            }
        }

        if (toDelete.Count > 0)
        {
            await _issueRepo.DeleteAsync(toDelete);
            await _uow.SaveChangesAsync();
        }

        var dbUpdates = new List<ValidationIssueEntry>();
        foreach (var s in rejectedList)
        {
            var match = lastBadIssues.FirstOrDefault(i =>
                Path.GetFileNameWithoutExtension(i.FileName).Equals(
                    s.ProviderId,
                    StringComparison.OrdinalIgnoreCase) &&
                MatchesField(i, s.Field));
            if (match is not null)
            {
                match.SuggestionStatus = (int)IssueSuggestionStatus.Rejected;
                dbUpdates.Add(
                    new ValidationIssueEntry
                        {
                            FileName = match.FileName,
                            Code = match.Code,
                            Message = match.Message,
                            SavedAt = now,
                            SuggestionStatus = IssueSuggestionStatus.Rejected,
                            AiAttemptedAt = match.AiAttemptedAt
                        });
            }
        }

        if (dbUpdates.Count > 0)
        {
            await _issueRepo.UpdateSuggestionsAsync(dbUpdates);
            await _uow.SaveChangesAsync();
        }
    }

    public async Task<List<ValidationIssue>> LoadIssuesAsync()
    {
        var entries = (await _issueRepo.GetAllAsync()).ToList();
        return entries.Select(MapEntryToIssue).ToList();
    }

    public async Task<List<ValidationMetadata>> LoadValidationStatesAsync(string manifestPath)
    {
        var states = new List<ValidationMetadata>();
        if (string.IsNullOrWhiteSpace(manifestPath) || !Directory.Exists(manifestPath))
            return states;

        var jsonFiles = Directory.GetFiles(manifestPath, "*.json")
            .Where(f => !f.EndsWith(".validation.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var jsonFile in jsonFiles)
        {
            var metadata = _metadataService.Read(jsonFile);
            if (metadata != null)
                states.Add(metadata);
        }

        return states;
    }

    public async Task SaveIssuesAsync(IEnumerable<ValidationIssue> issues)
    {
        await _issueRepo.ReplaceAllAsync(
            issues.Select(i => new ValidationIssueEntry
                                   {
                                       FileName = i.FileName,
                                       Code = i.Code,
                                       Message = i.Message,
                                       SavedAt = DateTime.UtcNow
                                   }));
        await _uow.SaveChangesAsync();
    }

    private static ValidationIssue MapEntryToIssue(ValidationIssueEntry entry) =>
        new(entry.FileName, entry.Code, entry.Message)
            {
                CurrentValue = entry.CurrentValue,
                Field = entry.Field,
                SuggestedValue = entry.SuggestedValue,
                SuggestionReason = entry.SuggestionReason,
                SuggestionSeverity = entry.SuggestionSeverity,
                SuggestionStatus = (int)entry.SuggestionStatus,
                AiAttemptedAt = entry.AiAttemptedAt
            };

    /// <summary>
    /// Matches a validation issue to a suggestion by field name.
    /// Uses the issue's Field property when available, falling back to
    /// message content matching for issues that lack a Field value.
    /// </summary>
    private static bool MatchesField(ValidationIssue issue, string suggestionField)
    {
        if (string.IsNullOrWhiteSpace(suggestionField))
            return false;

        if (!string.IsNullOrWhiteSpace(issue.Field))
            return issue.Field.Equals(suggestionField, StringComparison.OrdinalIgnoreCase);

        return issue.Message.Contains(suggestionField);
    }
}
