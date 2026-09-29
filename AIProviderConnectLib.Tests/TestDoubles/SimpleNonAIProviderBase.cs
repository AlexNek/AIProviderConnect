using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

namespace AIProviderConnect.Tests.TestDoubles;

/// <summary>
/// A minimal <see cref="IAIProvider"/> that does NOT derive from <c>AIProviderBase</c>,
/// used to prove that <c>GetProvider(id, overrides)</c> throws <c>ConfigurationError</c> when
/// the constructed provider cannot accept fixed credentials.
/// </summary>
public sealed class SimpleNonAIProviderBase : IAIProvider
{
    public string Id => "simple-consumer";
    public bool IsEnabled => true;
    public EProviderProtocol Protocol => EProviderProtocol.OpenAICompatible;

    public Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
