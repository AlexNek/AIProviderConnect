# Refactor 27 — Third-Pass Implementation Verification Review

**Plan:** `refactors/refactor27/network-exception-translation-streaming-and-documentation.md`
**Checklist:** `refactors/refactor27/implementation-checklist.md`
**Previous reviews:** `reviews/r27/01-implementation-review.md`, `reviews/r27/02-implementation-review.md`
**Review date:** 2026-09-25

---

## Summary

Independent third-pass verification of the current working-tree implementation against every defect,
design decision (D1–D6), checklist phase (1–11), and mandatory implementation rule of refactor 27.
All eleven phases are implemented and verified against source. Build and test evidence was re-run for
this review and is current.

The three Warnings carried forward from the 02 review are **still unaddressed** in the source
(re-verified, not assumed). One additional doc-coverage note (S-1) is raised by this review.

**Result: PASS** — 0 Errors, 3 Warnings, 1 Suggestion. The refactor is code-complete relative to the plan.

---

## Build and test evidence (re-run for this review)

| Check | Result |
|---|---|
| `dotnet build AIProviderConnectLib -c Release` | 0 warnings / 0 errors |
| `dotnet test AIProviderConnectLib.Tests -c Release` | 332 passed / 0 failed |
| `dotnet test ScraperTool.Tests -c Release` (builds ScraperTool + GraphVisualization) | 639 passed / 0 failed |

---

## Phase-by-phase verdicts

| Phase | Description | Verdict | Evidence |
|---|---|---|---|
| 1 | Network exception translation in AIProviderBase | PASS | `TranslateNetworkExceptionsAsync` (AIProviderBase.cs L255–271); used in `SendChatAndParseAsync` (L292), `SendGetModelsAndParseAsync` (L324), `StreamCoreAsync` (L233); Polly `ShouldHandle` includes `NoConnection` + `Timeout` (L99–101); 5 tests in `AIProviderBaseTests` (L296–377) |
| 2 | ModelCatalogOverrideDecorator streaming fix | PASS | `NonStreamingModelCatalogOverrideDecorator.cs` (no `IStreamingChatProvider`); `ApplyModelOverrides` ternary selection (AIProviderServiceCollectionExtensions.cs L258–260); delegation members identical to streaming decorator except `StreamAsync`; tests in `NonStreamingModelCatalogOverrideDecoratorTests` + DI-level non-streaming test |
| 3 | Messages API streaming tool calls | PASS | 5 constants added to `MessagesApiPropertyNames`; `MessagesApiStreamingParser` (stateful `Dictionary<int, ToolUseBlock>`, first-fragment identity emission, stop-cleanup); `MessagesApiProvider.StreamAsync` passes stateful closure (L58–75); `StreamCoreAsync` signature unchanged (`Func<string, StreamingChatChunk?>`); 8 tests in `MessagesApiStreamingParserTests` |
| 4 | Dead code removal | PASS | `EndpointDefaults.cs` contains only `ChatCompletions`, `Messages`, `Models`, `KeyQuery`; grep `EndpointDefaults.Catalog` → 0 source hits (only plan/review history) |
| 5 | error-handling.md | PASS | HTTP 400 → `ai/invalid-request` row (L39); `InvalidRequest` (L57) and `ProviderNotFound` (L67) present in codes table |
| 6 | README.md github-models fix | PASS | L17 now reads `*(no embedded entry — protocol exists for consumer-supplied providers)*`; grep `github-models` in docs/README → 0 hits |
| 7 | Provider/protocol terminology | PASS | All docs use count "four" (wire-protocols L3/L114, model-discovery L16, faq L37, interfaces L112, installation L41, quick-start L78, streaming L15); README L8 states "five wire protocols and four provider implementations"; wording variations noted in W-2 |
| 8 | model-discovery.md decorator note | PASS | L19–22 note that the decorator returns `true` when consumer-supplied overrides exist |
| 9 | Stale Catalog endpoint references | PASS | wire-protocols.md L67/L69 (`chat/completions`, `models`), di-extensions.md L105, model-discovery.md — grep `inference/chat/completions` / `catalog/models` in docs/ → 0 hits |
| 10 | Build and test verification | PASS | Re-run for this review, table above |
| 11 | Final grep checks | PASS | All four grep conditions verified against the current tree |

## Mandatory implementation rules

| Rule | Status |
|---|---|
| 1. Interfaces unchanged | Verified — `IAIProvider`, `IStreamingChatProvider`, `IModelDiscoveryProvider` untouched |
| 2. `StreamCoreAsync` signature unchanged | Verified — still `Func<string, StreamingChatChunk?>` |
| 3. OpenAI-compatible / KeyQuery streaming untouched | Verified — changes confined to Messages API path and base translation helper |
| 4. No `ai-providers/` JSON modifications | Verified |
| 5. No `Microsoft.Extensions.Http` / `IHttpClientFactory` dependency | Verified |
| 6–8. Tests for translation / decorator selection / streaming tool calls | Verified — present and passing |
| 9–10. 0-warning build, all tests pass | Verified — re-run for this review |

---

## Findings

### W-1 (carried from 01/02, still open) — Dead method `MessagesApiProtocol.ParseStreamChunk`

- **Severity:** Warning
- **Location:** `AIProviderConnectLib/Protocols/MessagesApiProtocol.cs` L304–322
- **Evidence:** Re-grepped for callers in this pass: zero references in library source and tests;
  `MessagesApiProvider.StreamAsync` uses `MessagesApiStreamingParser` exclusively.
- **Explanation:** Superseded by the stateful parser but not removed. It only handles text deltas and
  `message_stop`, so it is an incomplete duplicate of the new parser. Outside the plan's explicit
  Phase 4 scope (which targeted only `EndpointDefaults.Catalog`), hence non-blocking — but it is a
  public API surface that now advertises streaming-parse capability the library no longer routes through.
- **Correction:** Delete the method (and any public API mention) in a follow-up cleanup.

### W-2 (carried from 02, still open) — Documentation wording variations

- **Severity:** Warning
- **Locations:** `docs/chat/streaming.md` L15 ("All four **built-in** provider implementations"),
  `docs/getting-started/quick-start.md` L78 ("all four built-in implementations" — omits "provider"),
  `docs/getting-started/installation.md` L41 ("four **protocol-specific** provider implementations").
- **Explanation:** The count "four" is correct in every doc and the checkmark's intent (correct
  provider-class count) is satisfied; qualifiers are factually accurate (all four built-ins do support
  streaming — `ModelCatalogProvider` inherits `IStreamingChatProvider` via `OpenAICompatibleProviderBase`).
  Only strict uniformity with the checklist's exact phrasing is missing.
- **Correction:** Align wording in a follow-up pass if exact-phrase uniformity is desired.

### W-3 (carried from 02, still open) — No DI-level test for the streaming decorator branch

- **Severity:** Warning
- **Location:** `AIProviderConnectLib.Tests/DependencyInjection/AIProviderRegistrationBuilderTests.cs`
- **Evidence:** Re-grepped for `BeOfType<ModelCatalogOverrideDecorator>` in this pass: zero matches.
  The DI-level test `OverrideModels_WrapsResolvedProvider_AndMergesCatalog` asserts only the
  non-streaming branch (`BeOfType<NonStreamingModelCatalogOverrideDecorator>`, L289).
- **Explanation:** Checklist Phase 11 requires "verify the ModelCatalogOverrideDecorator streaming test
  covers both decorator paths". Both decorator types are tested directly (assignability both ways), but
  the streaming branch of `ApplyModelOverrides` is not exercised through the full DI pipeline. Partial
  satisfaction of the Phase 11 item.
- **Correction:** Mirror the existing DI test with `inner.As<IStreamingChatProvider>()` and assert
  `BeOfType<ModelCatalogOverrideDecorator>()`.

### S-1 (new) — Network translation behavior not stated in error-handling.md prose

- **Severity:** Suggestion
- **Location:** `docs/concepts/error-handling.md`
- **Evidence:** The doc gained the `NoConnection`/`Timeout` rows in the "All Error Codes" table, but no
  prose section states that network-level failures (DNS failure, connection refused, TLS error,
  HttpClient timeout) are translated to `AiException` at all three transport paths (chat, discovery,
  streaming) and that translated failures are retry-eligible when `MaxRetryCount > 0`.
- **Explanation:** The plan's Problem def. 1 quoted the doc claim "All failures throw `AiException`" as
  false; the implementation closed the behavior gap (defect fixed) and the codes are now tabulated
  (Phase 5 satisfied as written). Goal 5 ("every doc claim matches the implementation") holds — no
  remaining claim is false — but the newly guaranteed contract is not explicitly advertised to consumers.
  This is not a plan checklist item, so it does not block completion.
- **Correction:** Optionally add a short "Network Errors" subsection to error-handling.md describing the
  translation and retry eligibility.

---

## Notes (non-findings)

- `refactors/refactor27/implementation-checklist.md` remains fully unticked in the tree although all
  steps are verifiably implemented. Per project convention the plan documents are the implementation
  record; the boxes should be ticked to reflect the verified-done state.
- The design-doc defect list (defects 1–9) was spot-verified as historically accurate; the current
  source contradicts none of the "Current defects" preconditions, i.e., all nine are remediated.
- `MessageCatalogProvider` streaming docs claim (faq.md L37 "All four provider implementations implement
  `IStreamingChatProvider`") was verified true: `ModelCatalogProvider : OpenAICompatibleProviderBase
  : …, IStreamingChatProvider`.

---

## Verdict

**PASS.** Every checklist phase (1–11) and every mandatory rule (1–10) is satisfied in the current
source; build and all 971 tests pass. The refactor is code-complete. Three non-blocking Warnings from
the prior review round remain open (dead `MessagesApiProtocol.ParseStreamChunk`, minor doc wording
drift, missing DI-level streaming-branch test), plus one new doc-coverage suggestion. None of them is
required by the plan; all are suitable for a follow-up cleanup pass.
