# Changelog

All notable changes to this project will be documented in this file. Date format: YYYY-MM-DD

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Optional decision-model capability via `IDecisionProvider` (`SupportsDecisions`, `DecideAsync`): a provider answers typed questions about application state with probabilities rather than chat prose. Supports `Choice`, `Noul` (yes/no), and `Score` question kinds with typed answers (`ChoiceAnswer`, `NoulAnswer`, `ScoreAnswer`), a new `EProviderProtocol.Decision` wire protocol (JSON value `"decision"`), an `EModelCapability.Decision` flag for call-free discovery, `DecisionProviderOptions` (configurable `alpha/decisions` endpoint and optional decisions base-URL override), and dedicated error codes (`ai/chat-not-supported`, `ai/decision-not-supported`). A decision provider rejects `ChatAsync`; per-request credentials, retry, and error classification behave as they do for chat
- `UsageInfo.Cost` (USD, nullable) reports a per-call cost when the provider returns one; existing protocols leave it unset
- Optional per-operation `endpoints` block on `ProviderDefinition` for multi-protocol providers: each entry (keys `chat`, `models`, `messages`, `embeddings`, `decisions`, matched case-insensitively) carries a relative `path`, an optional `baseUrl` override used only when the surface sits on a different root than the definition's common base, and an optional `protocol` override with nullable-aware JSON conversion. Endpoint paths fold into the named options at registration time with a documented precedence (defaults, flat field, `endpoints` entry, consumer `Configure`), and malformed entries (unknown operation key, absolute path, non-HTTPS override, an entry that changes nothing, an unservable `decisions` override, or a KeyQuery `chat` path without a `{model}` placeholder) fail registration fast. An `OpenAICompatible`/`HybridGateway` provider declaring a `decisions` entry with `protocol: "decision"` registers the combined `OpenAICompatibleDecisionProvider` — one provider id serving chat, streaming, model discovery, embeddings, and decisions — and the model-override decorator chain stays capability-transparent for it
- ScraperTool manual editor: unified Endpoint Overrides editor for all endpoint data (path, optional base-URL override, optional protocol override). Legacy flat fields are auto-seeded into the editor on load (marked as non-deletable so essential endpoints cannot be accidentally removed), and edits to the flat properties sync back into the matching row so the data survives save. The protocol override ComboBox is disabled for `chat`, `models`, and `messages` rows — these operations are always served by the provider's root protocol class; only `decisions` and `embeddings` allow a per-endpoint protocol switch

## [1.1.0] - 2026-09-28

### Added
- Optional embeddings capability via `IEmbeddingProvider` with OpenAI-compatible `/embeddings` wire mapping, separate `DefaultEmbeddingModel` configuration, and dedicated error codes (`ai/embedding-failed`, `ai/embedding-model-not-configured`)
- Per-request (runtime) credential and model resolution: the `RequestCredentials` record (key-masked `ToString`) and the `ICredentialResolver` hook let a consumer supply an API key, base URL, and model per call — across chat, streaming, model discovery, and embeddings — without rebuilding the DI graph or leaving the singleton provider behind. Resolution priority is `GetProvider` overrides > `ICredentialResolver` > configured options
- `IAIProviderFactory.GetProvider(providerId, RequestCredentials overrides)` returns a transient provider bound to the supplied overrides; the shared singleton resolved by `GetProvider(providerId)` is never mutated. A provider id that has model-override entries is decorated, and the decorator exposes no embeddings through either overload
- `AddAiProviders(b => b.UseCredentialResolver(resolver))` registers a consumer-supplied `ICredentialResolver` (the library ships no default implementation)

### Changed
- `AIProviderOptions.DefaultModel` is now applied at runtime as the fallback model when a request carries no model and no per-call override supplies one; it was previously inert. An empty effective chat model now throws `AiException` with `ai/invalid-request`
- Provider configuration is validated against the effective values (per-call override combined with configured options) before the model is resolved, so a provider configured with an empty API key or base URL completes a call once an override supplies the missing value, still fails when nothing does, and keeps reporting the actionable configuration error when the request also carries no model
- `IAIProviderFactory` gains the `GetProvider(providerId, RequestCredentials)` member — a breaking change for consumers who implement `IAIProviderFactory` themselves, who must now also implement the new overload

## [1.0.0] - 2026-09-25

### Added
- Unified `IAIProvider` interface (`ChatAsync`) with optional capabilities: `IStreamingChatProvider` (`StreamAsync`) and `IModelDiscoveryProvider` (`GetModelsAsync`)
- `IAIProviderFactory` abstraction for multi-provider scenarios
- 35+ provider definitions via embedded JSON catalog (`ProviderCatalog`), served by four protocol implementations
- OpenAI-compatible wire protocol (`OpenAICompatibleWireProtocol`, `OpenAICompatibleProvider`)
- Messages API wire protocol (`MessagesApiProtocol`, `MessagesApiProvider`)
- Key-query protocol for Google-style APIs (`KeyQueryProvider`)
- Model catalog protocol for GitHub Models-style APIs (`ModelCatalogProvider`)
- Hybrid gateway protocol (`OpenAICompatibleProvider` with `HybridGatewayProviderOptions`)
- Streaming via `IAsyncEnumerable<StreamingChatChunk>` with tool-call deltas (`StreamingToolCallDelta`)
- Tool / function calling support
- Structured output via `ResponseFormat` with JSON schema
- Structured error handling via `AiException` with centralized `AiErrorCodes`, including status-aware messages for common HTTP error codes
- Single `AddAiProviders()` DI extension registering every catalog provider with named options per provider ID
- Named provider options bindable from `appsettings.json` via `Microsoft.Extensions.Options`
- Developer manual published as a documentation site via MkDocs Material and GitHub Pages

<!--## [1.0.0] — 2026-06-20-->

[Unreleased]: https://github.com/AlexNek/AIProviderConnect/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/AlexNek/AIProviderConnect/releases/tag/v1.0.0
