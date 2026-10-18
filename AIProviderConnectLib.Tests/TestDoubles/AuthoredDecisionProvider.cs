using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Protocols;
using AIProviderConnect.Providers;

namespace AIProviderConnect.Tests.TestDoubles;

/// <summary>
/// A consumer-authored decisions transport built only from public and <c>protected</c> API: it derives
/// <see cref="AIProviderBase"/>, reuses the public wire maps, and is seeded with the public
/// <c>SeedFromDefinition</c>. It exists to prove the D9 escape hatch compiles without an
/// <c>InternalsVisibleTo</c> grant.
/// </summary>
public sealed class AuthoredDecisionProvider : AIProviderBase
{
    private readonly string _endpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthoredDecisionProvider"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="catalog">The provider catalog containing the provider's definition.</param>
    /// <param name="options">The options instance, seeded by the consumer.</param>
    /// <param name="providerId">The provider id.</param>
    /// <param name="endpoint">The relative decisions endpoint path this transport posts to.</param>
    public AuthoredDecisionProvider(
        HttpClient httpClient,
        IProviderCatalog catalog,
        AIProviderOptions options,
        string providerId,
        string endpoint)
        : base(httpClient, catalog, options, providerId)
    {
        _endpoint = endpoint;
    }

    /// <inheritdoc />
    public override EDecisionSource DecisionSource => EDecisionSource.Native;

    /// <inheritdoc />
    public override Task<DecisionResponse> DecideAsync(
        DecisionRequest request,
        CancellationToken cancellationToken = default) =>
        SendDecisionAndParseAsync(
            _endpoint,
            DecisionsWireProtocol.MapRequest(request),
            null,
            DecisionsWireProtocol.ParseResponse,
            cancellationToken);

    /// <inheritdoc />
    public override Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new AiException(
            AiErrorCodes.ChatNotSupported,
            $"Provider '{Id}' is a decisions-only authored transport.");
}
