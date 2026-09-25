using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

using AIProviderConnect.Models;
using AIProviderConnect.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ScraperTool.Data.Entities;
using ScraperTool.Models;
using ScraperTool.Services;

namespace ScraperTool.ViewModels;

using ValidationIssue = ScraperTool.Services.ValidationIssue;

public sealed partial class CheckDataPanelViewModel : SuggestionManagementViewModelBase
{
    private enum PausedOperation
    {
        None,

        StartWork,

        AiFixAll,

        AiRetryFailed,

        AiFixSingle
    }

    private readonly AiDefinitionAnalyzer _analyzer;

    private readonly ProviderCatalog _catalog;

    private readonly IssueSyncService _issueSync;

    private readonly ProviderJsonPatchService _jsonPatch;

    private readonly string _manifestPath;

    private readonly IManifestPathResolver _manifestPathResolver;

    private readonly Action _navigateBack;

    private readonly Action _openAiSetup;

    private readonly ProviderBatchAnalysisService _batchAnalysis;

    private readonly AppSettings _settings;

    private readonly SuggestionProcessingService _suggestionProcessor;

    private readonly IOperationTimer _timer;

    private readonly IOperationLogger _uiLogger;

    private readonly ValidationOrchestrationService _validationOrchestrator;

    private List<AiAnalysisBatch> _deepAnalysisResults = [];

    private CancellationTokenSource? _deepAnalysisCts;

    [ObservableProperty]
    private string _costSummary = string.Empty;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _deepAnalysisCostSummary = string.Empty;

    [ObservableProperty]
    private string _deepAnalysisElapsedTime = "0m 0s";

    [ObservableProperty]
    private bool _hasDeepAnalysisElapsedSummary;

    [ObservableProperty]
    private bool _hasDeepAnalysisResults;

    [ObservableProperty]
    private bool _hasDeepAnalysisTokenUsage;

    [ObservableProperty]
    private bool _isDeepAnalysisBusy;

    [ObservableProperty]
    private string _deepAnalysisStatusText = string.Empty;

    [ObservableProperty]
    private string _deepAnalysisTokenSummary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationIssues))]
    private int _currentIssueCount;

    [ObservableProperty]
    private string _elapsedTime = "0m 0s";

    [ObservableProperty]
    private bool _hasAiSetupNeeded;

    [ObservableProperty]
    private bool _hasElapsedSummary;

    [ObservableProperty]
    private bool _hasTokenUsage;

    [ObservableProperty]
    private bool _hasUrlIssues;

    // ── UI State ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isValidating;

    private List<ValidationIssue> _lastBadIssues = [];

    private ValidationIssue? _pausedIssue;

    private PausedOperation _pausedOperation;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _tokenSummary = string.Empty;

    public bool HasAiSuggestions => AiSuggestions.Count > 0;

    public bool HasFailedItems =>
        _lastBadIssues.Any(i => i.SuggestionStatus == (int)IssueSuggestionStatus.Failed);

    public bool HasValidationIssues => ValidationIssues.Count > 0 || CurrentIssueCount > 0;

    public ReadOnlyObservableCollection<string> Log => _uiLogger.Entries;

    public IOperationTimer Timer => _timer;

    public ObservableCollection<AiSuggestion> DeepAnalysisSuggestions { get; } = [];

    public int TotalProviderCount => ValidationStates.Count;

    public IOperationLogger UiLogger => _uiLogger;

    public int ValidatedCount =>
        ValidationStates.Count(v => v.Level > ValidationLevel.NotValidated);

    public ObservableCollection<ValidationIssue> ValidationIssues { get; } = [];

    public ObservableCollection<ValidationMetadata> ValidationStates { get; } = [];

    public CheckDataPanelViewModel(
        ProviderCatalog catalog,
        string manifestPath,
        ValidationOrchestrationService validationOrchestrator,
        SuggestionProcessingService suggestionProcessor,
        IssueSyncService issueSync,
        AiDefinitionAnalyzer analyzer,
        Action navigateBack,
        Action openAiSetup,
        IClipboardService clipboardService,
        IOperationTimer timer,
        IOperationLogger uiLogger,
        AppSettings settings,
        ProviderJsonPatchService jsonPatch,
        IManifestPathResolver manifestPathResolver,
        ProviderBatchAnalysisService batchAnalysis)
        : base(clipboardService)
    {
        _catalog = catalog;
        _manifestPath = manifestPath;
        _validationOrchestrator = validationOrchestrator;
        _suggestionProcessor = suggestionProcessor;
        _issueSync = issueSync;
        _analyzer = analyzer;
        _navigateBack = navigateBack;
        _openAiSetup = openAiSetup;
        _timer = timer;
        _uiLogger = uiLogger;
        _settings = settings;
        _jsonPatch = jsonPatch;
        _manifestPathResolver = manifestPathResolver;
        _batchAnalysis = batchAnalysis;

        _timer.PropertyChanged += OnTimerPropertyChanged;

        AiSuggestions.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasAiSuggestions));
            };

        DeepAnalysisSuggestions.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasDeepAnalysisResults));
            };
    }

    // ── Initialization ─────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        HasElapsedSummary = false;
        try
        {
            var entries = await _issueSync.LoadIssuesAsync();

            foreach (var entry in entries)
            {
                ValidationIssues.Add(entry);
            }

            _lastBadIssues = entries.Where(IsBadIssue).ToList();

            var urlErrors = entries.Count(i => ValidationIssueCodes.UrlErrorCodes.Contains(i.Code));
            var localIssues = entries.Count(i => i.Code == "UrlLocalHost");
            var structural = entries.Count - urlErrors - localIssues;
            var failed =
                _lastBadIssues.Count(i => i.SuggestionStatus == (int)IssueSuggestionStatus.Failed);

            HasUrlIssues = urlErrors > 0 && _analyzer.IsAvailable;
            HasAiSetupNeeded = urlErrors > 0 && !_analyzer.IsAvailable;

            foreach (var loadError in _catalog.LoadErrors)
            {
                _uiLogger.Add($"  \u26A0 {loadError}");
            }

            if (entries.Count > 0)
            {
                _uiLogger.Add(
                    $"Loaded {entries.Count} issue(s): {structural} structural, {urlErrors} broken URLs, {localIssues} local/private");
                if (failed > 0)
                {
                    _uiLogger.Add(
                        $"  {failed} broken URL(s) previously failed AI fix — use 'Retry Failed'");
                }

                StatusText = $"{entries.Count} issue(s) from previous run ({urlErrors} URLs).";
            }

            CurrentIssueCount = ValidationIssues.Count;
            UpdateResultProps();
        }
        catch (Exception ex)
        {
            _uiLogger.Add($"Failed to load from database: {ex.Message}");
        }

        await LoadValidationStatesAsync();
    }

    // ── Per-item commands for issues grid ───────────────────────────────────────

    [RelayCommand]
    private async Task AiFixSingleIssueAsync(ValidationIssue? issue)
    {
        if (issue is null) return;

        if (!_analyzer.IsAvailable)
        {
            HasAiSetupNeeded = true;
            _openAiSetup();
            return;
        }

        if (!ValidationIssueCodes.UrlErrorCodes.Contains(issue.Code))
        {
            _uiLogger.Add($"Skipping {issue.FileName} — code '{issue.Code}' is not a URL error");
            return;
        }

        _uiLogger.Add($"=== Fixing single issue: {issue.FileName} ({issue.Code}) ===");
        IsBusy = true;
        IsPaused = false;
        _pausedOperation = PausedOperation.AiFixSingle;
        _pausedIssue = issue;
        if (!_timer.IsRunning)
            _timer.Start();
        _cts = new CancellationTokenSource();

        var progress = new Progress<AiUrlFixProgress>(p => _uiLogger.Add(p.Message));

        try
        {
            var result = await _suggestionProcessor.RunAiFixAsync(
                                 [issue],
                             _settings.PrimaryModel,
                             _settings.FallbackModel,
                             progress,
                             _cts.Token);

            foreach (var s in result.Suggestions)
                AiSuggestions.Add(s);

            UpdateTokenDisplay(result);
            UpdateResultProps();

            _uiLogger.Add(
                $"=== Single fix complete: {result.TotalSuggestions} suggestion(s) in {_timer.Elapsed.Minutes}m {_timer.Elapsed.Seconds}s ===");
            StatusText = $"{result.TotalSuggestions} suggestion(s) from single fix";
        }
        catch (OperationCanceledException) when (IsPaused)
        {
            _uiLogger.Add("=== Single fix paused ===");
            StatusText = "Paused — click Resume to continue.";
        }
        catch (OperationCanceledException)
        {
            _uiLogger.Add("=== Single fix cancelled ===");
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            var detail = ex is AIProviderConnect.Exceptions.AiException aiEx
                             ? $"[{aiEx.Code}] {aiEx.Message}"
                             : $"{ex.GetType().Name}: {ex.Message}";
            _uiLogger.Add($"Single fix failed: {detail}");
            StatusText = "Single fix failed — see log.";
        }
        finally
        {
            CleanupAfterOperation();
        }
    }

    [RelayCommand]
    private async Task AiFixUrlsAsync()
    {
        if (!_analyzer.IsAvailable)
        {
            HasAiSetupNeeded = true;
            _openAiSetup();
            return;
        }

        var broken = _suggestionProcessor.GetBrokenUrls(_lastBadIssues);
        if (broken.Count == 0)
        {
            _uiLogger.Add("No broken URLs to fix.");
            return;
        }

        _uiLogger.Add("=== AI URL Fix ===");
        _uiLogger.Add($"  Will send: {broken.Count} broken URL(s) to AI");

        IsBusy = true;
        IsPaused = false;
        _pausedOperation = PausedOperation.AiFixAll;
        AiSuggestions.Clear();
        ResetTokenSummary();
        if (!_timer.IsRunning)
            _timer.Start();
        _cts = new CancellationTokenSource();

        var progress = new Progress<AiUrlFixProgress>(p => _uiLogger.Add(p.Message));

        try
        {
            var result = await _suggestionProcessor.RunAiFixAsync(
                             broken,
                             _settings.PrimaryModel,
                             _settings.FallbackModel,
                             progress,
                             _cts.Token);

            foreach (var s in result.Suggestions)
                AiSuggestions.Add(s);

            UpdateTokenDisplay(result);
            UpdateResultProps();
            LogAiFixComplete(result);
            StatusText =
                $"{result.TotalSuggestions} suggestion(s) in {_timer.Elapsed.Minutes}m {_timer.Elapsed.Seconds}s — approve then Apply";
        }
        catch (OperationCanceledException) when (IsPaused)
        {
            _uiLogger.Add("=== AI URL Fix paused ===");
            StatusText = "Paused — click Resume to continue.";
        }
        catch (OperationCanceledException)
        {
            _uiLogger.Add("=== AI URL Fix cancelled ===");
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            var detail = ex is AIProviderConnect.Exceptions.AiException aiEx
                             ? $"[{aiEx.Code}] {aiEx.Message}"
                             : $"{ex.GetType().Name}: {ex.Message}";
            _uiLogger.Add($"AI fix failed: {detail}");
            StatusText = "AI fix failed — see log.";
        }
        finally
        {
            CleanupAfterOperation();
        }
    }

    [RelayCommand]
    private async Task AiRetryFailedAsync()
    {
        if (!_analyzer.IsAvailable)
        {
            HasAiSetupNeeded = true;
            _openAiSetup();
            return;
        }

        var failed = _suggestionProcessor.GetFailedItems(_lastBadIssues);
        if (failed.Count == 0)
        {
            _uiLogger.Add("No failed items to retry.");
            return;
        }

        _uiLogger.Add($"=== Retrying {failed.Count} failed broken URL(s) ===");
        IsBusy = true;
        IsPaused = false;
        _pausedOperation = PausedOperation.AiRetryFailed;
        if (!_timer.IsRunning)
            _timer.Start();
        _cts = new CancellationTokenSource();

        var progress = new Progress<AiUrlFixProgress>(p => _uiLogger.Add(p.Message));

        try
        {
            var result = await _suggestionProcessor.RunAiFixAsync(
                             failed,
                             _settings.PrimaryModel,
                             _settings.FallbackModel,
                             progress,
                             _cts.Token);

            foreach (var s in result.Suggestions)
                AiSuggestions.Add(s);

            UpdateTokenDisplay(result);
            UpdateResultProps();

            var stillFailed = _lastBadIssues.Count(i =>
                i.SuggestionStatus == (int)IssueSuggestionStatus.Failed);
            var dismissed = _lastBadIssues.Count(i =>
                i.SuggestionStatus == (int)IssueSuggestionStatus.Dismissed);

            if (stillFailed == 0 && dismissed > 0)
            {
                _uiLogger.Add(
                    $"=== Retry complete: {result.TotalSuggestions} new suggestion(s), {dismissed} resolved as not applicable ===");
            }
            else
            {
                _uiLogger.Add(
                    $"=== Retry complete: {result.TotalSuggestions} new suggestion(s), {stillFailed} still failed ===");
            }

            StatusText = stillFailed == 0
                ? $"All retry items resolved in {_timer.Elapsed.Minutes}m {_timer.Elapsed.Seconds}s."
                : $"{result.TotalSuggestions} suggestion(s) from retry in {_timer.Elapsed.Minutes}m {_timer.Elapsed.Seconds}s, {stillFailed} still failed.";
        }
        catch (OperationCanceledException) when (IsPaused)
        {
            _uiLogger.Add("=== Retry paused ===");
            StatusText = "Paused — click Resume to continue.";
        }
        catch (OperationCanceledException)
        {
            _uiLogger.Add("=== Retry cancelled ===");
            StatusText = "Cancelled.";
        }
        finally
        {
            CleanupAfterOperation();
        }
    }

    [RelayCommand]
    private async Task ApplyApprovedSuggestionsAsync()
    {
        var approved = AiSuggestions.Where(s => s.IsApproved && !s.IsRejected).ToList();
        var rejected = AiSuggestions.Where(s => s.IsRejected && !s.IsApproved).ToList();

        if (approved.Count == 0 && rejected.Count == 0)
        {
            _uiLogger.Add("No approved changes to apply.");
            return;
        }

        Serilog.Log.Information(
            "User applied {ApprovedCount} approved and {RejectedCount} rejected suggestions",
            approved.Count,
            rejected.Count);
        IsBusy = true;
        StatusText = "Applying approved changes...";

        try
        {
            await _issueSync.ApplyApprovedSuggestionsAsync(
                approved,
                rejected,
                _manifestPath,
                _settings,
                _lastBadIssues,
                ValidationIssues,
                msg => _uiLogger.Add(msg),
                status => StatusText = status);

            foreach (var s in approved)
                AiSuggestions.Remove(s);
            foreach (var s in rejected)
                AiSuggestions.Remove(s);

            await LoadValidationStatesAsync();

            var remainingUrlIssues =
                _lastBadIssues.Count(i => ValidationIssueCodes.UrlErrorCodes.Contains(i.Code));
            HasUrlIssues = remainingUrlIssues > 0 && _analyzer.IsAvailable;
            HasAiSetupNeeded = remainingUrlIssues > 0 && !_analyzer.IsAvailable;
            UpdateResultProps();
            StatusText = $"{_lastBadIssues.Count} issue(s) remaining ({remainingUrlIssues} URLs).";
        }
        catch (Exception ex)
        {
            _uiLogger.Add($"Error applying changes: {ex.GetType().Name}: {ex.Message}");
            StatusText = "Error applying changes.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _cts?.Cancel();
        IsPaused = false;
        _pausedOperation = PausedOperation.None;
        _pausedIssue = null;
        _uiLogger.Add("=== Operation cancelled by user ===");
    }

    [RelayCommand]
    private void ClearResults()
    {
        Serilog.Log.Information("User cleared validation results");
        ValidationIssues.Clear();
        AiSuggestions.Clear();
        DeepAnalysisSuggestions.Clear();
        _deepAnalysisResults = [];
        _lastBadIssues = [];
        HasUrlIssues = false;
        HasAiSetupNeeded = false;
        HasElapsedSummary = false;
        HasDeepAnalysisElapsedSummary = false;
        HasDeepAnalysisResults = false;
        HasDeepAnalysisTokenUsage = false;
        ResetTokenSummary();
        DeepAnalysisTokenSummary = string.Empty;
        DeepAnalysisCostSummary = string.Empty;
        _uiLogger.Clear();
        StatusText = "Cleared";
        DeepAnalysisStatusText = string.Empty;
        UpdateResultProps();
        _navigateBack();
    }

    [RelayCommand]
    private void CopyLog()
    {
        if (_uiLogger.Entries.Count == 0)
        {
            return;
        }

        Serilog.Log.Information(
            "User copied validation log to clipboard ({LineCount} lines)",
            _uiLogger.Entries.Count);
        ClipboardService.SetText(string.Join(Environment.NewLine, _uiLogger.Entries));
        StatusText = "Log copied to clipboard.";
    }

    private static bool IsBadIssue(ValidationIssue i) =>
        ValidationIssueCodes.UrlErrorCodes.Contains(i.Code)
        || ValidationIssueCodes.StructuralErrorCodes.Contains(i.Code);

    private async Task LoadValidationStatesAsync()
    {
        ValidationStates.Clear();
        var states = await _issueSync.LoadValidationStatesAsync(_manifestPath);
        foreach (var s in states)
            ValidationStates.Add(s);
        OnPropertyChanged(nameof(ValidatedCount));
        OnPropertyChanged(nameof(TotalProviderCount));
    }

    private void LogAiFixComplete(SuggestionResult result)
    {
        _uiLogger.Add("=== AI URL Fix complete ===");
        _uiLogger.Add($"  Elapsed: {_timer.Elapsed.Minutes}m {_timer.Elapsed.Seconds}s");
        _uiLogger.Add($"  Model: {result.UsedModelName}");
        _uiLogger.Add(
            $"  Total tokens: {result.TotalPromptTokens} prompt + {result.TotalCompletionTokens} completion = {result.TotalPromptTokens + result.TotalCompletionTokens} total");
        if (!string.IsNullOrEmpty(result.CostSourceLabel))
            _uiLogger.Add($"  Cost: ${result.TotalCost:F4}{result.CostSourceLabel}");
        if (result.FailedCount > 0)
            _uiLogger.Add($"  {result.FailedCount} item(s) failed — use 'Retry Failed' to retry");
    }

    private void OnTimerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IOperationTimer.ElapsedTime))
        {
            ElapsedTime = _timer.ElapsedTime;
        }
    }

    [RelayCommand]
    private void OpenAiSetup()
    {
        HasAiSetupNeeded = true;
        _openAiSetup();
    }

    [RelayCommand]
    private void PauseOperation()
    {
        _cts?.Cancel();
        IsPaused = true;
        _uiLogger.Add("=== Operation paused by user — click Resume to continue ===");
    }

    private void ResetTokenSummary()
    {
        TokenSummary = string.Empty;
        CostSummary = string.Empty;
        HasTokenUsage = false;
    }

    [RelayCommand]
    private async Task ResumeOperationAsync()
    {
        IsPaused = false;
        var op = _pausedOperation;
        var issue = _pausedIssue;
        _pausedOperation = PausedOperation.None;
        _pausedIssue = null;

        _uiLogger.Add("=== Resuming paused operation ===");
        _timer.Resume();

        switch (op)
        {
            case PausedOperation.StartWork:
                await StartWorkAsync();
                break;
            case PausedOperation.AiFixAll:
                await AiFixUrlsAsync();
                break;
            case PausedOperation.AiRetryFailed:
                await AiRetryFailedAsync();
                break;
            case PausedOperation.AiFixSingle when issue is not null:
                await AiFixSingleIssueAsync(issue);
                break;
        }
    }

    // ── Commands ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task StartWorkAsync()
    {
        if (string.IsNullOrWhiteSpace(_manifestPath) || !Directory.Exists(_manifestPath))
        {
            _uiLogger.Add("Please configure the provider manifests path in Settings first.");
            StatusText = "Manifest path not set.";
            return;
        }

        Serilog.Log.Information("User started provider data validation");
        _uiLogger.Clear();
        ValidationIssues.Clear();
        ValidationStates.Clear();
        AiSuggestions.Clear();
        _uiLogger.Add("=== Check Provider Data started ===");
        IsBusy = true;
        IsPaused = false;
        _pausedOperation = PausedOperation.StartWork;
        IsValidating = true;
        StatusText = "Checking provider data...";
        HasAiSetupNeeded = false;
        if (!_timer.IsRunning)
            _timer.Start();
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<ValidationProgress>(p =>
                {
                    StatusText = $"Checking {p.FileName} — {p.Field}...";

                    if (p.Stage == ValidationStage.CheckPassed)
                        _uiLogger.Add($"  \u2713 {p.FileName} — {p.Field}: {p.ResultMessage}");
                    else if (p.Stage == ValidationStage.CheckFailed)
                        _uiLogger.Add($"  \u2716 {p.FileName} — {p.Field}: {p.ResultMessage}");

                    if (p.Issue is not null)
                        ValidationIssues.Add(p.Issue);

                    CurrentIssueCount = ValidationIssues.Count;
                });

            var result = await _validationOrchestrator.RunValidationAsync(
                             _manifestPath,
                             _settings,
                             progress,
                             _catalog.All.Count(),
                             _cts.Token);

            foreach (var issue in result.Issues)
            {
                if (!ValidationIssues.Contains(issue))
                    ValidationIssues.Add(issue);
            }

            foreach (var state in result.ValidationStates)
                ValidationStates.Add(state);

            _lastBadIssues = result.LastBadIssues;
            HasUrlIssues = result.HasUrlIssues;
            HasAiSetupNeeded = result.HasAiSetupNeeded;

            if (result.Issues.Count == 0)
            {
                _uiLogger.Add(
                    $"\u2713 All {_catalog.All.Count()} files passed — every URL is reachable.");
                StatusText =
                    $"All {_catalog.All.Count()} files passed in {_timer.Elapsed.Minutes}m {_timer.Elapsed.Seconds}s. ({result.ValidatedCount}/{result.TotalProviderCount} validated)";
            }
            else
            {
                _uiLogger.Add(
                    $"\u2716 Found {result.Issues.Count} issue(s): {result.StructuralIssues} structural, {result.UrlErrors} broken URLs, {result.LocalIssues} local/private.");
                StatusText =
                    $"{result.Issues.Count} issue(s) found in {_timer.Elapsed.Minutes}m {_timer.Elapsed.Seconds}s — {result.LastBadIssues.Count} to fix. ({result.ValidatedCount}/{result.TotalProviderCount} validated)";
            }

            UpdateResultProps();
        }
        catch (OperationCanceledException) when (IsPaused)
        {
            _uiLogger.Add("=== Check paused ===");
            StatusText = "Paused — click Resume to continue.";
        }
        catch (OperationCanceledException)
        {
            _uiLogger.Add("\u2716 Check cancelled by user.");
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            _uiLogger.Add($"\u2716 Check failed: {ex.GetType().Name}: {ex.Message}");
            StatusText = "Check failed.";
        }
        finally
        {
            IsValidating = false;
            CleanupAfterOperation();
        }
    }

    // ── Deep AI Analysis ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RunDeepAnalysisAsync()
    {
        if (!_analyzer.IsAvailable)
        {
            _uiLogger.Add("AI is not configured. Open AI Setup to configure an API key.");
            DeepAnalysisStatusText = "AI not configured.";
            return;
        }

        Serilog.Log.Information("User started deep AI analysis from Check panel");
        DeepAnalysisSuggestions.Clear();
        _deepAnalysisResults = [];
        _uiLogger.Add("=== Deep AI Analysis started ===");
        IsDeepAnalysisBusy = true;
        DeepAnalysisStatusText = "Analyzing providers with AI...";
        var analysisTimer = new OperationTimer();
        analysisTimer.Start();
        _deepAnalysisCts = new CancellationTokenSource();

        try
        {
            var result = await _batchAnalysis.RunAsync(
                _deepAnalysisCts.Token,
                (idx, total, name, elapsed) =>
                    DeepAnalysisStatusText = $"[{idx + 1}/{total}] {name} — {elapsed.Minutes}m {elapsed.Seconds}s elapsed",
                s => DeepAnalysisSuggestions.Add(s),
                _uiLogger,
                analysisTimer);

            DeepAnalysisTokenSummary =
                $"{result.TotalPromptTokens:N0} prompt + {result.TotalCompletionTokens:N0} completion = {result.TotalPromptTokens + result.TotalCompletionTokens:N0} total";
            DeepAnalysisCostSummary = $"${result.InputCost:F4} input + ${result.OutputCost:F4} output = ${result.TotalCost:F4}{result.CostNote}";
            HasDeepAnalysisTokenUsage = result.TotalPromptTokens + result.TotalCompletionTokens > 0;

            DeepAnalysisStatusText = result.SuggestionCount > 0
                ? $"{result.SuggestionCount} suggestion(s) in {analysisTimer.Elapsed.Minutes}m {analysisTimer.Elapsed.Seconds}s — review and approve below."
                : $"AI analysis complete in {analysisTimer.Elapsed.Minutes}m {analysisTimer.Elapsed.Seconds}s. No suggestions.";
        }
        catch (OperationCanceledException)
        {
            _uiLogger.Add("=== Deep AI Analysis cancelled ===");
            DeepAnalysisStatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            _uiLogger.Add($"Deep AI analysis error: {ex.GetType().Name}: {ex.Message}");
            DeepAnalysisStatusText = "Analysis failed — see log.";
        }
        finally
        {
            analysisTimer.Stop();
            DeepAnalysisElapsedTime = analysisTimer.ElapsedTime;
            HasDeepAnalysisElapsedSummary = true;
            _deepAnalysisCts?.Dispose();
            _deepAnalysisCts = null;
            IsDeepAnalysisBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyDeepAnalysisSuggestionsAsync()
    {
        var approved = DeepAnalysisSuggestions.Where(s => s.IsApproved && !s.IsRejected).ToList();
        if (approved.Count == 0)
        {
            _uiLogger.Add("No approved changes to apply.");
            return;
        }

        Serilog.Log.Information(
            "User applied {SuggestionCount} approved deep analysis suggestions",
            approved.Count);
        IsDeepAnalysisBusy = true;
        DeepAnalysisStatusText = "Applying approved changes...";

        try
        {
            var resolvedPath = _manifestPathResolver.Resolve();
            var (updatedFileCount, _) = ApplyPatchAndReloadCore(
                approved, resolvedPath, _catalog, _jsonPatch, _uiLogger.Add);

            foreach (var s in approved)
                DeepAnalysisSuggestions.Remove(s);

            DeepAnalysisStatusText = $"Applied changes to {updatedFileCount} provider file(s).";
            _uiLogger.Add(
                $"Approved {approved.Count} suggestion(s) across {updatedFileCount} file(s).");
        }
        catch (Exception ex)
        {
            _uiLogger.Add($"Error applying changes: {ex.GetType().Name}: {ex.Message}");
            DeepAnalysisStatusText = "Error applying changes.";
        }
        finally
        {
            IsDeepAnalysisBusy = false;
        }
    }

    [RelayCommand]
    private void SelectAllDeepAnalysisSuggestions()
    {
        foreach (var s in DeepAnalysisSuggestions)
        {
            s.IsApproved = true;
            s.IsRejected = false;
        }
    }

    [RelayCommand]
    private void DeselectAllDeepAnalysisSuggestions()
    {
        foreach (var s in DeepAnalysisSuggestions)
        {
            s.IsApproved = false;
            s.IsRejected = false;
        }
    }

    [RelayCommand]
    private void CancelDeepAnalysis()
    {
        _deepAnalysisCts?.Cancel();
        _uiLogger.Add("=== Deep AI Analysis cancelled by user ===");
    }

    private void UpdateResultProps()
    {
        OnPropertyChanged(nameof(HasValidationIssues));
        OnPropertyChanged(nameof(HasAiSuggestions));
        OnPropertyChanged(nameof(HasFailedItems));
    }

    private void UpdateTokenDisplay(SuggestionResult result)
    {
        TokenSummary =
            $"{result.TotalPromptTokens:N0} prompt + {result.TotalCompletionTokens:N0} completion = {result.TotalPromptTokens + result.TotalCompletionTokens:N0} total";
        CostSummary = $"{result.UsedModelName}: ${result.TotalCost:F4}{result.CostSourceLabel}";
        HasTokenUsage = result.TotalPromptTokens + result.TotalCompletionTokens > 0;
    }

    private void CleanupAfterOperation()
    {
        if (IsPaused)
            _timer.Pause();
        else
        {
            _timer.Stop();
            ElapsedTime = _timer.ElapsedTime;
            HasElapsedSummary = true;
        }

        if (!IsPaused)
        {
            _pausedOperation = PausedOperation.None;
            _pausedIssue = null;
            _cts?.Dispose();
            _cts = null;
        }

        IsBusy = false;
    }
}
