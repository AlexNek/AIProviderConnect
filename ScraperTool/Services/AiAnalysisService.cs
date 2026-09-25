using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed class AiAnalysisService
{
    private AppSettings _settings;

    public string FallbackModel => _settings.FallbackModel;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_settings.ApiKey);

    public bool HasProviderSelection => !string.IsNullOrWhiteSpace(_settings.SelectedProviderId);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.ApiKey)
        && !string.IsNullOrWhiteSpace(_settings.SelectedProviderId);

    public bool IsModelConfigured =>
        !string.IsNullOrWhiteSpace(_settings.PrimaryModel)
        || !string.IsNullOrWhiteSpace(_settings.FallbackModel);

    public string PrimaryModel => _settings.PrimaryModel;

    public string SelectedProviderId => _settings.SelectedProviderId ?? string.Empty;

    public AiAnalysisService(AppSettings settings)
    {
        _settings = settings;
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
    }
}
