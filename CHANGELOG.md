# Changelog

All notable changes to this project will be documented in this file. Date format: YYYY-MM-DD

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
