using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ScraperTool.Models;
using ScraperTool.Services;

namespace ScraperTool.ViewModels;

public abstract partial class SuggestionManagementViewModelBase : ObservableObject
{
    private readonly IClipboardService _clipboardService;

    public virtual ObservableCollection<AiSuggestion> AiSuggestions { get; } = [];

    protected IClipboardService ClipboardService => _clipboardService;

    protected SuggestionManagementViewModelBase(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    [RelayCommand]
    protected void DeselectAllSuggestions()
    {
        foreach (var s in AiSuggestions)
        {
            s.IsApproved = false;
            s.IsRejected = false;
        }
    }

    [RelayCommand]
    protected void SelectAllSuggestions()
    {
        foreach (var s in AiSuggestions)
        {
            s.IsApproved = true;
            s.IsRejected = false;
        }
    }

    /// <summary>
    /// Applies approved suggestions as JSON patches, reloads the affected providers
    /// into the catalog, and logs patch/reload results.
    /// </summary>
    /// <returns>The number of files updated and the count of new load errors.</returns>
    protected (int UpdatedFileCount, int NewErrorCount) ApplyPatchAndReloadCore(
        List<AiSuggestion> approved,
        string resolvedManifestPath,
        AIProviderConnect.Services.ProviderCatalog catalog,
        ProviderJsonPatchService jsonPatch,
        Action<string> log)
    {
        var patch = jsonPatch.Apply(approved);
        foreach (var err in patch.Errors)
            log($"  {err}");
        foreach (var group in approved.GroupBy(s => s.ProviderId))
            log($"  [{group.Key}] Updated {group.Count()} field(s).");

        var modifiedProviderIds = approved.Select(s => s.ProviderId).Distinct().ToList();
        var errorsBefore = catalog.LoadErrors.Count;
        var reloaded = catalog.ReloadFromDisk(resolvedManifestPath, modifiedProviderIds);
        if (reloaded > 0)
            log($"  Reloaded {reloaded} provider(s) into catalog.");
        foreach (var error in catalog.LoadErrors.Skip(errorsBefore))
            log($"  Reload warning: {error}");

        return (patch.UpdatedFileCount, catalog.LoadErrors.Count - errorsBefore);
    }
}
