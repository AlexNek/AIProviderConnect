namespace ScraperTool.Services;

/// <summary>
/// Adapts <see cref="AiDefinitionAnalyzer"/> to <see cref="IAiFixConfiguration"/>.
/// </summary>
public sealed class AiFixConfigurationAdapter : IAiFixConfiguration
{
    private readonly AiDefinitionAnalyzer _analyzer;

    public bool HasApiKey => _analyzer.AiService.HasApiKey;

    public bool HasProviderSelection => _analyzer.AiService.HasProviderSelection;

    public bool IsModelConfigured => _analyzer.IsModelConfigured;

    public string? PrimaryModel => _analyzer.AiService.PrimaryModel;

    public string? FallbackModel => _analyzer.AiService.FallbackModel;

    public string? SelectedProviderId => _analyzer.AiService.SelectedProviderId;

    public AiFixConfigurationAdapter(AiDefinitionAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }
}
