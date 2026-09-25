# Model Discovery

Providers can list the models they serve at runtime through
`IModelDiscoveryProvider`:

```csharp
public interface IModelDiscoveryProvider
{
    bool SupportsModelDiscovery { get; }

    Task<IReadOnlyList<AIModel>> GetModelsAsync(
        CancellationToken cancellationToken = default);
}
```

All four built-in provider implementations implement the interface, but whether discovery
actually works depends on the catalog definition:
`SupportsModelDiscovery` mirrors `ProviderDefinition.HasModelDiscoveryApi`.
When consumer-supplied model overrides are registered for a provider (see
[Consumer-Supplied Overrides](#consumer-supplied-overrides) below), the
decorator also returns `true` — even if the inner provider has no
discovery API.

## Usage Pattern

Always check the flag first — calling `GetModelsAsync` on a provider
without a discovery API throws `AiException` with code
`ai/model-discovery-not-supported`:

```csharp
if (provider is IModelDiscoveryProvider discovery
    && discovery.SupportsModelDiscovery)
{
    IReadOnlyList<AIModel> models = await discovery.GetModelsAsync();
}
```

## AIModel

| Property | Type | Description |
| --- | --- | --- |
| `Id` | `string` | Model ID to pass to `ChatCompletionRequest.Model` |
| `DisplayName` | `string` | Human-readable name |
| `ProviderId` | `string` | Owning provider's catalog ID |
| `OwnedBy` | `string?` | Organization that owns the model |
| `Description` | `string?` | Model description, when the API provides one |
| `ContextWindow` | `int?` | Maximum context size in tokens |
| `Capabilities` | `EModelCapability` | Capability flags (see below) |
| `Modality` | `string?` | Input/output modalities, e.g. `"text+image->text"` |
| `PromptPrice` | `decimal?` | Price per million prompt tokens |
| `CompletionPrice` | `decimal?` | Price per million completion tokens |
| `PriceUnit` | `EModelPriceUnit` | Pricing unit, default `EModelPriceUnit.Per1M` |

Not every provider API returns all of these — discovery fills what the
provider exposes and leaves the rest at defaults.

## EModelCapability

`Capabilities` is a `[Flags]` enum covering text and multimodal abilities:

- Text & core: `TextGeneration`, `StructuredOutput`, `ToolCalling`,
  `Embedding`, `Reranker`
- Vision: `ImageRecognition`, `ImageGeneration`
- Audio: `AudioRecognition`, `TextToSpeech`, `AudioGeneration`
- Video: `VideoTranscription`, `VideoRecognition`, `VideoGeneration`

```csharp
bool supportsTools = model.Capabilities.HasFlag(EModelCapability.ToolCalling);
```

## Discovery per Protocol

| Protocol | Endpoint | Notes |
| --- | --- | --- |
| OpenAI-compatible / HybridGateway | `GET {BaseUrl}{ModelsEndpoint}` | Parses the `data` array |
| Messages API | `GET {BaseUrl}{ModelsEndpoint}` | Parses the `data` array |
| KeyQuery | `GET {BaseUrl}models` | Authenticates with `x-goog-api-key`; strips the `models/` prefix from names |
| Catalog | `GET {BaseUrl}models` | Maps `id`, `name`, `publisher` |

## Dynamic Catalogs (Localhost Providers)

Self-hosted providers (Ollama, Jan, LM Studio, LocalAI, vLLM, TGI,
TextGenWebUI, FastChat) have `IsDynamicModelCatalog = true`. Their model
catalog depends on what the user has downloaded or loaded locally —
no external method (API, web search, or page scraping) can determine
the available model count.

`SupportsModelDiscovery` is `false` for these providers. Use
`ProviderCatalog.WithDynamicCatalog` to retrieve the full list of
providers with dynamic catalogs.

## Consumer-Supplied Overrides

Many providers have no `/models` endpoint, and those that do often omit
pricing or return stale data. You can supply model lists and token prices
from your own source and have them applied **transparently over the
existing `GetModelsAsync()`** — the call site does not change, and
providers without overrides behave exactly as before.

Overrides are registered on the DI builder with `OverrideModels`. Each
`ModelOverride` is a patch with all-nullable fields: only the fields you
set are applied, so you never clobber a live value you did not intend to
change. The `Id` field matches an existing `AIModel.Id`
(case-insensitively); an override whose `Id` matches nothing is added as a
new model.

### Enrich live models with prices

```csharp
services.AddAiProviders(o => o.OverrideModels("openai", new[]
{
    new ModelOverride { Id = "gpt-4o", PromptPrice = 2.50m, CompletionPrice = 10.00m },
}));

// Same call site as before — live list merged with your prices.
IReadOnlyList<AIModel> models = await discovery.GetModelsAsync();
```

### Supply a full list for a provider with no API

```csharp
services.AddAiProviders(o => o.OverrideModels("edenai", MyCatalog.Load("edenai")));
```

When the inner provider has no discovery API (or throws
`ai/model-discovery-not-supported`), the base list is treated as empty and
only your overrides are returned. Any other live-discovery error (network,
auth) propagates to the caller.

### Correct a field

```csharp
services.AddAiProviders(o => o.OverrideModels("openai", new[]
{
    new ModelOverride { Id = "gpt-4o", ContextWindow = 128000, OwnedBy = "OpenAI" },
}));
```

### Hide a model

```csharp
services.AddAiProviders(o => o.OverrideModels("openai", new[]
{
    new ModelOverride { Id = "gpt-3.5-turbo", Hidden = true },
}));
```

### Custom override source

By default overrides are held in memory. Implement `IModelOverrideStore`
to back them with your own source (JSON, database, remote config):

```csharp
services.AddAiProviders(o => o.UseModelOverrideStore(new JsonModelOverrideStore("models.json")));
```

Merging preserves the live order, patches matched models in place, and
appends newly materialized models last.
