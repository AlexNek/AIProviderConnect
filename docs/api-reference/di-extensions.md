# DI Extensions

`AIProviderConnect.DependencyInjection` exposes `AddAiProviders` in three
overloads:

```csharp
// Embedded catalog only.
public static IServiceCollection AddAiProviders(this IServiceCollection services);

// Merge/override definitions, then register the resulting catalog.
public static IServiceCollection AddAiProviders(
    this IServiceCollection services,
    IEnumerable<ProviderDefinition> customProviders);

// Full builder: definitions, named options, custom providers, model overrides.
public static IServiceCollection AddAiProviders(
    this IServiceCollection services,
    Action<AIProviderRegistrationBuilder> configure);
```

Each overload registers the `ProviderCatalog` singleton, one named options
instance and one `IAIProvider` singleton per catalog entry, seeding option
defaults (`BaseUrl` plus the protocol's chat/models endpoints) from each
definition. See [Dependency Injection](../getting-started/dependency-injection.md)
for the full registration picture.

## AIProviderRegistrationBuilder

The builder overload collects customizations applied before providers are
wired. All methods return the builder for chaining.

| Method | Description |
| --- | --- |
| `Add(ProviderDefinition)` | Add a new definition, or replace an already-added one with the same id |
| `Replace(string providerId, Func<ProviderDefinition, ProviderDefinition>)` | Transform an embedded or added definition in place; chained calls on the same id compose |
| `Configure<TOptions>(string providerId, Action<TOptions>)` | Configure named options; runs after seeding, so consumer values win |
| `AddProvider<TProvider>(string providerId, Func<IServiceProvider, TProvider>)` | Register a consumer `IAIProvider` and exclude that id from the automatic protocol loop |
| `OverrideModels(string providerId, IEnumerable<ModelOverride>)` | Register model/pricing overrides merged transparently over `GetModelsAsync` (see [Model Discovery](../model-discovery/model-discovery.md)) |
| `UseModelOverrideStore(IModelOverrideStore)` | Replace the default in-memory override store with a consumer-supplied source |
| `UseCredentialResolver(ICredentialResolver)` | Register a consumer-supplied resolver consulted once per provider call for runtime credential/model overrides (see [Runtime Credentials](../concepts/runtime-credentials.md)) |

Custom definitions are validated at registration time: a missing id, a
missing base URL, or a duplicate id throws `ArgumentException`. A
`Native`-protocol definition with no `AddProvider` registration throws
`InvalidOperationException`.

## Options Reference

All options classes live in `AIProviderConnect.Options` and derive from
`AIProviderOptions`. Options are **named** by provider catalog ID.

### AIProviderOptions (base)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Enabled` | `bool` | `true` | Provider is enabled by default; set `false` to disable it before any call succeeds |
| `ApiKey` | `string` | `""` | API key (Bearer token, `x-api-key`, or `x-goog-api-key` header depending on protocol) |
| `BaseUrl` | `string` | `""` | API base URL |
| `DefaultModel` | `string` | `""` | Fallback model applied when a request carries no model and no per-call override supplies one; the chat path resolves `credentials?.Model` → `request.Model` → `DefaultModel` |
| `DefaultHeaders` | `Dictionary<string, string>` | empty | Extra headers sent with every request (see [Custom Headers](../concepts/wire-protocols.md#custom-headers)) |
| `MaxRetryCount` | `int` | `0` | Maximum retry attempts for transient failures (rate-limit, server errors); 0 disables retry |
| `RetryDelay` | `TimeSpan` | `1s` | Base delay between retries; actual delay uses exponential backoff with jitter |
| `ProtocolConfiguration` | `IReadOnlyDictionary<string, string>?` | `null` | Protocol-specific key-value pairs seeded from the provider JSON manifest |

### OpenAICompatibleProviderOptions

Base properties plus:

| Property | Type | Default |
| --- | --- | --- |
| `ChatEndpoint` | `string` | `"chat/completions"` |
| `ModelsEndpoint` | `string` | `"models"` |
| `EmbeddingsEndpoint` | `string` | `"embeddings"` |
| `DefaultEmbeddingModel` | `string` | `""` |

### HybridGatewayProviderOptions

Same shape as `OpenAICompatibleProviderOptions`. The `HybridGateway` protocol
is served by `OpenAICompatibleProvider` internally.

Base properties plus:

| Property | Type | Default |
| --- | --- | --- |
| `ChatEndpoint` | `string` | `"chat/completions"` |
| `ModelsEndpoint` | `string` | `"models"` |
| `EmbeddingsEndpoint` | `string` | `"embeddings"` |
| `DefaultEmbeddingModel` | `string` | `""` |

### MessagesApiOptions

Base properties plus:

| Property | Type | Default |
| --- | --- | --- |
| `MessagesEndpoint` | `string` | `"messages"` |
| `ModelsEndpoint` | `string` | `"models"` |

The `anthropic-version` header for Anthropic-compatible providers is sourced
from the provider JSON manifest's `protocolConfiguration.anthropicVersion`
via `MessagesApiProtocol.ApplyProtocolConfiguration`.

### KeyQueryOptions

Base properties only.

### DecisionProviderOptions

Base properties plus:

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `DecisionsEndpoint` | `string` | `"alpha/decisions"` | Endpoint path resolved against `BaseUrl` |
| `DecisionsBaseUrl` | `string?` | `null` | Full-URL override for hosts whose decisions surface lives on a different origin than `BaseUrl` |

Both are also readable from the definition's `protocolConfiguration`
(`decisionsEndpoint`, `decisionsBaseUrl`) via
`DecisionsWireProtocol.ApplyProtocolConfiguration`. See
[Decision Models](../decisions/decision-models.md).

### Catalog (uses OpenAICompatibleProviderOptions)

Base properties plus `ChatEndpoint` and `ModelsEndpoint` (see OpenAICompatibleProviderOptions above).
Endpoint defaults are `chat/completions` and `models` respectively.

The `X-GitHub-Api-Version` header is sourced from the provider JSON manifest's
`protocolConfiguration.apiVersion` via `CatalogWireProtocol.ApplyProtocolConfiguration`.

## Prerequisites

Providers resolve `HttpClient` directly from the container, so one of these
must be registered before resolving any provider:

```csharp
builder.Services.AddHttpClient();                       // Microsoft.Extensions.Http
// or
builder.Services.AddSingleton(new HttpClient());
```
