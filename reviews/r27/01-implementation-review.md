# Refactor 27 — Implementation Review

**Plan:** `refactors/refactor27/network-exception-translation-streaming-and-documentation.md`
**Checklist:** `refactors/refactor27/implementation-checklist.md`

---

## Summary

The implementation correctly and completely implements all eleven phases of the plan. Network-level exceptions are translated to `AiException` with stable codes at all three `HttpClient.SendAsync` call sites and are eligible for Polly retry. The `ModelCatalogOverrideDecorator` streaming false-positive is fixed by introducing a `NonStreamingModelCatalogOverrideDecorator` selected when the inner provider does not implement `IStreamingChatProvider`. Messages API streaming gains full tool-call delta support via a new stateful `MessagesApiStreamingParser`. Dead code (`EndpointDefaults.Catalog`) is removed. All documentation inaccuracies are corrected.

Three non-blocking Warnings are noted: one dead method left behind by the streaming parser replacement, minor documentation wording variations, and a missing DI-level test for the streaming decorator selection path.

**Result: PASS** — no unresolved Errors.

---

## Coverage Summary

| Metric | Count |
|---|---|
| Plan requirements (phases) | 11 |
| Implemented correctly | 11 |
| Missing | 0 |
| Incorrect | 0 |
| Unknown | 0 |
| **Errors** | **0** |
| **Warnings** | **3** |

---

## Build and test evidence

| Check | Result |
|---|---|
| `dotnet build AIProviderConnectLib` (Release) | 0 warnings / 0 errors |
| `dotnet test AIProviderConnectLib.Tests` (Release) | 300 passed / 0 failed |
| `dotnet build ScraperTool` (Release) | 0 warnings / 0 errors |
| `dotnet test ScraperTool.Tests` (Release) | 639 passed / 0 failed |

---

## Phase-by-phase verification

### Phase 1: Network exception translation in AIProviderBase — PASS

- `TranslateNetworkExceptionsAsync` private helper added at `AIProviderBase.cs` lines 255–271. Catches `HttpRequestException` → `AiException(AiErrorCodes.NoConnection)` and `TaskCanceledException` (when `!cancellationToken.IsCancellationRequested`) → `AiException(AiErrorCodes.Timeout)`. User-initiated cancellation propagates unchanged.
- Used inside the Polly pipeline callback in `SendChatAndParseAsync` (line 292–293) — replaces the direct `HttpClient.SendAsync` call.
- Used inside the Polly pipeline callback in `SendGetModelsAndParseAsync` (line 324–325) — replaces the direct `HttpClient.SendAsync` call.
- Used in `StreamCoreAsync` (line 233–235) — wraps `HttpClient.SendAsync` with `HttpCompletionOption.ResponseHeadersRead`. Translation only; retry does not apply to `IAsyncEnumerable` streaming.
- Polly `ShouldHandle` predicate at lines 99–101 now matches `AiErrorCodes.RateLimited`, `AiErrorCodes.NoServer`, `AiErrorCodes.NoConnection`, and `AiErrorCodes.Timeout`.
- `AiErrorCodes.NoConnection` (`"ai/no-connection"`) and `AiErrorCodes.Timeout` (`"ai/timeout"`) confirmed present in `AiErrorCodes.cs` lines 50 and 88.
- Five unit tests in `AIProviderBaseTests.cs`:
  - `ChatAsync_HttpRequestException_TranslatedToNoConnection` (line 297) — verifies `HttpRequestException` → `AiException` with code `NoConnection`.
  - `GetModelsAsync_TimeoutTaskCanceledException_TranslatedToTimeout` (line 312) — verifies timeout-type `TaskCanceledException` → `AiException` with code `Timeout`.
  - `ChatAsync_UserCancellation_PropagatesAsOperationCanceledException` (line 327) — verifies user cancellation propagates as `OperationCanceledException`, not `AiException`.
  - `GetModelsAsync_NoConnection_IsRetriedByPollyPipeline` (line 343) — verifies translated `NoConnection` is retried by Polly when `MaxRetryCount > 0`; uses `ScriptedThrowHttpMessageHandler` that throws on first call and succeeds on second.
  - `StreamAsync_HttpRequestException_TranslatedToNoConnection` (line 360) — verifies streaming path also translates `HttpRequestException` to `NoConnection`.

### Phase 2: ModelCatalogOverrideDecorator streaming fix — PASS

- `NonStreamingModelCatalogOverrideDecorator` created at `NonStreamingModelCatalogOverrideDecorator.cs`. Implements `IAIProvider` and `IModelDiscoveryProvider` only — does **not** implement `IStreamingChatProvider`. Delegates `ChatAsync`, `GetModelsAsync`, `SupportsModelDiscovery`, `Id`, `IsEnabled`, `Protocol` identically to the existing decorator.
- `ApplyModelOverrides` in `AIProviderServiceCollectionExtensions.cs` (lines 258–260) selects `ModelCatalogOverrideDecorator` when `inner is IStreamingChatProvider`, otherwise `NonStreamingModelCatalogOverrideDecorator`.
- Two unit tests in `NonStreamingModelCatalogOverrideDecoratorTests.cs`:
  - `StreamingInner_WrappedInStreamingDecorator_IsIStreamingChatProvider` (line 40) — verifies `ModelCatalogOverrideDecorator` passes `is IStreamingChatProvider`.
  - `NonStreamingInner_WrappedInNonStreamingDecorator_IsNotIStreamingChatProvider` (line 55) — verifies `NonStreamingModelCatalogOverrideDecorator` fails `is IStreamingChatProvider`.
- DI-level test `OverrideModels_WrapsResolvedProvider_AndMergesCatalog` in `AIProviderRegistrationBuilderTests.cs` (line 289) verifies `BeOfType<NonStreamingModelCatalogOverrideDecorator>` for a non-streaming inner provider.

### Phase 3: Messages API streaming tool call support — PASS

- Five new constants in `MessagesApiPropertyNames.cs`: `ContentBlockStart` (`"content_block_start"`), `ContentBlockStop` (`"content_block_stop"`), `InputJsonDelta` (`"input_json_delta"`), `PartialJson` (`"partial_json"`), `Index` (`"index"`).
- `MessagesApiStreamingParser` class created at `Protocols/MessagesApiStreamingParser.cs`. Maintains a `Dictionary<int, ToolUseBlock>` of active tool-use content blocks keyed by index.
  - `content_block_start` with `type: "tool_use"` → registers block with `id` and `name` (lines 56–81).
  - `content_block_delta` with `type: "text"` → emits text content chunk (lines 93–98, existing behavior preserved).
  - `content_block_delta` with `type: "input_json_delta"` → emits `StreamingChatChunk` with `ToolCalls` containing a `StreamingToolCallDelta` with correct `Index`, `ArgumentsFragment`, and (on first fragment only) `Id` and `Name` (lines 100–136).
  - `content_block_stop` → removes block from active dictionary (lines 141–147).
  - `message_stop` → emits `StreamingChatChunk { IsCompleted = true }` (lines 48–51).
- `MessagesApiProvider.StreamAsync` (lines 58–75) creates a `MessagesApiStreamingParser` instance and passes it as a stateful closure to `StreamCoreAsync`. The `StreamCoreAsync` method signature is unchanged (`Func<string, StreamingChatChunk?>`).
- `StreamingChatChunk` model carries `IReadOnlyList<StreamingToolCallDelta>? ToolCalls` property. `StreamingToolCallDelta` record has `Index`, `Id`, `Name`, `ArgumentsFragment`.
- Eight unit tests in `MessagesApiStreamingParserTests.cs`:
  - `ContentBlockStart_ToolUse_RegistersNewBlock` (line 19)
  - `InputJsonDelta_FirstFragment_EmitsIdAndName` (line 34)
  - `InputJsonDelta_SubsequentFragment_OmitsIdAndName` (line 57)
  - `TextDelta_EmitsContentChunk` (line 82)
  - `TextAndToolCallDeltas_CoexistInSameStream` (line 99)
  - `MessageStop_ProducesIsCompleted` (line 127)
  - `ContentBlockStop_CleansUpBlock` (line 142)
  - `UnknownEventType_ReturnsNull` (line 158)

### Phase 4: Dead code removal — PASS

- `EndpointDefaults.Catalog` nested class deleted from `Constants/EndpointDefaults.cs`. File now contains only `ChatCompletions`, `Messages`, `Models`, and `KeyQuery` — 18 lines total.
- Grep `EndpointDefaults.Catalog` across the solution: zero hits in source code. Only historical review/refactor documents reference it.
- Solution builds with 0 errors after deletion.

### Phase 5: Documentation fixes — error-handling.md — PASS

- HTTP 400 → `ai/invalid-request` row added to the HTTP status mapping table (line 39).
- `InvalidRequest` (`ai/invalid-request`) added to the "All Error Codes" table (line 57).
- `ProviderNotFound` (`ai/provider-not-found`) added to the "All Error Codes" table (line 67).
- `NoConnection` and `Timeout` also present in the error codes table (lines 61, 69).

### Phase 6: Documentation fixes — README.md — PASS

- Line 17: "github-models" replaced with "*(no embedded entry — protocol exists for consumer-supplied providers)*" — matches `wire-protocols.md`.
- Grep `github-models` across README.md and docs/: zero hits.

### Phase 7: Documentation fixes — provider/protocol terminology — PASS

- `docs/concepts/wire-protocols.md` line 3: "four protocol implementations covering five wire protocols" — already correct, no change needed.
- `docs/api-reference/interfaces.md` line 112: "all four provider implementations" — verified.
- `docs/getting-started/installation.md` line 41: "four protocol-specific provider implementations" — verified (see W-2).
- `docs/faq.md` line 37: "All four provider implementations" — verified.
- `docs/chat/streaming.md` line 15: "All four built-in provider implementations" — verified (see W-2).
- `docs/getting-started/quick-start.md` line 78: "all four built-in implementations" — verified (see W-2).
- `docs/model-discovery/model-discovery.md` line 16: "All four built-in provider implementations" — verified.

### Phase 8: Documentation fixes — model-discovery.md — PASS

- Lines 18–22: `SupportsModelDiscovery` description now notes that it mirrors `ProviderDefinition.HasModelDiscoveryApi` **and** that the decorator also returns `true` when consumer-supplied model overrides are registered, even if the inner provider has no discovery API.

### Phase 9: Documentation fixes — stale Catalog endpoint references — PASS

- `docs/concepts/wire-protocols.md` lines 67, 69: `chat/completions` and `models` — verified (was `inference/chat/completions` and `catalog/models`).
- `docs/model-discovery/model-discovery.md` line 78: `GET {BaseUrl}models` in the Catalog row — verified.
- `docs/api-reference/di-extensions.md` line 105: "Endpoint defaults are `chat/completions` and `models` respectively" — verified.
- Grep `inference/chat/completions` across docs/: zero hits.
- Grep `catalog/models` across docs/: zero hits.

### Phases 10–11: Build/test verification and final grep checks — PASS

- All four build/test commands succeed (see table above).
- All final grep checks pass: `EndpointDefaults.Catalog` (0 source hits), `github-models` (0 hits in docs/README), `inference/chat/completions` (0 hits), `catalog/models` (0 hits).

---

## Findings

### W-1 — Dead method: `MessagesApiProtocol.ParseStreamChunk`

- **Severity:** Warning
- **Plan requirement:** Phase 3 (side effect of D4)
- **Affected location:** `AIProviderConnectLib/Protocols/MessagesApiProtocol.cs`, lines 304–322
- **Evidence:** Grep for `MessagesApiProtocol.ParseStreamChunk` across both `AIProviderConnectLib/` and `AIProviderConnectLib.Tests/` returns zero matches. `MessagesApiProvider.StreamAsync` now uses `MessagesApiStreamingParser.ParseStreamChunk` exclusively.
- **Explanation:** The old static `ParseStreamChunk` method is superseded by the new stateful parser but was not removed. It only handles text deltas and `message_stop`, not tool call deltas, so it is now an incomplete duplicate of the new parser's text-delta logic. The plan's Phase 4 (dead code removal) only targets `EndpointDefaults.Catalog` and does not explicitly require removing this method.
- **Correction:** Remove the dead `ParseStreamChunk` method from `MessagesApiProtocol` in a follow-up cleanup, or retain it if external consumers are known to call it directly.

### W-2 — Minor documentation wording variations

- **Severity:** Warning
- **Plan requirement:** Phase 7 — "Standardize the provider/protocol terminology across all docs"
- **Affected locations:** `docs/chat/streaming.md` line 15, `docs/getting-started/quick-start.md` line 78, `docs/getting-started/installation.md` line 41
- **Evidence:** The plan specified exact phrasings: "All four provider implementations" (streaming.md, quick-start.md) and "four provider implementations" (installation.md). Actual text: streaming.md says "All four **built-in** provider implementations", quick-start.md says "all four **built-in** implementations" (missing "provider"), installation.md says "four **protocol-specific** provider implementations".
- **Explanation:** The count "four" is correct everywhere and the meaning is preserved. The extra qualifiers ("built-in", "protocol-specific") are not factually wrong but deviate from the standardized phrasing the plan intended.
- **Correction:** Align wording in a follow-up pass if strict uniformity is desired.

### W-3 — No DI-level test for streaming decorator selection path

- **Severity:** Warning
- **Plan requirement:** Phase 2 checklist — "Add a unit test verifying a streaming inner provider wrapped by `ApplyModelOverrides` passes the `is IStreamingChatProvider` check"
- **Affected location:** `AIProviderConnectLib.Tests/DependencyInjection/AIProviderRegistrationBuilderTests.cs`
- **Evidence:** `OverrideModels_WrapsResolvedProvider_AndMergesCatalog` (line 259) tests the non-streaming path (`BeOfType<NonStreamingModelCatalogOverrideDecorator>`). No corresponding DI-level test verifies that a streaming inner provider + overrides yields `ModelCatalogOverrideDecorator`. The decorator type itself is tested directly in `NonStreamingModelCatalogOverrideDecoratorTests.StreamingInner_WrappedInStreamingDecorator_IsIStreamingChatProvider` (line 40), which verifies `ModelCatalogOverrideDecorator` is assignable to `IStreamingChatProvider`.
- **Explanation:** The `ApplyModelOverrides` selection logic is a trivial ternary (`inner is IStreamingChatProvider ? streaming-decorator : non-streaming-decorator`), and the decorator types are individually tested. The gap is coverage of the streaming branch through the full DI pipeline. The existing tests provide adequate behavioral coverage.
- **Correction:** Add a DI-level test mirroring `OverrideModels_WrapsResolvedProvider_AndMergesCatalog` but with `inner.As<IStreamingChatProvider>()` and asserting `BeOfType<ModelCatalogOverrideDecorator>()`.

---

## Mandatory implementation rules verification

| Rule | Status |
|---|---|
| 1. Do not change `IAIProvider`, `IStreamingChatProvider`, `IModelDiscoveryProvider` | Verified — interfaces untouched |
| 2. Do not change `StreamCoreAsync` method signature | Verified — signature unchanged (`Func<string, StreamingChatChunk?>`) |
| 3. Do not modify OpenAI-compatible or KeyQuery streaming | Verified — only `MessagesApiProvider.StreamAsync` changed |
| 4. Do not modify provider JSON files under `ai-providers/` | Verified — no JSON files touched |
| 5. Do not introduce `Microsoft.Extensions.Http` / `IHttpClientFactory` | Verified — no new dependencies |
| 6. Do add unit tests for network exception translation | Verified — 5 tests covering all required scenarios |
| 7. Do add unit tests for decorator streaming selection | Verified — 2 direct decorator tests + 1 DI-level test |
| 8. Do add unit tests for Messages API streaming tool call parsing | Verified — 8 tests covering all required scenarios |
| 9. Do verify solution builds with 0 warnings / 0 errors | Verified — both library and ScraperTool |
| 10. Do verify all existing tests pass | Verified — 939/939 passing |
