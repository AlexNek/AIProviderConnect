using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed class ValidationOrchestrationService
{
    private readonly AiDefinitionAnalyzer _analyzer;

    private readonly IssueSyncService _issueSync;

    private readonly IOperationLogger _uiLogger;

    private readonly ProviderDefinitionValidator _validator;

    public ValidationOrchestrationService(
        ProviderDefinitionValidator validator,
        IssueSyncService issueSync,
        AiDefinitionAnalyzer analyzer,
        IOperationLogger uiLogger)
    {
        _validator = validator;
        _issueSync = issueSync;
        _analyzer = analyzer;
        _uiLogger = uiLogger;
    }

    public async Task<ValidationResult> RunValidationAsync(
        string manifestPath,
        AppSettings settings,
        IProgress<ValidationProgress> progress,
        int totalProviderCount,
        CancellationToken ct = default)
    {
        var result = new ValidationResult();
        var useLocalProviders = settings.UseLocalProviders;
        var revalidationDays = settings.RevalidationDays;

        var issues = await _validator.ValidateDirectoryAsync(
                         manifestPath,
                         useLocalProviders: useLocalProviders,
                         revalidationDays: revalidationDays,
                         progress: progress,
                         ct: ct);

        foreach (var issue in issues)
            result.Issues.Add(issue);

        result.ValidationStates = await _issueSync.LoadValidationStatesAsync(manifestPath);

        var urlErrors = issues.Count(i => ValidationIssueCodes.UrlErrorCodes.Contains(i.Code));
        var localIssues = issues.Count(i => i.Code == ValidationIssueCodes.UrlLocalHost);
        var structIssues = issues.Count - urlErrors - localIssues;

        result.LastBadIssues = issues.Where(IsBadIssue).ToList();
        result.HasUrlIssues = urlErrors > 0 && _analyzer.IsAvailable;
        result.HasAiSetupNeeded = urlErrors > 0 && !_analyzer.IsAvailable;
        result.UrlErrors = urlErrors;
        result.LocalIssues = localIssues;
        result.StructuralIssues = structIssues;
        result.TotalProviderCount = totalProviderCount;
        result.ValidatedCount =
            result.ValidationStates.Count(v => v.Level > ValidationLevel.NotValidated);

        await _issueSync.SaveIssuesAsync(issues);

        return result;
    }

    private static bool IsBadIssue(ValidationIssue i) =>
        ValidationIssueCodes.UrlErrorCodes.Contains(i.Code)
        || ValidationIssueCodes.StructuralErrorCodes.Contains(i.Code);
}
