using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using ScraperTool.Models;
using ScraperTool.Services;

using Serilog;

using WebTools.NET.Abstractions;

namespace ScraperTool.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private bool _isCloakBrowserEngine;

    [ObservableProperty]
    private bool _isPlaywrightEngine = true;

    [ObservableProperty]
    private string _manifestPath = string.Empty;

    [ObservableProperty]
    private int _revalidationDays = 30;

    [ObservableProperty]
    private bool _useLocalProviders;

    [ObservableProperty]
    private bool _isTranscriptModeNone = true;

    [ObservableProperty]
    private bool _isTranscriptModeTranscript;

    [ObservableProperty]
    private bool _isTranscriptModeDebugTranscript;

    public SettingsViewModel(
        AppSettings settings,
        string currentManifestPath)
    {
        _settings = settings;
        _databasePath = settings.DatabasePath;
        _manifestPath = !string.IsNullOrWhiteSpace(settings.ManifestPath)
                            ? settings.ManifestPath
                            : currentManifestPath;
        _useLocalProviders = settings.UseLocalProviders;
        _revalidationDays = settings.RevalidationDays;
        _isPlaywrightEngine = settings.BrowserEngine == EBrowserEngine.Playwright;
        _isCloakBrowserEngine = settings.BrowserEngine == EBrowserEngine.CloakBrowser;
        _isTranscriptModeNone = settings.TranscriptMode == ETranscriptMode.None;
        _isTranscriptModeTranscript = settings.TranscriptMode == ETranscriptMode.Transcript;
        _isTranscriptModeDebugTranscript = settings.TranscriptMode == ETranscriptMode.DebugTranscript;
    }

    [RelayCommand]
    private void BrowseDatabasePath()
    {
        Log.Information("User browsed for database path");
        var dialog = new Microsoft.Win32.OpenFolderDialog
                         {
                             Title = "Select database folder",
                             InitialDirectory = System.IO.Path.GetDirectoryName(DatabasePath)
                         };

        if (dialog.ShowDialog() == true)
            DatabasePath = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseManifestPath()
    {
        Log.Information("User browsed for manifest path");
        var dialog = new Microsoft.Win32.OpenFolderDialog
                         {
                             Title =
                                 "Select the ai-providers folder (AIProviderConnectLib/ai-providers/)"
                         };

        if (dialog.ShowDialog() == true)
            ManifestPath = dialog.FolderName;
    }

    partial void OnIsCloakBrowserEngineChanged(bool value)
    {
        if (value) IsPlaywrightEngine = false;
    }

    partial void OnIsPlaywrightEngineChanged(bool value)
    {
        if (value) IsCloakBrowserEngine = false;
    }

    partial void OnIsTranscriptModeNoneChanged(bool value)
    {
        if (value)
        {
            IsTranscriptModeTranscript = false;
            IsTranscriptModeDebugTranscript = false;
        }
    }

    partial void OnIsTranscriptModeTranscriptChanged(bool value)
    {
        if (value)
        {
            IsTranscriptModeNone = false;
            IsTranscriptModeDebugTranscript = false;
        }
    }

    partial void OnIsTranscriptModeDebugTranscriptChanged(bool value)
    {
        if (value)
        {
            IsTranscriptModeNone = false;
            IsTranscriptModeTranscript = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        Log.Information("User saved settings");
        var dbPath = DatabasePath;
        if (!dbPath.EndsWith("scraper.db", StringComparison.OrdinalIgnoreCase))
            dbPath = System.IO.Path.Combine(dbPath, "scraper.db");
        _settings.DatabasePath = dbPath;
        _settings.ManifestPath = ManifestPath;
        _settings.UseLocalProviders = UseLocalProviders;
        _settings.RevalidationDays = RevalidationDays;
        _settings.BrowserEngine = IsCloakBrowserEngine
                                      ? EBrowserEngine.CloakBrowser
                                      : EBrowserEngine.Playwright;
        if (IsTranscriptModeNone)
            _settings.TranscriptMode = ETranscriptMode.None;
        else if (IsTranscriptModeTranscript)
            _settings.TranscriptMode = ETranscriptMode.Transcript;
        else if (IsTranscriptModeDebugTranscript)
            _settings.TranscriptMode = ETranscriptMode.DebugTranscript;
        _settings.Save();
    }
}
