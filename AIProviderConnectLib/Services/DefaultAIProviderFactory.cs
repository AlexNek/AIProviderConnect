using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnect.Services;

/// <summary>Resolves providers configured through dependency injection.</summary>
public sealed class DefaultAIProviderFactory(IServiceProvider services) : IAIProviderFactory
{
    public IAIProvider GetProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return services.GetKeyedService<IAIProvider>(providerId)
            ?? throw new AiException(AiErrorCodes.ProviderNotFound, $"Provider '{providerId}' is not registered.");
    }
}
