# Refactor 26 — Implementation Review

**Plan:** `refactors/refactor26/protocol-tool-calling-and-documentation-accuracy.md`
**Checklist:** `refactors/refactor26/implementation-checklist.md`

---

## Summary

The implementation covers the majority of the plan correctly. Tool calling support for Messages API (Anthropic) and KeyQuery (Gemini) protocols is well-implemented, documentation fixes are largely accurate, the HTTP 400 error code is in place, streaming robustness wrappers are applied, the `ModelCatalogOverrideDecorator.SupportsModelDiscovery` logic is correct, and the `ChatCompletionRequest` parameter expansion is complete.

Three concrete Errors remain — all missing tests that the checklist explicitly requires. Two Warnings cover stale documentation that the plan's Goal #1 implies but the checklist does not explicitly call out.

**Result: FAIL** — three Errors remain.

---

## Coverage Summary

| Metric | Count |
|---|---|
| Plan requirements (checklist items) | 47 |
| Implemented correctly | 42 |
| Missing | 3 |
| Incorrect | 0 |
| Unknown | 0 |
| **Errors** | **3** |
| **Warnings** | **2** |

---

## Phase-by-phase verification

### Phase 1: README.md documentation fixes — PASS

- `ChatRole.User` → `EChatRole.User` at lines 65, 99 — verified.
- `ChatRole.Tool` → `EChatRole.Tool` at line 109 — verified.
- `IEnumerable<IAIProvider>` → `IAIProviderFactory factory` with `factory.GetProvider("openai")` at line 57 — verified.
- "four protocol implementations" → "five wire protocols and four provider implementations" at line 8 — verified.

### Phase 2: docs/ documentation fixes — PASS (with Warning, see F4)

- `docs/concepts/wire-protocols.md` line 20: "Unknown or empty protocol strings throw `JsonException`" — verified.
- `docs/concepts/wire-protocols.md` line 17: "github-models" replaced with "*(no embedded entry — protocol exists for consumer-supplied providers)*" — verified.
- `docs/concepts/provider-catalog.md`: "github-models" removed — verified (zero hits in docs/).
- `docs/chat/streaming.md` lines 36–42: `ReasoningContent` and `ToolCalls` rows added to the `StreamingChatChunk` property table — verified.
- `docs/chat/tool-calling.md` lines 65–82: `request.Messages.Add(...)` replaced with a `with` expression — verified.

### Phase 3: Messages API tool calling — request mapping — PASS

- `tools` serialization in `MessagesApiProtocol.MapPayload` maps each `ToolDefinition` to `{ name, description, input_schema }` — verified at `MessagesApiProtocol.cs` lines 79–87.
- Assistant messages with `ToolCalls` emit content blocks with `type: "tool_use"` (each with `id`, `name`, `input`) — verified at lines 122–144.
- Tool result messages (`EChatRole.Tool` with `ToolCallId`) emit user messages with `tool_result` content blocks — verified at lines 108–120.
- Tool blocks are not duplicated as text blocks — verified (early returns in `MapContent` prevent fallthrough to regular content mapping).

### Phase 4: Messages API tool calling — response parsing — PASS

- Response content blocks of `type: "tool_use"` are parsed into `ToolCall` instances — verified at lines 247–265.
- `ChatCompletionResponse.ToolCalls` is populated when tool use blocks are present — verified at line 269–270.
- `FinishReason` is read from `stop_reason` (Anthropic returns `"tool_use"`) — verified at lines 280–284. Uses `TryGetProperty` per mandatory rule #9.

### Phase 5: KeyQuery tool calling — request mapping — PASS

- `tools` serialization with `functionDeclarations` array — verified at `KeyQueryWireProtocol.cs` lines 79–93.
- Assistant messages with `ToolCalls` emit `role: "model"` with `functionCall` parts — verified at lines 131–154.
- Tool result messages (`EChatRole.Tool`) emit `role: "function"` with `functionResponse` parts — verified at lines 114–129.

### Phase 6: KeyQuery tool calling — response parsing — PASS

- Response candidates' `content.parts` are scanned for `functionCall` objects — verified at lines 220–242.
- `ChatCompletionResponse.ToolCalls` is populated when function calls are present — verified at lines 245–246.
- Synthetic `ToolCall.Id` generated via `Guid.NewGuid().ToString("N")` — verified at line 237.

### Phase 7: HTTP 400 error code — PASS

- `AiErrorCodes.InvalidRequest = "ai/invalid-request"` added — verified at `AiErrorCodes.cs` line 29.
- HTTP 400 mapped to `AiErrorCodes.InvalidRequest` in `ThrowIfErrorAsync` — verified at `AIProviderBase.cs` line 374.
- Unit test verifying HTTP 400 → `InvalidRequest` — verified at `AIProviderBaseTests.cs` line 51 (`InlineData(400, AiErrorCodes.InvalidRequest, ...)`).

### Phase 8: Streaming robustness — PARTIAL (see F1)

- `OpenAICompatibleProviderBase.StreamAsync` wraps `JsonSerializer.Deserialize<JsonElement>(item)` in try-catch logging warning — verified at `OpenAICompatibleProviderBase.cs` lines 69–78.
- `MessagesApiProvider.StreamAsync` — verified at `MessagesApiProvider.cs` lines 62–71.
- `KeyQueryProvider.StreamAsync` — verified at `KeyQueryProvider.cs` lines 62–71.
- **Missing:** unit test verifying malformed SSE data does not crash the stream.

### Phase 9: ModelCatalogOverrideDecorator.SupportsModelDiscovery — PARTIAL (see F3)

- `SupportsModelDiscovery` delegates to inner when it implements `IModelDiscoveryProvider`, returns true if overrides exist — verified at `ModelCatalogOverrideDecorator.cs` lines 46–48.
- XML doc comment explains the behavior — verified at lines 41–45.
- **Partial:** only one of three required test cases exists. `IdentityMembers_DelegateToInner` (line 46) covers "inner supports discovery." Missing: "inner does not support discovery but overrides exist → true" and "inner does not support discovery and no overrides → false."

### Phase 10: ChatCompletionRequest parameter expansion — PARTIAL (see F2)

- `Stop` (`IReadOnlyList<string>?`, default null) — verified at `ChatCompletionRequest.cs` line 41.
- `TopP` (`float?`, default null) — verified at line 46.
- `FrequencyPenalty` (`float?`, default null) — verified at line 51.
- `PresencePenalty` (`float?`, default null) — verified at line 56.
- Mapped in `OpenAICompatibleWireProtocol.MapRequest` (only when non-null) — verified at lines 111–121.
- **Missing:** unit tests verifying the new parameters are serialized correctly.

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
- **Affected location:** `AIProviderConnectLib.Tests/` — no test file contains this test
- **Evidence:** The try-catch wrappers exist in all three streaming providers (`OpenAICompatibleProviderBase.cs` lines 69–78, `MessagesApiProvider.cs` lines 62–71, `KeyQueryProvider.cs` lines 62–71), each logging a warning and returning null on `JsonException`. Grep for `malformed`, `skipping`, `SSE` across the test project returns zero hits related to streaming robustness.
- **Explanation:** The checklist explicitly requires a test that feeds a malformed SSE data line and verifies the stream continues. The production code is correct but untested.
- **Correction:** Add a test that feeds an SSE sequence containing valid JSON, then a non-JSON line, then valid JSON again, and verifies the stream yields the two valid chunks without throwing.

### F2 — Missing ChatCompletionRequest parameter serialization tests

- **Severity:** Error
- **Plan requirement:** Phase 10 checklist — "Add unit tests verifying the new parameters are serialized correctly in the OpenAI-compatible request body"
- **Affected location:** `AIProviderConnectLib.Tests/Protocols/OpenAICompatibleWireProtocolTests.cs` — no test references `Stop`, `TopP`, `FrequencyPenalty`, or `PresencePenalty`
- **Evidence:** The properties exist on `ChatCompletionRequest` (lines 41–56) and the mapping exists in `OpenAICompatibleWireProtocol.MapRequest` (lines 111–121). Grep for `TopP|FrequencyPenalty|PresencePenalty|\.Stop` across the test project returns zero hits (excluding unrelated `finish_reason: "stop"` values).
- **Explanation:** The checklist explicitly requires tests that set each new parameter and verify it appears in the serialized request body with the correct key name, and that null values are omitted.
- **Correction:** Add tests in `OpenAICompatibleWireProtocolTests` that construct a request with each parameter set, serialize the payload, and verify the JSON contains the expected key (`stop`, `top_p`, `frequency_penalty`, `presence_penalty`). Add a test verifying that null parameters produce no key in the body.

### F3 — Missing two of three SupportsModelDiscovery decorator tests

- **Severity:** Error
- **Plan requirement:** Phase 9 checklist — "Update or add unit tests verifying the three cases: inner supports discovery, inner does not support discovery but overrides exist, inner does not support discovery and no overrides exist"
- **Affected location:** `AIProviderConnectLib.Tests/Providers/ModelCatalogOverrideDecoratorTests.cs`
- **Evidence:** `IdentityMembers_DelegateToInner` (line 46) covers case 1 (inner implements `IModelDiscoveryProvider` with `SupportsModelDiscovery = true`). No test covers case 2 (inner is plain `IAIProvider`, overrides exist → `SupportsModelDiscovery` should be `true`). No test covers case 3 (inner is plain `IAIProvider`, no overrides → `SupportsModelDiscovery` should be `false`).
- **Explanation:** The checklist explicitly requires all three cases. Only one is tested.
- **Correction:** Add two tests: one where inner is a plain `IAIProvider` mock (no `IModelDiscoveryProvider`) with overrides in the store, asserting `SupportsModelDiscovery` is `true`; one where inner is a plain `IAIProvider` mock with an empty store, asserting `SupportsModelDiscovery` is `false`.

### F4 — Stale note in tool-calling.md contradicts new tool calling support

- **Severity:** Warning
- **Plan requirement:** Goal #1 — "Fix all documentation errors so every code example compiles and every claim matches the implementation"
- **Affected location:** `docs/chat/tool-calling.md`, lines 87–89
- **Evidence:** The note reads: *"Tool round trips require the OpenAI wire format. The Messages API and KeyQuery protocols map plain text messages only."* This is now factually incorrect — the implementation adds full tool calling support to both Messages API and KeyQuery protocols.
- **Explanation:** A consumer reading this note would incorrectly believe tool calling is unavailable for Anthropic and Gemini providers. The note directly contradicts the new implementation that this refactor introduces.
- **Correction:** Update or remove the note to reflect that tool calling is now supported on all three protocol families (OpenAI-compatible, Messages API, and KeyQuery).

### F5 — NuGetReadme.md uses stale `ChatRole` enum and obsolete resolution pattern

- **Severity:** Warning
- **Plan requirement:** Goal #1 — "Fix all documentation errors so every code example compiles and every claim matches the implementation"
- **Affected location:** `NuGetReadme.md`, lines 37 and 41
- **Evidence:** Line 37: `var provider = providers.First(p => p.Id == "openai");` uses the obsolete `IEnumerable<IAIProvider>` scan pattern. Line 41: `Role = ChatRole.User` uses the removed enum name (renamed to `EChatRole`). The code example will not compile.
- **Explanation:** The plan's checklist Phase 1 only targets README.md, but `NuGetReadme.md` carries the same stale patterns the plan was designed to fix. This file is the NuGet package readme displayed on nuget.org, making it high-visibility user-facing documentation with non-compiling code.
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
