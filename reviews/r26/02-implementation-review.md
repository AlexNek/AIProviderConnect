# Refactor 26 — Implementation Review (Independent)

**Plan:** `refactors/refactor26/protocol-tool-calling-and-documentation-accuracy.md`
**Checklist:** `refactors/refactor26/implementation-checklist.md`

---

## Summary

The production code implementation is correct and complete across all thirteen phases. Tool calling support for Messages API (Anthropic) and KeyQuery (Gemini) protocols is well-structured, documentation fixes are accurate, the HTTP 400 error code, streaming robustness wrappers, `ModelCatalogOverrideDecorator.SupportsModelDiscovery` delegation, and `ChatCompletionRequest` parameter expansion are all properly implemented.

Three Errors remain — all missing unit tests that the checklist explicitly requires. Two Warnings cover stale documentation that the plan's Goal #1 implies but the checklist does not explicitly call out by file name.

**Result: FAIL** — three Errors remain.

---

## Coverage Summary

| Metric | Count |
|---|---|
| Plan requirements (checklist items) | ~47 |
| Implemented correctly | 42 |
| Missing | 3 |
| Incorrect | 0 |
| Unknown | 0 |
| **Errors** | **3** |
| **Warnings** | **2** |

---

## Phase-by-phase verification

### Phase 1: README.md documentation fixes — PASS

- Line 8: "five wire protocols and four provider implementations" — verified.
- Line 57: `IAIProviderFactory factory` with `factory.GetProvider("openai")` — verified.
- Lines 65, 99: `EChatRole.User` — verified.
- Line 109: `EChatRole.Tool` — verified.

### Phase 2: docs/ documentation fixes — PASS (with Warning, see F4)

- `docs/concepts/wire-protocols.md` line 20: "Unknown or empty protocol strings throw `JsonException`" — verified.
- `docs/concepts/wire-protocols.md` line 17: Catalog row reads "*(no embedded entry — protocol exists for consumer-supplied providers)*" — verified.
- `docs/concepts/provider-catalog.md`: zero `github-models` hits — verified.
- `docs/chat/streaming.md` lines 36–41: property table includes all four properties (`Content`, `ReasoningContent`, `ToolCalls`, `IsCompleted`) — verified.
- `docs/chat/tool-calling.md` lines 65–82: `request.Messages.Add(...)` replaced with `with` expression — verified.

### Phase 3: Messages API tool calling — request mapping — PASS

- `tools` serialization in `MessagesApiProtocol.MapPayload` (lines 79–87): maps each `ToolDefinition` to `{ name, description, input_schema }` — verified.
- Assistant messages with `ToolCalls` emit content blocks with `type: "tool_use"` (lines 122–144): each block carries `id`, `name`, `input` — verified.
- Tool result messages (`EChatRole.Tool` with `ToolCallId`) emit user messages with `tool_result` content blocks (lines 108–120) — verified.
- Tool blocks are not duplicated as text blocks — early returns in `MapContent` prevent fallthrough.

### Phase 4: Messages API tool calling — response parsing — PASS

- Response content blocks of `type: "tool_use"` parsed into `ToolCall` instances (lines 247–265) — verified. Uses `TryGetProperty` per mandatory rule #9.
- `ChatCompletionResponse.ToolCalls` populated when tool use blocks present (lines 269–270) — verified.
- `FinishReason` read from `stop_reason` (lines 280–284) — Anthropic returns `"tool_use"` — verified.

### Phase 5: KeyQuery tool calling — request mapping — PASS

- `tools` serialization with `functionDeclarations` array (lines 79–93) — verified.
- Assistant messages with `ToolCalls` emit `role: "model"` with `functionCall` parts (lines 131–154) — verified.
- Tool result messages (`EChatRole.Tool`) emit `role: "function"` with `functionResponse` parts (lines 114–129) — verified.

### Phase 6: KeyQuery tool calling — response parsing — PASS

- Response candidates' `content.parts` scanned for `functionCall` objects (lines 220–242) — verified. Uses `TryGetProperty` throughout.
- `ChatCompletionResponse.ToolCalls` populated when function calls present (lines 245–246) — verified.
- Synthetic `ToolCall.Id` generated via `Guid.NewGuid().ToString("N")` (line 237) — verified.

### Phase 7: HTTP 400 error code — PASS

- `AiErrorCodes.InvalidRequest = "ai/invalid-request"` at `AiErrorCodes.cs` line 29 — verified.
- HTTP 400 mapped to `AiErrorCodes.InvalidRequest` in `AIProviderBase.ThrowIfErrorAsync` at line 374 (before the default arm) — verified.
- Unit test at `AIProviderBaseTests.cs` line 51: `InlineData(400, AiErrorCodes.InvalidRequest, ...)` — verified.

### Phase 8: Streaming robustness — PARTIAL (see F1)

- `OpenAICompatibleProviderBase.StreamAsync` (lines 69–78): `JsonSerializer.Deserialize<JsonElement>(item)` wrapped in try-catch, logs warning, returns null — verified.
- `MessagesApiProvider.StreamAsync` (lines 62–71): same pattern — verified.
- `KeyQueryProvider.StreamAsync` (lines 62–71): same pattern — verified.
- **Missing:** unit test verifying malformed SSE data does not crash the stream.

### Phase 9: ModelCatalogOverrideDecorator.SupportsModelDiscovery — PARTIAL (see F3)

- `SupportsModelDiscovery` at `ModelCatalogOverrideDecorator.cs` lines 46–48: delegates to inner when it implements `IModelDiscoveryProvider`, falls back to `_store.Get(_inner.Id).Count > 0` — verified.
- XML doc comment (lines 41–45) explains: "true means either the inner provider has a live discovery API or the consumer has registered override entries" — verified.
- **Partial:** only one of three required test cases exists. `IdentityMembers_DelegateToInner` (line 46) covers "inner supports discovery → true." Missing: "inner does not support discovery but overrides exist → true" and "inner does not support discovery and no overrides → false."

### Phase 10: ChatCompletionRequest parameter expansion — PARTIAL (see F2)

- `Stop` (`IReadOnlyList<string>?`, default null) at `ChatCompletionRequest.cs` line 41 — verified.
- `TopP` (`float?`, default null) at line 46 — verified.
- `FrequencyPenalty` (`float?`, default null) at line 51 — verified.
- `PresencePenalty` (`float?`, default null) at line 56 — verified.
- Mapped in `OpenAICompatibleWireProtocol.MapRequest` (lines 111–122): each included only when non-null — verified.
- **Missing:** unit tests verifying the new parameters are serialized correctly in the OpenAI-compatible request body.

### Phase 11: Tool calling tests — PASS

All eight required test methods exist:

| Test | File | Verified |
|---|---|---|
| Messages API tool definition serialization | `MessagesApiProtocolTests.MapRequest_WithTools_SerializesToolsArray` | line 293 |
| Messages API assistant tool_use mapping | `MessagesApiProtocolTests.MapRequest_AssistantWithToolCalls_SerializesToolUseBlocks` | line 318 |
| Messages API tool result mapping | `MessagesApiProtocolTests.MapRequest_ToolResultMessage_SerializesAsUserWithToolResultBlock` | line 356 |
| Messages API response tool_use parsing | `MessagesApiProtocolTests.ParseResponse_WithToolUseBlocks_PopulatesToolCalls` | line 390 |
| KeyQuery tool definition serialization | `KeyQueryWireProtocolTests.MapRequest_WithTools_SerializesFunctionDeclarations` | line 162 |
| KeyQuery model-role functionCall mapping | `KeyQueryWireProtocolTests.MapRequest_AssistantWithToolCalls_SerializesFunctionCallParts` | line 189 |
| KeyQuery function-role functionResponse mapping | `KeyQueryWireProtocolTests.MapRequest_ToolResultMessage_SerializesAsFunctionRoleWithFunctionResponse` | line 223 |
| KeyQuery response functionCall parsing | `KeyQueryWireProtocolTests.ParseResponse_WithFunctionCall_PopulatesToolCalls` | line 257 |

### Phase 12: Build and test verification — Unknown

Build/test execution not performed during this review. Code inspection shows no compilation issues.

### Phase 13: Final documentation verification — PASS (with Warning, see F4–F5)

- Grep `ChatRole\.` across docs/ and README.md: zero stale hits (all use `EChatRole.`).
- Grep `IEnumerable<IAIProvider>` across docs/ and README.md: zero hits.
- Grep `github-models` across docs/: zero hits.

---

## Findings

### F1 — Missing malformed-SSE streaming test

- **Severity:** Error
- **Plan requirement:** Phase 8 checklist — "Add a unit test verifying that a malformed SSE data line does not crash the stream (the chunk is skipped and streaming continues)"
- **Affected location:** `AIProviderConnectLib.Tests/` — no test file contains this scenario
- **Evidence:** The try-catch wrappers exist in all three streaming providers (`OpenAICompatibleProviderBase.cs` lines 69–78, `MessagesApiProvider.cs` lines 62–71, `KeyQueryProvider.cs` lines 62–71), each logging a warning via `Logger.LogWarning(ex, ...)` and returning null on `JsonException`. Grep for `malformed`, `skipping`, `SSE`, `StreamAsync.*Malform` across the test project returns zero hits related to streaming robustness.
- **Explanation:** The checklist explicitly requires a test that feeds a malformed SSE data line and verifies the stream continues without crashing. The production code is correct but untested.
- **Correction:** Add a test using a fake `HttpMessageHandler` that returns an SSE response containing a non-JSON `data:` line followed by a valid chunk, and verify the valid chunk is yielded without throwing.

### F2 — Missing ChatCompletionRequest parameter serialization tests

- **Severity:** Error
- **Plan requirement:** Phase 10 checklist — "Add unit tests verifying the new parameters are serialized correctly in the OpenAI-compatible request body"
- **Affected location:** `AIProviderConnectLib.Tests/Protocols/OpenAICompatibleWireProtocolTests.cs`
- **Evidence:** The properties exist on `ChatCompletionRequest` (lines 41–56) and the mapping exists in `OpenAICompatibleWireProtocol.MapRequest` (lines 111–122). Grep for `TopP|FrequencyPenalty|PresencePenalty|\.Stop` across the test project returns zero relevant hits (only unrelated `finish_reason: "stop"` values in JSON test data).
- **Explanation:** The checklist explicitly requires tests that set each new parameter and verify it appears in the serialized request body with the correct key name (`stop`, `top_p`, `frequency_penalty`, `presence_penalty`), and that null values are omitted from the body.
- **Correction:** Add tests in `OpenAICompatibleWireProtocolTests` that construct a request with each parameter set, serialize the payload, and verify the JSON contains the expected key. Add a test verifying that null parameters produce no key in the body.

### F3 — Missing two of three SupportsModelDiscovery decorator tests

- **Severity:** Error
- **Plan requirement:** Phase 9 checklist — "Update or add unit tests verifying the three cases: inner supports discovery, inner does not support discovery but overrides exist, inner does not support discovery and no overrides exist"
- **Affected location:** `AIProviderConnectLib.Tests/Providers/ModelCatalogOverrideDecoratorTests.cs`
- **Evidence:** `IdentityMembers_DelegateToInner` (line 46) covers case 1 (inner implements `IModelDiscoveryProvider` with `SupportsModelDiscovery = true`, decorator returns `true`). No test covers case 2 (inner is plain `IAIProvider` without `IModelDiscoveryProvider`, overrides exist in store → `SupportsModelDiscovery` should be `true`). No test covers case 3 (inner is plain `IAIProvider`, empty store → `SupportsModelDiscovery` should be `false`).
- **Explanation:** The checklist explicitly requires all three logical branches of the `SupportsModelDiscovery` property to be verified. Only one is tested.
- **Correction:** Add two tests: (a) inner is a plain `IAIProvider` mock (no `IModelDiscoveryProvider`) with overrides in the store, asserting `SupportsModelDiscovery` is `true`; (b) inner is a plain `IAIProvider` mock with an empty `InMemoryModelOverrideStore`, asserting `SupportsModelDiscovery` is `false`.

### F4 — Stale claims in tool-calling.md contradict new tool calling support

- **Severity:** Warning
- **Plan requirement:** Goal #1 — "Fix all documentation errors so every code example compiles and every claim matches the implementation"; D7 — "Documentation accuracy"
- **Affected location:** `docs/chat/tool-calling.md`, lines 3–4 and lines 87–89
- **Evidence:** Lines 3–4: *"Providers on the OpenAI-compatible protocols (OpenAI-compatible, HybridGateway, Catalog) support function calling."* Lines 87–89: *"Tool round trips require the OpenAI wire format. The Messages API and KeyQuery protocols map plain text messages only."* Both statements are now factually incorrect — the implementation adds full tool calling support to Messages API and KeyQuery protocols (Phases 3–6).
- **Explanation:** A consumer reading this page would incorrectly believe tool calling is unavailable for Anthropic and Gemini providers. The introductory paragraph and closing note directly contradict the new functionality this refactor introduces. The checklist fixes the code examples in this file but does not update these surrounding claims.
- **Correction:** Update the opening paragraph to include Messages API and KeyQuery alongside the OpenAI-compatible protocols. Remove or rewrite the closing note to reflect that all three protocol families now support tool round trips.

### F5 — NuGetReadme.md uses stale `ChatRole` enum and obsolete resolution pattern

- **Severity:** Warning
- **Plan requirement:** Goal #1 — "Fix all documentation errors so every code example compiles and every claim matches the implementation"
- **Affected location:** `NuGetReadme.md`, lines 37 and 41
- **Evidence:** Line 37: `var provider = providers.First(p => p.Id == "openai");` uses the obsolete `IEnumerable<IAIProvider>` scan pattern. Line 41: `Role = ChatRole.User` uses the removed enum name (renamed to `EChatRole`). The code example will not compile.
- **Explanation:** The plan's checklist Phase 1 only targets README.md, but `NuGetReadme.md` carries the same stale patterns the plan was designed to fix. This file is the NuGet package readme displayed on nuget.org, making it high-visibility user-facing documentation with non-compiling code examples.
- **Correction:** Update `NuGetReadme.md` to use `IAIProviderFactory.GetProvider("openai")` and `EChatRole.User`, mirroring the corrections applied to `README.md`.

---

## Mandatory implementation rules verification

| Rule | Status |
|---|---|
| 1. Do not change `IAIProvider`, `IStreamingChatProvider`, `IModelDiscoveryProvider` | Verified — interfaces untouched |
| 2. Do not modify OpenAI-compatible tool calling | Verified — existing implementation preserved |
| 3. Do not add tool calling to Catalog protocol | Verified — `CatalogWireProtocol` untouched |
| 4. Do not change record structures beyond specified properties | Verified — only additive properties on `ChatCompletionRequest` |
| 5. Do not introduce breaking changes to public API | Verified — all changes are additive |
| 6. Do add unit tests for every new protocol feature | **Partial** — tool calling tests present; streaming/parameter/decorator tests missing (F1, F2, F3) |
| 7. Do verify every documentation code example compiles | **Partial** — NuGetReadme.md has non-compiling examples (F5) |
| 8. Do use existing `ToolCall`, `ToolDefinition`, `ChatMessage` models | Verified — no protocol-specific tool models created |
| 9. Do handle null/missing fields gracefully with `TryGetProperty` | Verified — all protocol parsing uses `TryGetProperty` |
| 10. Do log warnings for malformed SSE data, not errors | Verified — all three providers use `LogWarning` |
