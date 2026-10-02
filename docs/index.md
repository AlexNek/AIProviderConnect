# AIProviderConnect Developer Manual

One .NET interface for talking to AI providers — chat, streaming, tool
calling, embeddings, runtime model discovery, and decision models behind a
single set of capability interfaces.

AIProviderConnect ships a catalog of 35+ provider definitions (embedded JSON)
and a small set of protocol-driven provider implementations. You configure a
provider by ID, and the library speaks the right wire protocol for you. It also
supports **decision models** — including TypeSafe's **Jev** (`typesafe/jev-1.13`)
reached via OpenRouter — that answer typed questions about application state
with probabilities.

## Feature Overview

| Capability | Entry point | Description |
| --- | --- | --- |
| Chat completions | `IAIProvider.ChatAsync` | Single request/response chat over any registered provider |
| Streaming | `IStreamingChatProvider.StreamAsync` | Server-sent events as `IAsyncEnumerable<StreamingChatChunk>` |
| Tool calling | `ChatCompletionRequest.Tools` | Function definitions in, `ToolCall` list out |
| Embeddings | `IEmbeddingProvider.EmbedAsync` | Vector embeddings for one or more input strings |
| Model discovery | `IModelDiscoveryProvider.GetModelsAsync` | List models available from a provider at runtime |
| Decision models | `IDecisionProvider.DecideAsync` | Typed probabilistic answers to questions about state (e.g. Jev) |
| Provider catalog | `ProviderCatalog` | 35+ embedded JSON provider definitions with metadata |
| Dependency injection | `AddAiProviders()` | One-line registration of every catalog provider |

## Architecture at a Glance

All runtime work goes through `IAIProvider`; metadata comes from the catalog.
Concrete providers cover every wire protocol in the catalog, including a
non-chat `DecisionProvider` for the decisions wire:

```mermaid
graph LR
    A[IAIProvider] --> B[AIProviderBase]
    B --> C[OpenAICompatibleProviderBase]
    B --> D[MessagesApiProvider]
    B --> E[KeyQueryProvider]
    B --> G[DecisionProvider]
    C --> F[OpenAICompatibleProvider]
    C --> H[ModelCatalogProvider]
    I[ProviderCatalog] --> F
    I --> H
    I --> D
    I --> E
    I --> G
```

Every failure surfaces as an `AiException` with a stable error code — see
[Error Handling](concepts/error-handling.md).

## Where to Start

- New to the library? Follow [Installation](getting-started/installation.md)
  and the [Quick Start](getting-started/quick-start.md).
- Wondering how providers are wired up? Read
  [Wire Protocols](concepts/wire-protocols.md) and
  [Dependency Injection](getting-started/dependency-injection.md).
- Working with decision models (e.g. Jev)? See
  [Decision Models](decisions/decision-models.md).
- Looking for a specific type? Check the
  [API Reference](api-reference/interfaces.md).

## License

MIT — see the `LICENSE.txt` in the repository.
