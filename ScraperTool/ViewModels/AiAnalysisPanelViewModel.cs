using System.Collections.ObjectModel;
using System.ComponentModel;

using AIProviderConnect.Models;
using AIProviderConnect.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ScraperTool.Models;
using ScraperTool.Services;

namespace ScraperTool.ViewModels;

public sealed partial class AiAnalysisPanelViewModel : SuggestionManagementViewModelBase
{
    private readonly AiDefinitionAnalyzer _analyzer;

    private readonly ProviderBatchAnalysisService _batchAnalysis;

    private readonly ProviderCatalog _catalog;

    private readonly ProviderJsonPatchService _jsonPatch;

    private readonly IManifestPathResolver _manifestPathResolver;

    private readonly Action _navigateBack;

    private readonly IOperationTimer _timer;

    private readonly IOperationLogger _uiLogger;

    private List<AiAnalysisBatch> _analysisResults = [];

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _elapsedTime = "0m 0s";

    [ObservableProperty]
    private bool _hasElapsedSummary;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _statusText = "Ready";

    public new ObservableCollection<AiSuggestion> AiSuggestions { get; } = [];

    public bool HasAiSuggestions => AiSuggestions.Count > 0;

    public ReadOnlyObservableCollection<string> Log => _uiLogger.Entries;

    public IOperationTimer Timer => _timer;

    public IOperationLogger UiLogger => _uiLogger;

    public AiAnalysisPanelViewModel(
        AiDefinitionAnalyzer analyzer,
        ProviderCatalog catalog,
        ProviderBatchAnalysisService batchAnalysis,
        Action navigateBack,
        IManifestPathResolver manifestPathResolver,
        ProviderJsonPatchService jsonPatch,
        IClipboardService clipboardService,
        IOperationTimer? timer = null,
        IOperationLogger? logger = null)
        : base(clipboardService)
    {
        _analyzer = analyzer;
        _catalog = catalog;
        _batchAnalysis = batchAnalysis;
        _navigateBack = navigateBack;
        _manifestPathResolver = manifestPathResolver;
        _jsonPatch = jsonPatch;
        _timer = timer ?? new OperationTimer();
        _uiLogger = logger ?? new OperationLogger();

        _timer.PropertyChanged += OnTimerPropertyChanged;

        AiSuggestions.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasAiSuggestions));
            };
    }

    [RelayCommand]
    private async Task ApplyApprovedSuggestionsAsync()
    {
        var approved = AiSuggestions.Where(s => s.IsApproved && !s.IsRejected).ToList();
        if (approved.Count == 0)
        {
            _uiLogger.Add("No approved changes to apply.");
            return;
        }

        Serilog.Log.Information(
            "User applied {SuggestionCount} approved AI analysis suggestions",
            approved.Count);
        IsBusy = true;
        StatusText = "Applying approved changes...";

        try
        {
            var manifestPath = _manifestPathResolver.Resolve();
            var (updatedFileCount, _) = ApplyPatchAndReloadCore(
                approved, manifestPath, _catalog, _jsonPatch, _uiLogger.Add);

            StatusText = $"Applied changes to {updatedFileCount} provider file(s).";
            _uiLogger.Add(
                $"Approved {approved.Count} suggestion(s) across {updatedFileCount} file(s).");
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
        _uiLogger.Add("=== Analysis cancelled by user ===");
    }

    [RelayCommand]
    private void ClearResults()
    {
        Serilog.Log.Information("User cleared AI analysis results");
        AiSuggestions.Clear();
        _analysisResults = [];
        _uiLogger.Clear();
        HasElapsedSummary = false;
        StatusText = "Cleared";
        UpdateResultProps();
        _navigateBack();
    }

    [RelayCommand]
    private void CopyLog()
    {
        if (_uiLogger.Entries.Count == 0) return;
        Serilog.Log.Information(
            "User copied AI analysis log to clipboard ({LineCount} lines)",
            _uiLogger.Entries.Count);
        ClipboardService.SetText(string.Join(Environment.NewLine, _uiLogger.Entries));
        StatusText = "Log copied to clipboard.";
    }

    private void OnTimerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IOperationTimer.ElapsedTime))
        {
            OnPropertyChanged(nameof(IOperationTimer.ElapsedTime));
        }
    }

    [RelayCommand]
    private void PauseOperation()
    {
        _cts?.Cancel();
        IsPaused = true;
        _uiLogger.Add("=== Analysis paused by user — click Resume to continue ===");
    }

    [RelayCommand]
    private async Task ResumeOperationAsync()
    {
        IsPaused = false;
        _uiLogger.Add("=== Resuming analysis ===");
        _timer.Resume();
        await StartWorkAsync();
    }

    [RelayCommand]
    private async Task StartWorkAsync()
    {
        if (!_analyzer.IsAvailable)
        {
            _uiLogger.Add("AI is not configured. Open AI Setup to configure an API key.");
            StatusText = "AI not configured.";
            return;
        }

        Serilog.Log.Information("User started AI analysis");
        _uiLogger.Clear();
        AiSuggestions.Clear();
        _analysisResults = [];
        _uiLogger.Add("=== AI Analysis started ===");
        IsBusy = true;
        IsPaused = false;
        StatusText = "Analyzing providers with AI...";
        if (!_timer.IsRunning)
            _timer.Start();
        _cts = new CancellationTokenSource();

        try
        {
            var result = await _batchAnalysis.RunAsync(
                _cts.Token,
                (idx, total, name, elapsed) =>
                    StatusText = $"[{idx + 1}/{total}] {name} — {elapsed.Minutes}m {elapsed.Seconds}s elapsed",
                s => AiSuggestions.Add(s),
                _uiLogger,
                _timer);

            StatusText = result.SuggestionCount > 0
                             ? $"{result.SuggestionCount} suggestion(s) in {_timer.Elapsed.Minutes}m {_timer.Elapsed.Seconds}s — review and approve below."
                             : $"AI analysis complete in {_timer.Elapsed.Minutes}m {_timer.Elapsed.Seconds}s. No suggestions.";
            UpdateResultProps();
        }
        catch (OperationCanceledException) when (IsPaused)
        {
            _uiLogger.Add("=== Analysis paused ===");
            StatusText = "Paused — click Resume to continue.";
        }
        catch (OperationCanceledException)
        {
            _uiLogger.Add("=== Analysis cancelled ===");
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            _uiLogger.Add($"AI analysis error: {ex.GetType().Name}: {ex.Message}");
            StatusText = "AI analysis failed — see log.";
        }
        finally
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
                _cts?.Dispose();
                _cts = null;
            }

            IsBusy = false;
        }
    }

    private void UpdateResultProps()
    {
        OnPropertyChanged(nameof(HasAiSuggestions));
    }
}
