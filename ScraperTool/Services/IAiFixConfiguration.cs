namespace ScraperTool.Services;

/// <summary>
/// Exposes the AI service configuration state needed by <see cref="AiUrlFixService"/>
/// to determine whether AI operations can proceed.
/// </summary>
public interface IAiFixConfiguration
{
    bool HasApiKey { get; }

    bool HasProviderSelection { get; }

    bool IsModelConfigured { get; }

    string? PrimaryModel { get; }

    string? FallbackModel { get; }

    string? SelectedProviderId { get; }
}
