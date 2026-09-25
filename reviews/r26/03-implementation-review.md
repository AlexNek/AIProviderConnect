# Refactor 26 — Implementation Review (Pass 03)

Review of the Refactor 26 implementation against plan x1
(`refactors/refactor26/protocol-tool-calling-and-documentation-accuracy.md`)
and companion checklist (`implementation-checklist.md`).

**Scope:** All 13 implementation phases, mandatory rules, and verification steps.

**Method:** Source inspection of every cited file; build and test execution;
grep sweeps across `docs/`, `README.md`, and both test projects.

---

## Build & Test Evidence

| Check | Result |
|---|---|
| `dotnet build AIProviderConnectLib` (Release, no-incremental) | 0 warnings, 0 errors |
| `dotnet test AIProviderConnectLib.Tests` (Release) | 300 passed, 0 failed |
| `dotnet build ScraperTool` (Release, no-incremental) | 0 warnings, 0 errors |
| `dotnet test ScraperTool.Tests` (Release) | 639 passed, 0 failed |

---

## Phase-by-Phase Verification

### Phase 1. README.md documentation fixes — COMPLETE

| Requirement | Status | Evidence |
|---|---|---|
| `ChatRole.User` → `EChatRole.User` (lines 65, 99) | ✅ | `README.md` L65, L99: `EChatRole.User` |
| `ChatRole.Tool` → `EChatRole.Tool` (line 109) | ✅ | `README.md` L109: `EChatRole.Tool` |
| `IEnumerable<IAIProvider>` → `IAIProviderFactory` (line 57) | ✅ | `README.md` L57: `public class MyService(IAIProviderFactory factory)` with `factory.GetProvider("openai")` |
| Clarify protocol count (line 8) | ✅ | `README.md` L8: "five wire protocols and four provider implementations" |

### Phase 2. docs/ documentation fixes — INCOMPLETE

| Requirement | Status | Evidence |
|---|---|---|
| wire-protocols.md L20: "fall back" → "throw" | ✅ | L20: "Unknown or empty protocol strings throw `JsonException`." |
| wire-protocols.md L17: remove "github-models" | ✅ | L17: "*(no embedded entry — protocol exists for consumer-supplied providers)*" |
| provider-catalog.md: remove "github-models" | ✅ | Grep `github-models` in `docs/` returns zero hits |
| streaming.md: add `ReasoningContent` + `ToolCalls` | ✅ | `streaming.md` L39-40: both rows present in the property table |
| tool-calling.md: replace `.Messages.Add(...)` with `with` | ✅ | `tool-calling.md` L65-82: uses `request = request with { Messages = [..request.Messages, ...] }` |
| tool-calling.md: stale "OpenAI-only" note | ❌ | L87-89: "Tool round trips require the OpenAI wire format. The Messages API and KeyQuery protocols map plain text messages only." — directly contradicts Phases 3-6 |

### Phase 3. Messages API tool calling — request mapping — COMPLETE

| Requirement | Status | Evidence |
|---|---|---|
| `tools` serialization (`{name, description, input_schema}`) | ✅ | `MessagesApiProtocol.cs` L79-87 |
| Assistant messages with `ToolCalls` → `tool_use` blocks | ✅ | `MessagesApiProtocol.cs` L122-144 |
| Tool result messages → user with `tool_result` blocks | ✅ | `MessagesApiProtocol.cs` L108-120 |
| Tool blocks filtered from regular content mapping | ✅ | Branch structure in `MapContent` ensures mutual exclusion |

### Phase 4. Messages API tool calling — response parsing — COMPLETE

| Requirement | Status | Evidence |
|---|---|---|
| Parse `tool_use` content blocks → `ToolCall` | ✅ | `MessagesApiProtocol.cs` L247-265 |
| Populate `ChatCompletionResponse.ToolCalls` | ✅ | `MessagesApiProtocol.cs` L269-270, L296 |
| `FinishReason` set from `stop_reason` | ✅ | `MessagesApiProtocol.cs` L280-284 (reads native `stop_reason`, which is `"tool_use"` when tools are called) |

### Phase 5. KeyQuery tool calling — request mapping — COMPLETE

| Requirement | Status | Evidence |
|---|---|---|
| `tools` serialization (`functionDeclarations`) | ✅ | `KeyQueryWireProtocol.cs` L79-93 |
| Assistant → model with `functionCall` parts | ✅ | `KeyQueryWireProtocol.cs` L131-154 |
| Tool result → function with `functionResponse` | ✅ | `KeyQueryWireProtocol.cs` L114-129 |

### Phase 6. KeyQuery tool calling — response parsing — COMPLETE

| Requirement | Status | Evidence |
|---|---|---|
| Parse `functionCall` from response parts | ✅ | `KeyQueryWireProtocol.cs` L226-241 |
| Populate `ChatCompletionResponse.ToolCalls` | ✅ | `KeyQueryWireProtocol.cs` L245-246, L261 |
| Synthetic `ToolCall.Id` | ✅ | `KeyQueryWireProtocol.cs` L237: `Guid.NewGuid().ToString("N")` |

### Phase 7. HTTP 400 error code — COMPLETE

| Requirement | Status | Evidence |
|---|---|---|
| `AiErrorCodes.InvalidRequest = "ai/invalid-request"` | ✅ | `AiErrorCodes.cs` L29 |
| HTTP 400 → `InvalidRequest` in `ThrowIfErrorAsync` | ✅ | `AIProviderBase.cs` L374: `400 => AiErrorCodes.InvalidRequest` |
| Unit test | ✅ | `AIProviderBaseTests.cs` L51: `InlineData(400, AiErrorCodes.InvalidRequest, ...)` |

### Phase 8. Streaming robustness — INCOMPLETE

| Requirement | Status | Evidence |
|---|---|---|
| `OpenAICompatibleProviderBase.StreamAsync` try-catch | ✅ | `OpenAICompatibleProviderBase.cs` L69-78 |
| `MessagesApiProvider.StreamAsync` try-catch | ✅ | `MessagesApiProvider.cs` L62-71 |
| `KeyQueryProvider.StreamAsync` try-catch | ✅ | `KeyQueryProvider.cs` L60-71 |
| Malformed SSE data unit test | ❌ | Grep for `malformed`, `skipping.*malformed`, `SSE.*data`, `JsonException.*stream` across both test projects returns zero relevant hits |

All three providers correctly wrap `JsonSerializer.Deserialize<JsonElement>(item)` in a
try-catch that logs a warning and returns `null`. However, the plan explicitly requires
a unit test proving the behavior.

### Phase 9. ModelCatalogOverrideDecorator.SupportsModelDiscovery — INCOMPLETE

| Requirement | Status | Evidence |
|---|---|---|
| Delegate to inner when `IModelDiscoveryProvider` | ✅ | `ModelCatalogOverrideDecorator.cs` L46-48 |
| Return `true` only if inner supports OR overrides exist | ✅ | Same expression: `(_inner is IModelDiscoveryProvider discovery && discovery.SupportsModelDiscovery) \|\| _store.Get(_inner.Id).Count > 0` |
| XML doc comment | ✅ | L41-45: explains "true means either the inner provider has a live discovery API or the consumer has registered override entries" |
| Test: inner supports discovery | ✅ | `IdentityMembers_DelegateToInner` (L47-63): inner implements `IModelDiscoveryProvider` with `SupportsModelDiscovery = true` |
| Test: inner doesn't support, overrides exist → `true` | ❌ | No test asserts `SupportsModelDiscovery` is `true` when inner lacks `IModelDiscoveryProvider` but overrides exist |
| Test: inner doesn't support, no overrides → `false` | ❌ | No test asserts `SupportsModelDiscovery` is `false` when inner lacks `IModelDiscoveryProvider` and store is empty |

### Phase 10. ChatCompletionRequest parameter expansion — INCOMPLETE

| Requirement | Status | Evidence |
|---|---|---|
| `Stop` (`IReadOnlyList<string>?`, default `null`) | ✅ | `ChatCompletionRequest.cs` L41 |
| `TopP` (`float?`, default `null`) | ✅ | `ChatCompletionRequest.cs` L46 |
| `FrequencyPenalty` (`float?`, default `null`) | ✅ | `ChatCompletionRequest.cs` L51 |
| `PresencePenalty` (`float?`, default `null`) | ✅ | `ChatCompletionRequest.cs` L56 |
| Map in `OpenAICompatibleWireProtocol.MapRequest` | ✅ | L111-121: all four mapped conditionally (only when non-null) |
| Unit tests for new parameter serialization | ❌ | No test in `OpenAICompatibleWireProtocolTests.cs` references `Stop`, `TopP`, `FrequencyPenalty`, or `PresencePenalty` |

### Phase 11. Tool calling tests — COMPLETE

All 8 required test methods exist:

| Test | File |
|---|---|
| Messages API: tool definition serialization | `MessagesApiProtocolTests.MapRequest_WithTools_SerializesToolsArray` |
| Messages API: assistant tool_use blocks | `MessagesApiProtocolTests.MapRequest_AssistantWithToolCalls_SerializesToolUseBlocks` |
| Messages API: tool result mapping | `MessagesApiProtocolTests.MapRequest_ToolResultMessage_SerializesAsUserWithToolResultBlock` |
| Messages API: response tool_use parsing | `MessagesApiProtocolTests.ParseResponse_WithToolUseBlocks_PopulatesToolCalls` |
| KeyQuery: tool definition serialization | `KeyQueryWireProtocolTests.MapRequest_WithTools_SerializesFunctionDeclarations` |
| KeyQuery: model-role functionCall | `KeyQueryWireProtocolTests.MapRequest_AssistantWithToolCalls_SerializesFunctionCallParts` |
| KeyQuery: function-role functionResponse | `KeyQueryWireProtocolTests.MapRequest_ToolResultMessage_SerializesAsFunctionRoleWithFunctionResponse` |
| KeyQuery: response functionCall parsing | `KeyQueryWireProtocolTests.ParseResponse_WithFunctionCall_PopulatesToolCalls` |

### Phase 12. Build and test verification — COMPLETE

All four build/test checks pass (see table at top of this document).

### Phase 13. Final documentation review — INCOMPLETE

| Check | Result |
|---|---|
| Grep `ChatRole\.` across `docs/` and `README.md` | ✅ Zero stale hits (all use `EChatRole.`) |
| Grep `IEnumerable<IAIProvider>` across `docs/` and `README.md` | ✅ Zero hits |
| Grep `github-models` across `docs/` | ✅ Zero hits |
| tool-calling.md note accuracy | ❌ L87-89 still claims tool calling is OpenAI-only |

---

## Mandatory Rule Compliance

| Rule | Status | Notes |
|---|---|---|
| R1. Do not change `IAIProvider`, `IStreamingChatProvider`, `IModelDiscoveryProvider` | ✅ | No changes to any abstraction interface |
| R2. Do not modify OpenAI-compatible tool calling | ✅ | `OpenAICompatibleWireProtocol.cs` tool calling code unchanged |
| R3. Do not add tool calling to Catalog protocol | ✅ | `CatalogWireProtocol.cs` unchanged |
| R4. Do not change request/response record structures beyond specified properties | ✅ | Only four additive optional properties added to `ChatCompletionRequest` |
| R5. Do not introduce breaking changes | ✅ | All changes are additive |
| R6. Do add unit tests for every new protocol feature | ❌ | Missing: streaming robustness test, `SupportsModelDiscovery` decorator cases, new parameter serialization |
| R7. Do verify documentation code examples compile | ✅ | README.md, tool-calling.md, quick-start.md examples use current API |
| R8. Do use existing `ToolCall`, `ToolDefinition`, `ChatMessage` models | ✅ | No protocol-specific tool models created |
| R9. Do handle null/missing fields gracefully with `TryGetProperty` | ✅ | All protocol parsing uses `TryGetProperty` for optional fields |
| R10. Do log warnings for malformed SSE data | ✅ | All three providers log `LogWarning` and return `null` |

---

## Findings

### F1 — Missing malformed-SSE streaming robustness test

- **Severity:** Error
- **Plan section:** Phase 8 checklist item; Mandatory Rule 6
- **Affected location:** No test file covers streaming robustness in `AIProviderConnectLib.Tests/Providers/`
- **Evidence:** Grep for `malformed`, `skipping.*malformed`, `SSE.*data`, `JsonException.*stream` across both test projects returns zero relevant hits
- **Explanation:** The try-catch wrapper was correctly implemented in all three streaming providers (`OpenAICompatibleProviderBase.cs` L69-78, `MessagesApiProvider.cs` L62-71, `KeyQueryProvider.cs` L60-71), but the plan explicitly requires "a unit test verifying that a malformed SSE data line does not crash the stream (the chunk is skipped and streaming continues)." No such test exists.
- **Correction:** Add a test that feeds a mix of valid and malformed SSE data lines through a provider's `StreamAsync` (using a scripted `HttpMessageHandler`) and asserts the valid chunks are yielded while the malformed ones are skipped without throwing.

### F2 — Missing `SupportsModelDiscovery` decorator test cases

- **Severity:** Error
- **Plan section:** Phase 9 checklist: "Update or add unit tests verifying the three cases"
- **Affected location:** `AIProviderConnectLib.Tests/Providers/ModelCatalogOverrideDecoratorTests.cs`
- **Evidence:** Only `IdentityMembers_DelegateToInner` (L47-63) asserts `SupportsModelDiscovery`, and only for the "inner supports discovery" case. The other two required cases are absent:
  - Inner does not implement `IModelDiscoveryProvider`, overrides exist → `SupportsModelDiscovery` should be `true`
  - Inner does not implement `IModelDiscoveryProvider`, no overrides → `SupportsModelDiscovery` should be `false`
- **Explanation:** The plan requires three explicit test cases for the changed `SupportsModelDiscovery` behavior. Only one of three is covered.
- **Correction:** Add two tests asserting `SupportsModelDiscovery` for the remaining cases.

### F3 — Missing `ChatCompletionRequest` new parameter serialization tests

- **Severity:** Error
- **Plan section:** Phase 10 checklist: "Add unit tests verifying the new parameters are serialized correctly in the OpenAI-compatible request body"; Mandatory Rule 6
- **Affected location:** `AIProviderConnectLib.Tests/Protocols/OpenAICompatibleWireProtocolTests.cs`
- **Evidence:** The 627-line test file covers stream chunks, response parsing, and multimodal mapping, but no test references `Stop`, `TopP`, `FrequencyPenalty`, or `PresencePenalty`. The properties and mapping are correctly implemented (`OpenAICompatibleWireProtocol.cs` L111-121), but the required tests are absent.
- **Explanation:** The plan explicitly requires unit tests for the new parameter serialization.
- **Correction:** Add a test constructing a `ChatCompletionRequest` with all four new parameters set, calling `MapRequest`, and verifying the JSON body contains `stop`, `top_p`, `frequency_penalty`, `presence_penalty` with correct values. Also add a test verifying null parameters produce no keys in the body.

### F4 — Stale tool-calling.md note contradicts new tool calling support

- **Severity:** Warning
- **Plan section:** Goal 1 ("Fix all documentation errors so every claim matches the implementation"); D7 ("Documentation accuracy")
- **Affected location:** `docs/chat/tool-calling.md` L87-89
- **Evidence:** The note reads: "Tool round trips require the OpenAI wire format. The Messages API and KeyQuery protocols map plain text messages only." This was true before this refactor but is now false — Phases 3-6 added tool calling to both Messages API and KeyQuery.
- **Explanation:** A consumer reading this note would incorrectly believe tool calling only works with OpenAI-compatible providers, directly contradicting the new functionality.
- **Correction:** Remove the note or update it to reflect that tool calling is now supported on OpenAI-compatible, Messages API, and KeyQuery protocols.

---

## Coverage Summary

| Metric | Count |
|---|---|
| Plan phases | 13 |
| Phases fully complete (code + tests) | 8 (Phases 1, 3, 4, 5, 6, 7, 11, 12) |
| Phases with code complete but tests missing | 3 (Phases 8, 9, 10) |
| Phases with documentation gap | 1 (Phase 2/13 — stale note) |
| Mandatory rules satisfied | 9 of 10 |
| Mandatory rules violated | 1 (Rule 6 — missing tests) |
| **Errors** | **3** (F1, F2, F3) |
| **Warnings** | **1** (F4) |

---

## Final Result

**FAIL** — three concrete Errors remain.

All production code is correctly implemented and the build is clean (0 warnings,
0 errors; 300 + 639 tests pass). The tool calling features for Messages API and
KeyQuery are well-structured and follow the design decisions. However, Mandatory
Rule 6 ("Do add unit tests for every new protocol feature") is violated in three
areas: streaming robustness (Phase 8), `SupportsModelDiscovery` decorator cases
(Phase 9), and new `ChatCompletionRequest` parameter serialization (Phase 10).
Additionally, `tool-calling.md` carries a stale note that contradicts the newly
added tool calling support (Warning F4).
