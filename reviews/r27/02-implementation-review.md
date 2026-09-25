# Refactor 27 — Independent Implementation Verification

**Plan:** `refactors/refactor27/network-exception-translation-streaming-and-documentation.md`
**Checklist:** `refactors/refactor27/implementation-checklist.md`
**Previous review:** `reviews/r27/01-implementation-review.md`

---

## Summary

Independent re-verification of the refactor 27 implementation against the design doc and checklist. All eleven phases are correctly implemented. The codebase is consistent with the plan's goals: network exceptions are translated to `AiException` with stable codes and are eligible for Polly retry, the streaming capability check is accurate through the decorator split, Messages API streaming supports tool call deltas, dead code is removed, and documentation inaccuracies are corrected. All five mandatory implementation rules are satisfied.

Three non-blocking Warnings carried forward from the 01 review remain unaddressed: one dead method superseded by the new streaming parser, minor documentation wording variations, and a missing DI-level test for the streaming decorator branch. No new Errors or Warnings found.

**Result: PASS** — 0 Errors, 3 Warnings.

---

## Build and test evidence

| Check | Result |
|---|---|
| `dotnet build AIProviderConnectLib` (Release) | 0 warnings / 0 errors |
| `dotnet test AIProviderConnectLib.Tests` (Release) | 332 passed / 0 failed |
| `dotnet build ScraperTool` (Release) | 0 warnings / 0 errors |
| `dotnet test ScraperTool.Tests` (Release) | 639 passed / 0 failed |

---

## Phase-by-phase verdicts

| Phase | Description | Verdict |
|---|---|---|
| 1 | Network exception translation in AIProviderBase | PASS |
| 2 | ModelCatalogOverrideDecorator streaming fix | PASS |
| 3 | Messages API streaming tool call support | PASS |
| 4 | Dead code removal | PASS |
| 5 | Documentation — error-handling.md | PASS |
| 6 | Documentation — README.md | PASS |
| 7 | Documentation — provider/protocol terminology | PASS |
| 8 | Documentation — model-discovery.md | PASS |
| 9 | Documentation — stale Catalog endpoint references | PASS |
| 10 | Build and test verification | PASS |
| 11 | Final grep checks | PASS |

---

## Phase 1: Network exception translation — PASS

**Helper method:** `TranslateNetworkExceptionsAsync` in [AIProviderBase.cs](file:///y:/user_alex_new/dot_net2022/Github/AIProviderConnect_private/AIProviderConnectLib/Providers/AIProviderBase.cs) wraps `HttpClient.SendAsync` in a try-catch:
- `HttpRequestException` → `AiException(AiErrorCodes.NoConnection)`
- `TaskCanceledException` when `!cancellationToken.IsCancellationRequested` → `AiException(AiErrorCodes.Timeout)`
- User cancellation (`cancellationToken.IsCancellationRequested == true`) propagates as `OperationCanceledException`

**Call-site coverage:**
- `SendChatAndParseAsync` — helper used inside the Polly pipeline callback
- `SendGetModelsAndParseAsync` — helper used inside the Polly pipeline callback
- `StreamCoreAsync` — helper wraps `HttpClient.SendAsync` with `HttpCompletionOption.ResponseHeadersRead`

**Polly retry:** `ShouldHandle` predicate matches `RateLimited`, `NoServer`, `NoConnection`, and `Timeout`.

**Error codes:** `AiErrorCodes.NoConnection` (`"ai/no-connection"`) and `AiErrorCodes.Timeout` (`"ai/timeout"`) confirmed in `AiErrorCodes.cs`.

**Tests (5):**
- `ChatAsync_HttpRequestException_TranslatedToNoConnection` — HttpRequestException → NoConnection
- `GetModelsAsync_TimeoutTaskCanceledException_TranslatedToTimeout` — timeout → Timeout
- `ChatAsync_UserCancellation_PropagatesAsOperationCanceledException` — user cancel stays OperationCanceledException
- `GetModelsAsync_NoConnection_IsRetriedByPollyPipeline` — NoConnection retried by Polly
- `StreamAsync_HttpRequestException_TranslatedToNoConnection` — streaming path also translates

## Phase 2: Decorator streaming fix — PASS

**New class:** `NonStreamingModelCatalogOverrideDecorator` implements `IAIProvider` and `IModelDiscoveryProvider` only — does **not** implement `IStreamingChatProvider`.

**Selection logic:** `ApplyModelOverrides` in `AIProviderServiceCollectionExtensions.cs` selects `ModelCatalogOverrideDecorator` when `inner is IStreamingChatProvider`, otherwise `NonStreamingModelCatalogOverrideDecorator`.

**Tests (3):**
- `StreamingInner_WrappedInStreamingDecorator_IsIStreamingChatProvider` — streaming inner passes `is IStreamingChatProvider`
- `NonStreamingInner_WrappedInNonStreamingDecorator_IsNotIStreamingChatProvider` — non-streaming inner fails check
- `OverrideModels_WrapsResolvedProvider_AndMergesCatalog` — DI-level test verifies `BeOfType<NonStreamingModelCatalogOverrideDecorator>`

## Phase 3: Messages API streaming tool calls — PASS

**Constants:** `ContentBlockStart`, `ContentBlockStop`, `InputJsonDelta`, `PartialJson`, `Index` added to `MessagesApiPropertyNames.cs`.

**Parser:** `MessagesApiStreamingParser` in `Protocols/` maintains a `Dictionary<int, ToolUseBlock>` of active tool-use blocks:
- `content_block_start` (type `tool_use`) → registers block with `id` and `name`
- `content_block_delta` (type `text`) → emits text content chunk
- `content_block_delta` (type `input_json_delta`) → emits `StreamingToolCallDelta` with `Index`, `ArgumentsFragment`, and (on first fragment) `Id` and `Name`
- `content_block_stop` → removes block from active dictionary
- `message_stop` → emits `IsCompleted = true`

**Provider integration:** `MessagesApiProvider.StreamAsync` creates a parser instance and passes it as a stateful closure to `StreamCoreAsync`. The `StreamCoreAsync` method signature is unchanged.

**Tests (8):** Cover block registration, first/subsequent fragment emission, text deltas, coexistence of text and tool call deltas, `message_stop`, `content_block_stop` cleanup, and unknown event types.

## Phase 4: Dead code removal — PASS

`EndpointDefaults.Catalog` nested class deleted. File contains only `ChatCompletions`, `Messages`, `Models`, and `KeyQuery`. Grep for `EndpointDefaults.Catalog` across the solution: zero hits in source code.

## Phases 5–9: Documentation fixes — PASS

| Phase | File(s) | Verification |
|---|---|---|
| 5 | `docs/concepts/error-handling.md` | HTTP 400 → `ai/invalid-request` row present; `InvalidRequest`, `ProviderNotFound`, `NoConnection`, `Timeout` in error codes table |
| 6 | `README.md` | "github-models" replaced with explanatory note matching `wire-protocols.md`; grep returns 0 hits |
| 7 | 6 doc files | All use "four" with correct count; wording variations noted in W-2 |
| 8 | `docs/model-discovery/model-discovery.md` | `SupportsModelDiscovery` notes decorator override behavior |
| 9 | 3 doc files | `inference/chat/completions` → `chat/completions`, `catalog/models` → `models`; grep returns 0 hits for old values |

## Phases 10–11: Build/test and grep verification — PASS

All four build/test commands succeed (see table above). All final grep conditions satisfied:
- `EndpointDefaults.Catalog` — 0 source hits
- `github-models` in docs/README — 0 hits
- `inference/chat/completions` in docs/ — 0 hits
- `catalog/models` in docs/ — 0 hits

---

## Mandatory implementation rules

| Rule | Status |
|---|---|
| 1. Do not change `IAIProvider`, `IStreamingChatProvider`, `IModelDiscoveryProvider` | Verified — interfaces untouched |
| 2. Do not change `StreamCoreAsync` method signature | Verified — `Func<string, StreamingChatChunk?>` unchanged |
| 3. Do not modify OpenAI-compatible or KeyQuery streaming | Verified — only `MessagesApiProvider.StreamAsync` changed |
| 4. Do not modify provider JSON files under `ai-providers/` | Verified — no JSON files touched |
| 5. Do not introduce `Microsoft.Extensions.Http` / `IHttpClientFactory` | Verified — no new dependencies |
| 6. Do add unit tests for network exception translation | Verified — 5 tests |
| 7. Do add unit tests for decorator streaming selection | Verified — 3 tests |
| 8. Do add unit tests for Messages API streaming tool call parsing | Verified — 8 tests |
| 9. Do verify solution builds with 0 warnings / 0 errors | Verified |
| 10. Do verify all existing tests pass | Verified — 971/971 passing |

---

## Findings

### W-1 — Dead method: `MessagesApiProtocol.ParseStreamChunk`

- **Severity:** Warning
- **Affected location:** `AIProviderConnectLib/Protocols/MessagesApiProtocol.cs`, lines 304–322
- **Evidence:** Grep for `MessagesApiProtocol.ParseStreamChunk` across `AIProviderConnectLib/` and `AIProviderConnectLib.Tests/` returns zero matches in source code. `MessagesApiProvider.StreamAsync` now uses `MessagesApiStreamingParser.ParseStreamChunk` exclusively.
- **Explanation:** The old static method is superseded by the new stateful parser but was not removed. It only handles text deltas and `message_stop`, not tool call deltas, so it is an incomplete duplicate of the new parser's text-delta logic. Phase 4 (dead code removal) only targets `EndpointDefaults.Catalog` and does not explicitly require removing this method, so this is outside the plan's scope.
- **Correction:** Remove the dead `ParseStreamChunk` method from `MessagesApiProtocol` in a follow-up cleanup.

### W-2 — Minor documentation wording variations

- **Severity:** Warning
- **Affected locations:** `docs/chat/streaming.md` line 15, `docs/getting-started/quick-start.md` line 78, `docs/getting-started/installation.md` line 41
- **Evidence:** Plan specified "All four provider implementations" (streaming.md, quick-start.md) and "four provider implementations" (installation.md). Actual text: streaming.md says "All four **built-in** provider implementations", quick-start.md says "all four built-in implementations" (omits "provider"), installation.md says "four **protocol-specific** provider implementations".
- **Explanation:** The count "four" is correct everywhere and the meaning is preserved. The extra qualifiers are not factually wrong but deviate from the standardized phrasing the plan intended.
- **Correction:** Align wording in a follow-up pass if strict uniformity is desired.

### W-3 — No DI-level test for streaming decorator selection path

- **Severity:** Warning
- **Affected location:** `AIProviderConnectLib.Tests/DependencyInjection/AIProviderRegistrationBuilderTests.cs`
- **Evidence:** `OverrideModels_WrapsResolvedProvider_AndMergesCatalog` tests the non-streaming path (`BeOfType<NonStreamingModelCatalogOverrideDecorator>`). No corresponding DI-level test verifies that a streaming inner provider + overrides yields `ModelCatalogOverrideDecorator`. The decorator type itself is tested directly in `NonStreamingModelCatalogOverrideDecoratorTests.StreamingInner_WrappedInStreamingDecorator_IsIStreamingChatProvider`.
- **Explanation:** The `ApplyModelOverrides` selection logic is a trivial ternary, and both decorator types are individually tested. The gap is coverage of the streaming branch through the full DI pipeline. Existing tests provide adequate behavioral coverage.
- **Correction:** Add a DI-level test mirroring the existing non-streaming test but with `inner.As<IStreamingChatProvider>()` and asserting `BeOfType<ModelCatalogOverrideDecorator>()`.
