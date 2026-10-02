# Provider Catalog

Provider *definitions* (metadata) and provider *implementations* (runtime)
are separate things in AIProviderConnect. The catalog owns the metadata:
35+ JSON files embedded in the assembly under `ai-providers/*.json`.

## ProviderDefinition

Each JSON file deserializes into an immutable `ProviderDefinition` record.
The runtime-relevant fields:

| Field | Type | Purpose |
| --- | --- | --- |
| `Id` | `string` | Unique ID used as the DI options name (`"openai"`, `"gemini"`, ...) |
| `DisplayName` | `string` | Human-readable name |
| `Protocol` | `EProviderProtocol` | Wire protocol, mapped from the JSON string by `EProviderProtocolJsonConverter` |
| `BaseUrl` | `string` | Default API base URL |
| `ChatEndpoint` | `string` | Chat endpoint path, default `chat/completions` |
| `ModelsEndpoint` | `string` | Model list endpoint path, default `models` |
| `Endpoints` | `IReadOnlyDictionary<string, EndpointDefinition>?` | Optional per-operation overrides (`chat`, `models`, `messages`, `embeddings`, `decisions`); see [Wire Protocols](wire-protocols.md#per-operation-endpoint-overrides) |
| `HasModelDiscoveryApi` | `bool` | Drives `SupportsModelDiscovery` on the provider instance |

The descriptive fields — `Website`, `LoginUrl`, `ApiPricingUrl`,
`SubscriptionPricingUrl`, `DocumentationUrl`, `ModelDescription`,
`ModelDiscoveryNotes`, `PayAsYouGo`, `HasFreeTier`, `MinimumCommitment`,
`SupportsFineTuning`, `IsDynamicModelCatalog`, `RegionalEndpoints` — live on
a separate `ProviderResearchMetadata` record (read it with
`catalog.GetResearchMetadata(id)`). They are metadata for tooling and UIs —
the runtime providers do not use them. A value of `"-"` marks fields that do
not apply (e.g. pricing pages for localhost providers); that sentinel is
defined by consuming tooling, not by the library.

## Categories

The definitions are grouped into these `category` values:

| Category | Examples |
| --- | --- |
| `DirectProvider` | openai, anthropic, gemini, xai, mistral, cohere |
| `SelfHosted` | ollama, lmstudio, vllm, jan, localai, textgenwebui |
| `HostedInference` | groq, cerebras, deepinfra, fireworks |
| `MultiModelAggregator` | openrouter, poe, requesty, aimlapi, edenai |
| `AIGateway` | portkey, vercel-ai-gateway |
| `HybridGateway` | opencode-go, opencode-zen |
| `ResearchPlatform` | blablador |

## Using the Catalog

```csharp
using AIProviderConnect.Services;

var catalog = new ProviderCatalog();

IReadOnlyList<ProviderDefinition> all = catalog.All;
ProviderDefinition? openai = catalog.Get("openai");          // case-insensitive
IReadOnlyList<ProviderDefinition> local = catalog.GetByCategory("SelfHosted");
IReadOnlyList<ProviderDefinition> discoverable = catalog.WithModelDiscovery;
IReadOnlyList<ProviderDefinition> dynamic = catalog.WithDynamicCatalog;   // localhost providers
IReadOnlyList<string> errors = catalog.LoadErrors;           // malformed JSON entries
```

`ProviderCatalog` is registered as a singleton by `AddAiProviders()`, so
inject it instead of constructing it manually.

## Loading Behavior

- Definitions are loaded from embedded resources whose names contain
  `ai_providers` and end in `.json` (`*.validation.json` files are skipped).
- Property names are matched case-insensitively.
- A definition that fails to deserialize is skipped and reported in
  `LoadErrors` — the rest of the catalog still loads.

## Reloading Patched Definitions

`ReloadFromDisk(manifestPath, providerIds)` replaces in-memory definitions
with JSON files from disk (`{providerId}.json` under `manifestPath`). This
exists for tooling that patches provider files at runtime; normal
applications never need it.
