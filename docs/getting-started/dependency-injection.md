# Dependency Injection

All registration happens through extension methods in
`AIProviderConnect.DependencyInjection`. Three overloads are provided:

```csharp
// Embedded catalog only; option defaults are seeded from each definition.
public static IServiceCollection AddAiProviders(this IServiceCollection services);

// Merge/override definitions, then register every provider in the resulting catalog.
public static IServiceCollection AddAiProviders(
    this IServiceCollection services,
    IEnumerable<ProviderDefinition> customProviders);

// Full builder: add/replace definitions, configure named options, register a custom IAIProvider.
public static IServiceCollection AddAiProviders(
    this IServiceCollection services,
    Action<AIProviderRegistrationBuilder> configure);
```

!!! note
    For every provider, option defaults (`BaseUrl`, plus the protocol's
    chat/models endpoints) are seeded from its `ProviderDefinition`, so
    supplying only an API key is enough. Any `Configure` callback you register
    runs after seeding and therefore wins.

## What AddAiProviders Registers

1. **`ProviderCatalog`** as a singleton — the catalog loads all embedded
   provider definitions at construction time.
2. **One `IAIProvider` singleton per catalog entry.** The concrete type is
   chosen from the definition's wire protocol:

   | Protocol | Concrete type | Options class |
   | --- | --- | --- |
   | `OpenAICompatible` | `OpenAICompatibleProvider` | `OpenAICompatibleProviderOptions` |
   | `MessagesApi` | `MessagesApiProvider` | `MessagesApiOptions` |
   | `KeyQuery` | `KeyQueryProvider` | `KeyQueryOptions` |
   | `Catalog` | `ModelCatalogProvider` | `OpenAICompatibleProviderOptions` |
   | `HybridGateway` | `OpenAICompatibleProvider` | `HybridGatewayProviderOptions` |

   One exception: an `OpenAICompatible` or `HybridGateway` definition whose
   `endpoints` block declares a `decisions` entry with `protocol: "decision"`
   registers the combined `OpenAICompatibleDecisionProvider` — a single id
   that serves chat, streaming, model discovery, embeddings, **and** decisions
   over one transport. See [Wire Protocols](../concepts/wire-protocols.md#per-operation-endpoint-overrides)
   for the `endpoints` shape and its precedence chain; registration-time
   validation rejects a malformed entry before any provider is built.

3. **Named options per provider.** For each provider ID the matching options
   class is registered under that name with option defaults seeded from the
   catalog definition, so the provider can look up its catalog definition.

## Configuring Providers

Use the provider's catalog ID as the options name:

```csharp
builder.Services.Configure<OpenAICompatibleProviderOptions>("ollama", o =>
{
    o.Enabled = true;
    o.BaseUrl = "http://localhost:11434/v1/";
    o.ApiKey = "ollama";
});

builder.Services.Configure<MessagesApiOptions>("anthropic", o =>
{
    o.Enabled = true;
    o.BaseUrl = "https://api.anthropic.com/v1/";
    o.ApiKey = "fake-api-key";
});
```

Configuration can be inline (as above) or bound from `appsettings.json`
via `Microsoft.Extensions.Options.ConfigurationExtensions`:

```csharp
builder.Services.Configure<OpenAICompatibleProviderOptions>(
    "openai", builder.Configuration.GetSection("OpenAI"));
```

## Resolving Providers

Each provider is registered as a **keyed singleton** keyed by its catalog
ID. Use `IAIProviderFactory` to resolve a provider by ID:

```csharp
public class ChatService(IAIProviderFactory factory)
{
    private IAIProvider Get(string providerId) =>
        factory.GetProvider(providerId);
}
```

Alternatively, resolve directly from the service provider:

```csharp
var provider = sp.GetRequiredKeyedService<IAIProvider>("openai");
```

## IAIProviderFactory

`AddAiProviders()` registers `DefaultAIProviderFactory` for
`IAIProviderFactory`. It exposes `GetProvider(providerId)`,
resolving the keyed `IAIProvider` registrations from the
container. To use your own factory (custom key storage, per-call key
overrides, fallback order), register it after `AddAiProviders()` so it
supersedes the default:

```csharp
builder.Services.AddSingleton<IAIProviderFactory, MyProviderFactory>();
```

## Catalog Access

`ProviderCatalog` is also injectable for metadata lookups (display names,
pricing URLs, categories):

```csharp
public class CatalogService(ProviderCatalog catalog)
{
    public ProviderDefinition? GetDefinition(string id) => catalog.Get(id);
}
```

## Custom Providers and Overrides

The builder overload customizes the catalog at registration time.
`IProviderCatalog` stays read-only; all merging happens before providers are
wired, so custom ids receive correct options and `IAIProvider` registration.

### Override runtime values (base URL, endpoints)

```csharp
builder.Services.AddAiProviders(b => b
    .Replace("openai", d => d with { BaseUrl = "https://my-gateway.example.com/v1/" })
    .Configure<OpenAICompatibleProviderOptions>("openai", o =>
    {
        o.Enabled = true;
        o.ApiKey = "fake-api-key";
    }));
```

### Override the display name

`Replace` transforms the embedded `ProviderDefinition` in place. Note that
the runtime record carries only identity and endpoint fields — descriptive
metadata such as `DocumentationUrl` lives on `ProviderResearchMetadata`.

```csharp
builder.Services.AddAiProviders(b => b
    .Replace("anthropic", d => d with { DisplayName = "Anthropic (EU)" }));
```

### Add a new provider that uses a built-in protocol

```csharp
var myProvider = new ProviderDefinition
{
    Id = "my-llm",
    DisplayName = "My Self-Hosted LLM",
    BaseUrl = "https://test.example.com/v1/",
    Protocol = EProviderProtocol.OpenAICompatible
};

builder.Services.AddAiProviders(b => b
    .Add(myProvider)
    .Configure<OpenAICompatibleProviderOptions>("my-llm", o =>
    {
        o.Enabled = true;
        o.ApiKey = "fake-api-key";
    }));
```

### Register your own IAIProvider

`AddProvider` registers a consumer implementation and excludes that id from the
automatic protocol loop, so it is never double-registered:

```csharp
builder.Services.AddAiProviders(b => b
    .AddProvider<MyCustomProvider>("my-custom", sp => new MyCustomProvider(sp)));
```

!!! note
    Custom definitions are validated at registration time — a missing id, a
    missing base URL, or a duplicate id throws `ArgumentException` immediately
    instead of failing on the first request.
