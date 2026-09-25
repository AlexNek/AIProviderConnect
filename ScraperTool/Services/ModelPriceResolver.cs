using ScraperTool.Models;

namespace ScraperTool.Services;

public sealed class ModelPriceResolver
{
    private readonly AppSettings _settings;

    public ModelPriceResolver(AppSettings settings)
    {
        _settings = settings;
    }

    public Task<(decimal InputPrice, decimal OutputPrice, PriceSource Source)> ResolveAsync(
        string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return Task.FromResult((0m, 0m, PriceSource.Unknown));

        if (string.Equals(modelId, _settings.PrimaryModel, StringComparison.OrdinalIgnoreCase))
        {
            if (_settings.PrimaryModelPromptPrice.HasValue
                && _settings.PrimaryModelCompletionPrice.HasValue)
                return Task.FromResult(
                    (_settings.PrimaryModelPromptPrice.Value,
                        _settings.PrimaryModelCompletionPrice.Value, PriceSource.Confirmed));
            if (_settings.PrimaryModelPromptPrice.HasValue)
                return Task.FromResult(
                    (_settings.PrimaryModelPromptPrice.Value,
                        _settings.PrimaryModelPromptPrice.Value, PriceSource.Confirmed));
        }

        if (string.Equals(modelId, _settings.FallbackModel, StringComparison.OrdinalIgnoreCase))
        {
            if (_settings.FallbackModelPromptPrice.HasValue
                && _settings.FallbackModelCompletionPrice.HasValue)
                return Task.FromResult(
                    (_settings.FallbackModelPromptPrice.Value,
                        _settings.FallbackModelCompletionPrice.Value, PriceSource.Confirmed));
            if (_settings.FallbackModelPromptPrice.HasValue)
                return Task.FromResult(
                    (_settings.FallbackModelPromptPrice.Value,
                        _settings.FallbackModelPromptPrice.Value, PriceSource.Confirmed));
        }

        return Task.FromResult((0m, 0m, PriceSource.Unknown));
    }
}
