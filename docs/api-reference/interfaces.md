# Core Interfaces

All interfaces live in the `AIProviderConnect.Abstractions` namespace.

## IAIProvider

The core runtime interface — every registered provider implements it.

```csharp
public interface IAIProvider
{
    string Id { get; }
    bool IsEnabled { get; }
    EProviderProtocol Protocol { get; }

    Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}
```

| Member | Description |
| --- | --- |
| `Id` | Catalog ID, e.g. `"openai"`, `"anthropic"` |
| `IsEnabled` | Mirrors the `Enabled` flag of the provider's options |
| `Protocol` | Wire protocol resolved from the catalog definition |
| `ChatAsync` | One chat completion round trip |

Deliberately metadata-free: URLs, display names, and pricing come from
`ProviderCatalog`, not from the provider instance.

## IStreamingChatProvider

Optional streaming capability — see [Streaming](../chat/streaming.md).

```csharp
public interface IStreamingChatProvider
{
    IAsyncEnumerable<StreamingChatChunk> StreamAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}
```

## IModelDiscoveryProvider

Optional model listing — see [Model Discovery](../model-discovery/model-discovery.md).

```csharp
public interface IModelDiscoveryProvider
{
    bool SupportsModelDiscovery { get; }

    Task<IReadOnlyList<AIModel>> GetModelsAsync(
        CancellationToken cancellationToken = default);
}
```

## IEmbeddingProvider

Optional embeddings capability — see [Embeddings](../chat/embeddings.md).

```csharp
public interface IEmbeddingProvider
{
    Task<EmbeddingResponse> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default);
}
```

## IModelOverrideStore

Consumer-supplied source of `ModelOverride` entries, keyed by provider id.
The library ships an in-memory default populated by `OverrideModels`;
implement this to back overrides with your own store (code, JSON, database)
and register it with `UseModelOverrideStore`. See
[Model Discovery](../model-discovery/model-discovery.md).

```csharp
public interface IModelOverrideStore
{
    IReadOnlyList<ModelOverride> Get(string providerId);
}
```

## IProviderCatalog

Read-only view over the provider definitions.

```csharp
public interface IProviderCatalog
{
    IReadOnlyList<ProviderDefinition> All { get; }
    ProviderDefinition? Get(string providerId);
    ProviderResearchMetadata? GetResearchMetadata(string providerId);
    IReadOnlyList<ProviderDefinition> WithModelDiscovery { get; }
    IReadOnlyList<ProviderDefinition> WithDynamicCatalog { get; }
    IReadOnlyList<string> LoadErrors { get; }
    IReadOnlyList<ProviderDefinition> GetByCategory(string category);
}
```

`Get` returns the runtime definition; `GetResearchMetadata` returns the
separate `ProviderResearchMetadata` (website, pricing URLs, dynamic-catalog
and regional-endpoint flags) held outside the runtime record. The concrete
`ProviderCatalog` additionally offers `ReloadFromDisk` for tooling that
patches definitions at runtime.

## IAIProviderFactory

Abstraction for multi-provider scenarios. `AddAiProviders()` registers the
built-in `DefaultAIProviderFactory`, which resolves keyed `IAIProvider`
instances from the container.

```csharp
public interface IAIProviderFactory
{
    IAIProvider GetProvider(string providerId);

    IAIProvider GetProvider(string providerId, RequestCredentials overrides);
}
```

| Member | Description |
| --- | --- |
| `GetProvider(providerId)` | Returns the shared keyed `IAIProvider` singleton configured from DI options |
| `GetProvider(providerId, overrides)` | Returns a **transient** provider bound to the supplied `RequestCredentials`; `FixedCredentials` take priority over any `ICredentialResolver` and over configured options, and the singleton is left unchanged |

## ICredentialResolver

Consumer-supplied, thread-safe singleton consulted once per provider call to
supply runtime credential/model overrides. Register it with
`AddAiProviders(b => b.UseCredentialResolver(resolver))` or directly as
`services.AddSingleton<ICredentialResolver>(...)`. The library provides no
default implementation. See [Runtime Credentials](../concepts/runtime-credentials.md).

```csharp
public interface ICredentialResolver
{
    ValueTask<RequestCredentials?> ResolveAsync(
        string providerId,
        CancellationToken cancellationToken = default);
}
```

## AIProviderBase

Abstract base class behind all four provider implementations. Implements
`IAIProvider` and `IModelDiscoveryProvider`, provides SSE reading, header
and auth helpers, and the centralized HTTP error translation
(`ThrowIfErrorAsync`). OpenAI-compatible providers inherit shared request
transport from `OpenAICompatibleProviderBase`; protocol-specific request
building and parsing live in the `Protocols/` wire-protocol types.
