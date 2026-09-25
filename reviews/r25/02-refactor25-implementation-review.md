# Refactor 25 — Implementation Review (round 02)

## Scope

Review the **current implementation** against refactoring plan **x1**:
`refactors/refactor25/ai-provider-connect-lib-clean-code-consolidation.md`

Purpose: determine whether the implementation correctly and completely implements plan x1.

Review criteria applied: plan compliance, functional correctness, codebase consistency,
architecture, completeness, scope, tests. Only concrete problems are reported; unverifiable
items are marked `Unknown`.

## Baseline

| Item | Value |
|---|---|
| Implementation commit | `d0cf981 "r25 implementation"` (42 files, +549 / −391) |
| Predecessor | `215bafe update 7 refactor 25` |
| Working tree | clean (only `reviews/r25/` untracked) |
| Plan document | 551 lines — 17 defects, design decisions D1–D10, Compatibility, Mandatory rules, "Do not:" |
| Completion record | `refactors/refactor25/implementation-checklist.md` (Phases 1–15) |

## Build & test evidence

| Check | Result |
|---|---|
| `dotnet build AIProviderConnect.sln` | 0 errors; 2 pre-existing unrelated `CS8604` warnings in `ScraperTool/Services/AiUrlFixService.cs:1208` (not touched by r25) |
| `dotnet test AIProviderConnectLib.Tests` | 256 passed |
| `dotnet test ScraperTool.Tests` | 639 passed |

### Grep verification — deleted / renamed symbols

Repo-wide grep for every symbol the plan removes or renames, restricted to live code:

| Symbol | Hits in live code |
|---|---|
| `HybridGatewayProvider` (class, not options) | 0 |
| `AuthSchemes` | 0 |
| `HeaderNames` | 0 |
| `ChatEndpointPattern` | 0 |
| `StreamEndpointPattern` | 0 |
| `DefaultMaxTokens` | 0 |
| `AIProviderOptions.ProviderId` (`options.ProviderId` / `o.ProviderId`) | 0 in code (all remaining `ProviderId =` hits belong to `AIModel.ProviderId`, ScraperTool models, EF migrations — unrelated types) |
| `"apiKeyHeaderName"` literal outside `ProtocolParsingHelpers` | 0 |

---

## Design-decision verification

### D1 — Transport template consolidation ✅

- `AIProviderBase` exposes `SendChatAndParseAsync` / `SendGetModelsAndParseAsync`, each wrapped
  in `pipeline.ExecuteAsync` with a `try/catch` that calls `Logger.LogError`.
- `OpenAICompatibleProviderBase.ChatAsync` / `GetModelsAsync` now delegate to those helpers
  (private `_chatEndpoint` / `_modelsEndpoint` fields supplied by the base).
- `StreamCoreAsync` unchanged. `ThrowIfErrorAsync` unchanged (error-mapping behavior preserved).

### D2 — Shared protocol parsing helpers ✅

- `ProtocolParsingHelpers.cs` is `internal static class` under `Protocols/` (correctly not public).
- Contains `ApiKeyHeaderNameKey`, `SafeGetString`, `TryExtractApiKeyHeaderName`,
  `MapContentParts<T>`, `ParseModelArray` (filter `!string.IsNullOrWhiteSpace(x.Id)`,
  returns `[]` on missing/non-array root).
- All four `ParseModels` implementations route through `ParseModelArray` + `SafeGetString`.
- `ImageContent.ResolveSource()` + `ImageSource` (public abstract record with nested
  `UrlSource` / `Base64Source`) faithfully encapsulate the `Data is { Length: > 0 }` branch.

### D3 — Concrete-provider consolidation ✅

- `HybridGatewayProvider.cs` deleted.
- `OpenAICompatibleProvider` ctor widened to `AIProviderOptions`; endpoint overrides via
  `options as IChatAndModelsEndpointOptions ?? throw new ArgumentException(...)`.
- `ChatEndpoint` / `ModelsEndpoint` are `protected` properties backed by private fields
  (abstract removed); `_options` field removed from subclasses.
- `ModelCatalogProvider` keeps its typed options and only the `ParseModels` override.
- `IChatAndModelsEndpointOptions` remains `internal` with exactly the two sealed implementing
  option types — cast-at-construction approach is sound.

### D4 — Magic-string elimination ✅

- `OpenAICompatiblePropertyNames` gains `JsonSchema = "json_schema"` / `Function`.
- `OpenAICompatibleWireProtocol` uses `JsonSchema` for the input discriminator and `type =`
  value; role comparisons route through `EChatRole.Assistant/Tool.ToString().ToLowerInvariant()`.
- Output wire literals `"text"` / `"image_url"` correctly retained.

### D5 — Data-driven error extraction ✅

- `private static readonly string[] ErrorMessagePaths = ["error.message", "message"]`;
  path-walking `TryExtractJsonMessage` preserves the old behavior exactly (both require
  `JsonValueKind.String` and non-whitespace).

### D6 — Minor cleanups / caching ✅

- `AuthSchemes` / `HeaderNames` deleted; `"Bearer"` and `"Accept"` inlined as local consts.
- `ProviderCatalog` parameterless ctor inlined (no `this(Enumerable.Empty<...>)`).
- `EProviderProtocol.Native` XML doc added.
- Lazy `IReadOnlyList<T>` snapshots (`_allSnapshot`, `_withModelDiscoverySnapshot`,
  `_withDynamicCatalogSnapshot`) as mutable (non-`readonly`) fields, rebuilt under `lock`;
  `InvalidateSnapshots()` called from `Merge` and inside `ReloadFromDisk`'s mutation lock.
  `Upsert` is only reached from those two, so invalidation is complete. `GetByCategory` left dynamic.

### D7 — ProviderId SRP cleanup ✅

- `ProviderId` removed from `AIProviderOptions`; `AIProviderBase` takes `string providerId`
  and stores `protected string ProviderId { get; }`.
- `SetApiKeyHeader` converted from `static` to `protected` instance method using `ProviderId`.
- DI `Register` factory passes `providerId`; `.Configure(o => o.ProviderId = providerId)` removed.
- `SeedFromDefinition` `case KeyQueryOptions` unchanged (seeds only `ModelsEndpoint`;
  `KeyQueryOptions` does not implement the interface). Native throws — per plan.

### D8 — Logging integration ⚠ (requirement gap — see W-4)

- `Microsoft.Extensions.Logging.Abstractions` 10.0.12 referenced; `protected ILogger Logger`
  defaults to `NullLogger.Instance`; Debug/Warning/Error levels wired in the transport template.
- No `AddLogging()` inside the library; DI resolves `ILogger<TOptions>` via
  `sp.GetRequiredService<ILogger<TOptions>>()` — see W-4.

### D9 — Retry support ⚠ (partial — see W-2, W-3)

- `Polly.Core` referenced; `ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions{...})`
  with `DelayBackoffType.Exponential`, predicate on `AiException` codes `RateLimited` / `NoServer`;
  both non-streaming transport helpers wrapped; `StreamAsync` not wrapped; `ThrowIfErrorAsync`
  untouched. Correctly returns `ResiliencePipeline.Empty` when `MaxRetryCount <= 0`
  (equivalent to a zero-retry pipeline).
- `OnRetry` log template argument mismatch → W-2; 429 Warning coverage gap when retries
  disabled → W-3.

### D10 — DefaultMaxTokens removal ✅

- `MessagesApiOptions.DefaultMaxTokens` and its `<remarks>` seeding note removed;
  `MapRequest` / `MapStreamRequest` dropped the parameter and emit `[MaxTokens] = request.MaxTokens`.

### D7 (ScraperTool) — consumer factory ✅

- `AIProviderFactory` all six switch arms pass `_catalog, definition.Id`; no `ProviderId =`
  initializers; HybridGateway arm constructs `new OpenAICompatibleProvider(http, new
  HybridGatewayProviderOptions {...}, _catalog, definition.Id)`.

### Test-project migration ✅

- `HybridGatewayProviderTests.cs` → `OpenAICompatibleProviderTests.cs` (88% similarity, all
  three `[InlineData]` operations kept, `AuthSchemes.Bearer` → `"Bearer"`).
- Renames applied in `KeyQueryWireProtocolTests`, `KeyQueryProviderTests`,
  `AIProviderRegistrationBuilderTests`; `CatalogWireProtocolTests` uses `"Accept"`;
  `MessagesApiProtocolTests` calls `MapRequest(request)`; ProviderId initializers removed.
- Test csproj gained `Microsoft.Extensions.Logging` 10.0.12; `AddLogging()` wired in the two
  registration-builder test spots.

---

## Findings

### E-1 — Required new unit tests for the consolidated behavior are absent

- **Severity**: Error
- **Plan x1 requirement**: "Mandatory implementation rules" — *"Add new unit tests for
  `ProtocolParsingHelpers`, retry behavior, and logging output."*
- **Affected file / location**: `AIProviderConnectLib.Tests/` (no new test files added anywhere).
- **Evidence**: Glob of the test project (25 files) contains no `ProtocolParsingHelpersTests.cs`;
  grep of both test projects for `ProtocolParsingHelpers`, `ResolveSource`, `MaxRetryCount`,
  `RetryDelay`, `NullLogger`, `ILogger`, `ImageSource` returns zero hits. `AIProviderBaseTests.cs`
  only carries status-code mapping `[InlineData]` rows (e.g. `[InlineData(429, AiErrorCodes.RateLimited, ...)]`).
- **Explanation**: The plan mandates dedicated tests for three newly consolidated surfaces:
  the shared `internal` parsing helpers, Polly retry behavior, and logger wiring. None exist.
  Existing tests exercise these code paths only indirectly and cannot cover the specified
  edge/failure scenarios, so the "completely implements plan x1" goal is not met.
- **Required correction**: Add
  1. `ProtocolParsingHelpersTests.cs` — `SafeGetString` (present/null/wrong-kind),
     `TryExtractApiKeyHeaderName` (null config, missing key, empty value), `ParseModelArray`
     (missing/non-array root → `[]`, whitespace-`Id` filtering), `MapContentParts<T>`.
  2. Retry tests — 429 and 5xx retried only when `MaxRetryCount > 0`; 400/401/403/404 and
     `StreamAsync` never retried; zero requests issued when `MaxRetryCount == 0`.
  3. A test asserting `NullLogger.Instance` is used when no logger is supplied.

### W-1 — MessagesApi `display_name` present-but-empty no longer preserves the empty string

- **Severity**: Warning
- **Plan x1 requirement**: Compatibility — *"All error mapping and model parsing produce
  identical results."* (D2 helper migration must be behavior-preserving.)
- **Affected file / location**: `AIProviderConnectLib/Protocols/MessagesApiProtocol.cs`,
  `ParseModels` `DisplayName` assignment.
- **Evidence**: New code is
  `DisplayName = !string.IsNullOrEmpty(SafeGetString(x, DisplayName)) ? SafeGetString(x, DisplayName) : SafeGetString(x, Id)`.
  The pre-refactor logic was
  `TryGetProperty(display_name) ? GetString() ?? "" : TryGetProperty(id) ? GetString() ?? "" : ""` —
  i.e. a `display_name` that is present but `""` yielded `""`, not a fallback to `id`.
- **Explanation**: Routing the fallback through `IsNullOrEmpty` (rather than property presence)
  changes the result for the specific case "present but empty `display_name`": that model now
  reports the `id` as its display name instead of `""`. This is a behavioral deviation from the
  "identical results" guarantee, not exercised by any test.
- **Required correction**: Either make the fallback conditional on property *presence*
  (`TryGetProperty(display_name)`) rather than emptiness to match the prior semantics, or update
  the plan's Compatibility guarantee to acknowledge the intentional change.

### W-2 — `OnRetry` log template placeholders do not line up with the supplied arguments

- **Severity**: Warning
- **Plan x1 requirement**: D9 — *"`OnRetry` logs the retry attempt at `Warning` level."*
- **Affected file / location**: `AIProviderConnectLib/Providers/AIProviderBase.cs`,
  `BuildResiliencePipeline` → `OnRetry`.
- **Evidence**: Template `"Provider '{ProviderId}': retry {Attempt}/{MaxAttempts} after {Delay}ms — {Code}"`
  is called with 5 arguments where the value bound to `{Delay}` position is
  `args.Outcome.Exception is AiException aiEx ? aiEx.Code : "unknown"` and `{Code}` position
  receives `args.Outcome.Exception?.Message`. There is no `ms` delay value passed.
- **Explanation**: The rendered message is misordered/mislabeled (e.g. `after RateLimitedms —
  HTTP 429: ...`). The retry-attempt log does not convey what it claims, undermining the
  D9 requirement's intent and making retry diagnostics misleading.
- **Required correction**: Reorder/repair the format placeholders so each argument is logged
  under the correct name (attempt number, max attempts, delay, code, message).

### W-3 — 429 responses are never logged at `Warning` when retries are disabled

- **Severity**: Warning
- **Plan x1 requirement**: D8 — *"Log at `Warning` level for: rate-limit responses (429), retry attempts."*
- **Affected file / location**: `AIProviderConnectLib/Providers/AIProviderBase.cs`,
  `BuildResiliencePipeline` (returns `ResiliencePipeline.Empty` when `MaxRetryCount <= 0`)
  and the `Warning` logging site (`OnRetry`).
- **Evidence**: The only `Warning`-level retry log lives inside `OnRetry`. With the default
  `MaxRetryCount == 0`, the pipeline is `ResiliencePipeline.Empty`, so `OnRetry` never runs and a
  429 surfaces only as the outer `Error` log.
- **Explanation**: The plan requires 429s to be logged at `Warning` independently of retry
  attempts; the current wiring satisfies it only when retry is enabled. The `Warning` requirement
  is therefore only partially implemented.
- **Required correction**: Emit the `Warning` for a rate-limit (`AiErrorCodes.RateLimited`)
  outcome regardless of `MaxRetryCount`, keeping the per-attempt retry `Warning` separate.

### W-4 — DI resolves `ILogger<TOptions>` with `GetRequiredService`, silently requiring consumer `AddLogging()`

- **Severity**: Warning
- **Plan x1 requirement**: Compatibility — the plan presents D8 logging as a non-breaking
  addition ("no behavioral change" for existing consumers).
- **Affected file / location**: `AIProviderConnectLib/DependencyInjection/AIProviderServiceCollectionExtensions.cs`,
  `Register<TOptions,TProvider>` factory: `sp.GetRequiredService<ILogger<TOptions>>()`.
- **Evidence**: The library does not call `AddLogging()`; the only `AddLogging()` calls are in the
  test project and in ScraperTool's `App.xaml.cs` (`AddSerilog`). `GetRequiredService` throws if
  the logger service is unregistered.
- **Explanation**: A consumer that registers providers but never calls `services.AddLogging()` will
  now get an `InvalidOperationException` at resolve time where it previously succeeded — a
  behavioral breaking change not acknowledged by the plan's Compatibility section.
- **Required correction**: Use `sp.GetService<ILogger<TOptions>>()` (allowing the base to fall back
  to `NullLogger`), or explicitly document `AddLogging()` as a new prerequisite in the plan's
  Compatibility notes and the getting-started docs.

### W-5 — Stale references to the deleted `HybridGatewayProvider` class remain in documentation

- **Severity**: Warning
- **Plan x1 requirement**: Mandatory implementation rules — *"Update documentation files to
  remove references to deleted types."*
- **Affected file / location**:
  - `README.md:18` — `| Hybrid gateway | `HybridGatewayProvider` | opencode-go, opencode-zen |`
  - `docs/concepts/wire-protocols.md:114` — "All five implementations: Extend `AIProviderBase`"
  - `features/10/custom-provider-registration.md:114` — still describes
    `.Configure(o => o.ProviderId = providerId)`
- **Evidence**: Grep of the documentation tree confirms these three references. The class and the
  `AIProviderOptions.ProviderId` property they name no longer exist.
- **Explanation**: The documentation now points at a deleted provider type and a removed options
  property, and the "five implementations" count is stale after `HybridGatewayProvider` was folded
  into `OpenAICompatibleProvider` (four concrete providers remain). These are the specific files the
  plan's doc-cleanup rule targets.
- **Required correction**: Change `README.md` to reference `OpenAICompatibleProvider`; correct the
  implementation count in `wire-protocols.md`; update the `features/10` snippet to the
  constructor-parameter approach.

### W-6 — `implementation-checklist.md` left entirely unchecked

- **Severity**: Warning
- **Plan x1 requirement**: The checklist (`refactors/refactor25/implementation-checklist.md`) is
  the plan's completion record (Phases 1–15).
- **Affected file / location**: `refactors/refactor25/implementation-checklist.md` — every box is
  `- [ ]`, including the phases that are verifiably implemented (Phases 1–14).
- **Evidence**: All build/test/grep evidence above confirms Phases 1–14 complete; the plan (per the
  project's planning/checklist convention) treats this file as the implementation status record.
- **Explanation**: The completion record does not reflect reality; a reader cannot tell what has
  landed. (The only genuinely incomplete Phase 15 items are the missing tests — see E-1.)
- **Required correction**: Mark the completed checklist boxes, leaving unchecked only the missing
  test items tracked by E-1.

---

## Deviations checked and accepted (not findings)

- Conditional pipeline build (`ResiliencePipeline.Empty` when `MaxRetryCount <= 0`) — functionally
  equivalent to an unconditionally built zero-retry pipeline.
- `"json_schema"` anonymous-member name in `OpenAICompatibleWireProtocol` cannot be a constant —
  anonymous object member names must be identifiers; the property *value* does use the constant.
- Retained `"text"` / `"image_url"` / MessagesApi `"assistant"` / `"user"` / `"model"` literals —
  these are output wire values or a different protocol's semantics, not the input discriminators
  D4 targets.
- `OpenAICompatibleWireProtocol.Description` / `Modality` intentionally NOT routed through
  `SafeGetString` — that would collapse nullability the current code preserves.
- Four doc files the plan's D3 list names (tool-calling, model-discovery, api-reference/models,
  concepts/provider-catalog) never contained the deleted class name (verified with
  `git grep -c` at `d0cf981^`), so their non-edit is correct.
- No logging wired inside the protocol classes — the plan places logging in the transport
  template, not the pure parsers.
- `ThrowIfErrorAsync` left byte-for-byte unmodified (error-mapping parity).
- Two pre-existing `CS8604` build warnings in `AiUrlFixService.cs` — untouched by r25.
- `Polly.Core` uses a floating `Version="8.*"` while sibling packages are pinned `10.0.12` — not a
  functional/architectural defect within the plan's scope, so not filed.

---

## Final result

**FAIL** — the implementation is functionally sound and, per the build plus the 256 + 639 passing
tests, does not regress existing behavior. It is blocked by **E-1**: the plan's mandatory rule to
add dedicated tests for `ProtocolParsingHelpers`, retry behavior, and logging output is entirely
unimplemented. Six Warnings cover one behavioral parity deviation (W-1) and requirement/quality
gaps in the retry/logging wiring (W-2–W-4), documentation (W-5), and the stale completion record
(W-6).

## Implementation coverage summary

| Metric | Count |
|---|---|
| Plan requirements | 28 |
| Implemented correctly | 23 |
| Missing | 1 (E-1 required tests) |
| Incorrect / partial | 4 (W-1 parity, W-2 log template, W-3 429 Warning coverage, W-5 doc references) |
| Unknown | 0 |
| Errors | 1 (E-1) |
| Warnings | 6 (W-1 – W-6) |
