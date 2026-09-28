# Runtime Credentials

AIProviderConnect resolves provider configuration (API key, base URL, model) at
DI-registration time and bakes it into the singleton `IAIProvider`. This works
for applications with a static key per provider, but not for multi-user
scenarios where each caller supplies their own credentials.

**Per-request credentials** let a consumer supply API key, base URL, or model
for a single call — or for every call on a transient provider — without
rebuilding the DI graph.

## RequestCredentials

```csharp
namespace AIProviderConnect.Models;

public sealed record RequestCredentials
{
    public string? ApiKey  { get; init; }
    public string? BaseUrl { get; init; }
    public string? Model   { get; init; }
}
```

Every field is nullable. A `null` (or whitespace) value means "use the
provider's configured value". A record with all three unset is treated as
"no override". `BaseUrl` must be the **full** API base including the version
segment (for example `https://api.example.com/v1/`).

The `ToString()` override masks `ApiKey` so a credential record can be logged
safely — the key never appears in full.

## Priority Chain

Credential and model resolution follows a strict priority at each call site:

1. **`GetProvider(id, overrides)` fixed credentials** — highest, attached once
   to the transient instance before publication.
2. **`ICredentialResolver` result** — consulted once per call if no fixed
   credentials are set and a resolver is registered.
3. **`AIProviderOptions` (configured at DI registration)** — fallback for any
   field the first two sources leave unset.

For the model specifically (chat path):
`credentials?.Model` → `request.Model` → `Options.DefaultModel`.
An empty effective model throws `AiException(InvalidRequest)`.

For embeddings:
`credentials?.Model` → `request.Model` → `DefaultEmbeddingModel` (never the
chat `DefaultModel`).

## ICredentialResolver

```csharp
namespace AIProviderConnect.Abstractions;

public interface ICredentialResolver
{
    ValueTask<RequestCredentials?> ResolveAsync(
        string providerId,
        CancellationToken cancellationToken = default);
}
```

The implementation must be a **thread-safe singleton** — the library calls it
once per provider call without creating a DI scope. The `providerId` parameter
identifies which provider is being resolved; the resolver reads per-caller
identity from ambient state (e.g. `HttpContext`, `AsyncLocal<T>`, or a custom
tenant token).

### Sample Implementation

```csharp
public sealed class TenantCredentialResolver(
    IServiceScopeFactory scopeFactory,
    IHttpContextAccessor httpContext) : ICredentialResolver
{
    public async ValueTask<RequestCredentials?> ResolveAsync(
        string providerId, CancellationToken ct)
    {
        var tenantId = httpContext.HttpContext?.User.FindFirst("tenant_id")?.Value;
        if (tenantId is null)
            return null; // no override — use configured options

        // Create a short-lived scope to reach scoped storage.
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantKeyStore>();
        var key = await store.GetKeyAsync(tenantId, providerId, ct);

        return key is not null
            ? new RequestCredentials { ApiKey = key }
            : null;
    }
}
```

## Registration

Wire the resolver through the builder:

```csharp
services.AddAiProviders(b =>
{
    b.UseCredentialResolver(new TenantCredentialResolver(scopeFactory, httpContext));
    // ... Add definitions, Configure options, etc.
});
```

Alternatively, register the resolver directly as a singleton in the container:

```csharp
services.AddSingleton<ICredentialResolver>(new TenantCredentialResolver(...));
services.AddAiProviders(); // library resolves it via GetService at provider construction
```

## GetProvider with Overrides (Factory Path)

```csharp
var factory = serviceProvider.GetRequiredService<IAIProviderFactory>();

var provider = factory.GetProvider("openai", new RequestCredentials
{
    ApiKey  = "user-supplied-key",
    BaseUrl = "https://user-instance.example.com/v1/",
    Model   = "gpt-4o-mini"
});

var response = await provider.ChatAsync(request);
```

`GetProvider(id, overrides)` returns a **transient** instance bound to the
supplied credentials. The shared singleton (resolved by `GetProvider(id)`) is
never mutated. Each call to `GetProvider(id, overrides)` builds a fresh
provider — safe under concurrency with no cross-contamination.

The transient instance honors `overrides.ApiKey`, `overrides.BaseUrl`, and
`overrides.Model` for all transport operations (chat, streaming, model
discovery, and embeddings). Cast the result to `IEmbeddingProvider` or
`IStreamingChatProvider` to access optional capabilities.

## Shared HttpClient Expectation

Providers resolve `HttpClient` from the DI container. Both the singleton and
transient (factory-built) instances share the same `HttpClient`. Per-call
credential changes apply at the request-message level (headers, URL) — the
shared client itself is never mutated. This is the expected pattern: register
one `HttpClient` (or use `AddHttpClient()`) and let the library build per-request
configuration.

## Retry Determinism

When retry is enabled (`MaxRetryCount > 0`), the Polly pipeline retries the
outgoing HTTP call, but credentials are resolved **once** before the pipeline
is entered. A retry reuses the same resolved `RequestCredentials` — the
resolver is never re-consulted mid-flight.

## Error Behavior

| Condition | Result |
| --- | --- |
| `ICredentialResolver` throws | `AiException(ProviderMissingConfiguration)` naming the provider id |
| Caller cancellation during resolution | `OperationCanceledException` propagates unchanged |
| Fixed credentials set, then overwritten | `InvalidOperationException` (single-write enforcement) |
| Provider does not derive from `AIProviderBase` | `AiException(ConfigurationError)` |
