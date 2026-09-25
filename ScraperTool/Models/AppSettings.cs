using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using AiCleverness.Models;

using WebTools.NET.Abstractions;

namespace ScraperTool.Models;

public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AIProviderConnect",
        "scraper-settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
                                                                    {
                                                                        WriteIndented = true,
                                                                        Converters =
                                                                            {
                                                                                new
                                                                                    JsonStringEnumConverter()
                                                                            }
                                                                    };

    public string ApiKey { get; set; } = string.Empty;

    public EBrowserEngine BrowserEngine { get; set; } = EBrowserEngine.CloakBrowser;

    public string DatabasePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "scraper.db");

    public string FallbackModel { get; set; } = string.Empty;

    public decimal? FallbackModelCompletionPrice { get; set; }

    public decimal? FallbackModelPromptPrice { get; set; }

    public string ManifestPath { get; set; } = string.Empty;

    public string PrimaryModel { get; set; } = string.Empty;

    public decimal? PrimaryModelCompletionPrice { get; set; }

    public decimal? PrimaryModelPromptPrice { get; set; }

    // Number of days after which a manually validated provider is re-validated
    // to catch site changes (URLs, endpoints, etc.). 0 = never re-validate.
    public int RevalidationDays { get; set; } = 30;

    public string SelectedProviderId { get; set; } = string.Empty;

    // When false, local provider URL reachability checks will be skipped.
    public bool UseLocalProviders { get; set; }

    /// <summary>
    /// Transcript generation mode. <see cref="ETranscriptMode.None"/> disables transcripts;
    /// <see cref="ETranscriptMode.Transcript"/> writes redacted Markdown transcripts;
    /// <see cref="ETranscriptMode.DebugTranscript"/> bypasses redaction and records all
    /// available content. Default is <see cref="ETranscriptMode.None"/>.
    /// </summary>
    public ETranscriptMode TranscriptMode { get; set; }

    /// <summary>
    /// Transcript storage directory pinned to the application executable placement.
    /// </summary>
    public string TranscriptDirectory => Path.Combine(AppContext.BaseDirectory, "transcripts");

    /// <summary>
    /// Adds transcript-related parameters to the dictionary when transcripts are enabled.
    /// </summary>
    public void ApplyTranscriptParameters(IDictionary<string, object> parameters)
    {
        if (TranscriptMode == ETranscriptMode.None)
            return;

        parameters[AgentPropertyKeys.MarkdownTranscriptDirectory] = TranscriptDirectory;
        if (TranscriptMode == ETranscriptMode.DebugTranscript)
            parameters[AgentPropertyKeys.MarkdownTranscriptDebug] = true;
    }

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath))
            return new AppSettings();

        try
        {
            var encrypted = File.ReadAllText(FilePath);
            var decrypted = ProtectedData.Unprotect(
                Convert.FromBase64String(encrypted),
                null,
                DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decrypted);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                           ?? new AppSettings();
            OverrideApiKeyFromEnvironment(settings);
            return settings;
        }
        catch
        {
            try
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                OverrideApiKeyFromEnvironment(settings);
                return settings;
            }
            catch
            {
                return new AppSettings();
            }
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json),
            null,
            DataProtectionScope.CurrentUser);
        File.WriteAllText(FilePath, Convert.ToBase64String(encrypted));
    }

    private static void OverrideApiKeyFromEnvironment(AppSettings settings)
    {
        var envKey = Environment.GetEnvironmentVariable("AI_PROVIDER_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
            settings.ApiKey = envKey.Trim();
    }
}
