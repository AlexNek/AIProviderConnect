using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ScraperTool.Data;
using ScraperTool.Data.Entities;
using ScraperTool.Data.Repositories;

using Serilog;

namespace ScraperTool.ViewModels;

public sealed partial class AiUsageHistoryViewModel : ObservableObject
{
    private readonly Action _navigateBack;

    private readonly ITokenUsageRepository _tokenRepo;

    private readonly IUnitOfWork _uow;

    private List<TokenUsageEntry> _allEntries = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _operationFilter = "All";

    [ObservableProperty]
    private string _providerFilter = "All";

    [ObservableProperty]
    private string _summaryLine = string.Empty;

    public ObservableCollection<TokenUsageEntry> Entries { get; } = [];

    public string[] OperationFilters { get; } = ["All", "AiUrlFix", "AiRetryFailed"];

    public List<string> ProviderFilters { get; private set; } = ["All"];

    public AiUsageHistoryViewModel(
        ITokenUsageRepository tokenRepo,
        IUnitOfWork uow,
        Action navigateBack)
    {
        _tokenRepo = tokenRepo;
        _uow = uow;
        _navigateBack = navigateBack;
    }

    public async Task InitializeAsync()
    {
        await LoadDataAsync();
    }

    private void ApplyFilter()
    {
        Entries.Clear();
        var filtered = _allEntries.AsEnumerable();
        if (OperationFilter != "All")
            filtered = filtered.Where(e => e.Operation == OperationFilter);
        if (ProviderFilter != "All")
            filtered = filtered.Where(e => e.ProviderName == ProviderFilter);
        foreach (var e in filtered)
            Entries.Add(e);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _tokenRepo.ClearAsync();
            await _uow.SaveChangesAsync();
            _allEntries.Clear();
            Entries.Clear();
            SummaryLine = "History cleared.";
            Log.Information("User cleared AI usage history");
        }
        catch (Exception ex)
        {
            SummaryLine = $"Failed to clear: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void GoBack() => _navigateBack();

    private async Task LoadDataAsync()
    {
        try
        {
            _allEntries = (await _tokenRepo.GetAllAsync()).ToList();
            ProviderFilters =
                    ["All", .. _allEntries.Select(e => e.ProviderName).Distinct().Order()];
            OnPropertyChanged(nameof(ProviderFilters));
            ApplyFilter();
            UpdateStats();
        }
        catch (Exception ex)
        {
            SummaryLine = $"Failed to load history: {ex.Message}";
        }
    }

    partial void OnOperationFilterChanged(string value)
    {
        ApplyFilter();
        UpdateStats();
    }

    partial void OnProviderFilterChanged(string value)
    {
        ApplyFilter();
        UpdateStats();
    }

    private void UpdateStats()
    {
        var list = Entries.ToList();
        if (list.Count == 0)
        {
            SummaryLine = "No usage records.";
            return;
        }

        var totalPrompt = list.Sum(e => e.PromptTokens);
        var totalCompletion = list.Sum(e => e.CompletionTokens);
        var totalCost = list.Sum(e => e.Cost);
        var models = list.Select(e => e.ModelName).Distinct();
        var perProvider = list.GroupBy(e => e.ProviderName)
            .Select(g => $"{g.Key}: ${g.Sum(e => e.Cost):F4}")
            .ToList();
        SummaryLine =
            $"{list.Count} call(s) — {totalPrompt:N0} prompt + {totalCompletion:N0} completion = {totalPrompt + totalCompletion:N0} tokens — ${totalCost:F4} total — [{string.Join(" | ", perProvider)}] — models: {string.Join(", ", models)}";
    }
}
