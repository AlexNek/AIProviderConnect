using AIProviderConnect.Constants;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Providers;

namespace AIProviderConnect.DependencyInjection;

/// <summary>
/// Registration-time validation and class-selection lookup for the optional
/// <see cref="ProviderDefinition.Endpoints"/> block. Validation lives here rather than in
/// <c>CustomProviderDefinitionValidator</c> because <see cref="AIProviderServiceCollectionExtensions"/>
/// runs for every catalog entry, embedded and consumer-supplied alike.
/// </summary>
internal static class EndpointConfiguration
{
    /// <summary>
    /// Rejects an unusable <c>endpoints</c> entry: an unknown operation key, an entry that
    /// changes nothing, an empty <c>path</c>, a schemed <c>path</c> (a surface on another
    /// root uses the <c>baseUrl</c> override), a <c>baseUrl</c> that is not an absolute
    /// <c>https</c> URL, a <c>KeyQuery</c> <c>chat</c> value without a <c>{model}</c>
    /// placeholder, a <c>decision</c> override on a primary that has no class for the
    /// combination, and a malformed <c>additionalQueryParameter</c> (starts with <c>?</c>,
    /// or contains an unencoded <c>#</c> or space). Each failure throws <see cref="AiException"/>
    /// with <see cref="AiErrorCodes.ConfigurationError"/> naming the provider id and the operation.
    /// </summary>
    internal static void Validate(string providerId, ProviderDefinition definition)
    {
        if (definition.Endpoints is null)
        {
            return;
        }

        foreach (var (key, entry) in definition.Endpoints)
        {
            ValidateEntry(providerId, key, entry, definition);
        }
    }

    /// <summary>
    /// Resolves the <c>decisions</c> override for class selection: true when the definition
    /// declares an entry with <c>protocol: "decision"</c>. The operation is resolved through
    /// <see cref="EndpointOperations.Find"/>.
    /// </summary>
    internal static bool TryGetDecisionsOverride(
        ProviderDefinition definition,
        out EndpointDefinition? entry)
    {
        entry = EndpointOperations.Find(definition.Endpoints, EndpointOperations.Decisions);
        return entry?.Protocol == EProviderProtocol.Decision;
    }

    /// <summary>
    /// Resolves the effective decisions surface a definition declares. Returns true only when the
    /// <c>decisions</c> entry pins a protocol; <paramref name="surface"/> then carries that protocol,
    /// otherwise it carries the primary protocol and the caller treats decisions as the provider's
    /// default read-out capability. Unlike <see cref="TryGetDecisionsOverride"/> this reports a
    /// <c>decisionReadout</c> pin as well as a native <c>decision</c> surface.
    /// </summary>
    internal static bool TryGetDecisionsSurface(
        ProviderDefinition definition,
        out EndpointDefinition? entry,
        out EProviderProtocol surface)
    {
        entry = EndpointOperations.Find(definition.Endpoints, EndpointOperations.Decisions);
        surface = entry?.Protocol ?? definition.Protocol;
        return entry?.Protocol is not null;
    }

    private static void ValidateEntry(
        string providerId,
        string key,
        EndpointDefinition entry,
        ProviderDefinition definition)
    {
        var operation = ResolveKnownOperation(key)
            ?? throw new AiException(
                AiErrorCodes.ConfigurationError,
                $"Provider '{providerId}': unknown operation '{key}' in the endpoints block. " +
                $"Known operations: {EndpointOperations.Chat}, {EndpointOperations.Embeddings}, " +
                $"{EndpointOperations.Messages}, {EndpointOperations.Models}, {EndpointOperations.Decisions}.");

        if (entry.Path is null && entry.BaseUrl is null && entry.Protocol is null
            && entry.AdditionalQueryParameter is null)
        {
            throw new AiException(
                AiErrorCodes.ConfigurationError,
                $"Provider '{providerId}': the endpoints['{operation}'] entry changes nothing — " +
                "an endpoints mapping must not be decoration.");
        }

        if (entry.Path is not null)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                throw new AiException(
                    AiErrorCodes.ConfigurationError,
                    $"Provider '{providerId}': endpoints['{operation}'].path must not be empty.");
            }

            if (Uri.TryCreate(entry.Path, UriKind.Absolute, out _))
            {
                throw new AiException(
                    AiErrorCodes.ConfigurationError,
                    $"Provider '{providerId}': endpoints['{operation}'].path must be a relative path. " +
                    "Use the entry's baseUrl override for a surface on another root.");
            }
        }

        if (entry.BaseUrl is not null
            && (!Uri.TryCreate(entry.BaseUrl, UriKind.Absolute, out var baseUri)
                || (!baseUri.Scheme.Equals(UriSchemes.Https, StringComparison.OrdinalIgnoreCase)
                    && !LoopbackUrl.IsLoopback(baseUri))))
        {
            throw new AiException(
                AiErrorCodes.ConfigurationError,
                $"Provider '{providerId}': endpoints['{operation}'].baseUrl must be an absolute https " +
                "URL, or an absolute http URL on a loopback host.");
        }

        if (operation == EndpointOperations.Chat
            && entry.Path is not null
            && EffectiveProtocol(entry, definition) == EProviderProtocol.KeyQuery
            && !entry.Path.Contains("{model}", StringComparison.Ordinal))
        {
            throw new AiException(
                AiErrorCodes.ConfigurationError,
                $"Provider '{providerId}': endpoints['chat'].path for a KeyQuery surface must carry " +
                "a {model} placeholder.");
        }

        if (operation == EndpointOperations.Decisions
            && entry.Protocol == EProviderProtocol.Decision
            && definition.Protocol is not (EProviderProtocol.OpenAICompatible
                or EProviderProtocol.HybridGateway
                or EProviderProtocol.Decision))
        {
            throw new AiException(
                AiErrorCodes.ConfigurationError,
                $"Provider '{providerId}': the {definition.Protocol} primary has no class serving " +
                "chat and decisions together; a decisions protocol override is not usable here.");
        }

        if (operation == EndpointOperations.Decisions
            && entry.Protocol == EProviderProtocol.DecisionReadout
            && definition.Protocol is not (EProviderProtocol.OpenAICompatible
                or EProviderProtocol.HybridGateway
                or EProviderProtocol.MessagesApi
                or EProviderProtocol.KeyQuery
                or EProviderProtocol.Catalog))
        {
            throw new AiException(
                AiErrorCodes.ConfigurationError,
                $"Provider '{providerId}': the {definition.Protocol} primary has no chat surface; " +
                "a decisionReadout override is not usable here.");
        }

        if (entry.AdditionalQueryParameter is not null)
        {
            if (entry.AdditionalQueryParameter.StartsWith('?'))
            {
                throw new AiException(
                    AiErrorCodes.ConfigurationError,
                    $"Provider '{providerId}': endpoints['{operation}'].additionalQueryParameter " +
                    "must not start with '?' — the seeding step supplies the separator itself.");
            }

            if (entry.AdditionalQueryParameter.Contains('#')
                || entry.AdditionalQueryParameter.Contains(' '))
            {
                throw new AiException(
                    AiErrorCodes.ConfigurationError,
                    $"Provider '{providerId}': endpoints['{operation}'].additionalQueryParameter " +
                    "must not contain an unencoded '#' or space.");
            }
        }
    }

    private static string? ResolveKnownOperation(string key)
    {
        foreach (var known in new[]
                 {
                     EndpointOperations.Chat,
                     EndpointOperations.Embeddings,
                     EndpointOperations.Messages,
                     EndpointOperations.Models,
                     EndpointOperations.Decisions
                 })
        {
            if (string.Equals(key, known, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return null;
    }

    private static EProviderProtocol EffectiveProtocol(
        EndpointDefinition entry,
        ProviderDefinition definition) => entry.Protocol ?? definition.Protocol;
}
