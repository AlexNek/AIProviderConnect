# Refactor 23 — Implementation Verification Review (second pass)

Review of commit `3422bff "implementation r23"` against `refactors/refactor23/scraper-tool-catalog-and-service-coupling.md` (Phases 1–35).

All findings below were verified directly against the source tree and against actual build/test runs at HEAD — including re-verification of the claims in `01-implementation-review.md`, two of which are corrected here.

## Verification runs (evidence)

| Command | Result |
|---|---|
| `dotnet test AIProviderConnectLib.Tests` | **BUILD FAILED** — `HybridGatewayProviderTests.cs(68,13): error CS0117: 'HybridGatewayProviderOptions' does not contain a definition for 'AuthType'` |
| `dotnet test ScraperTool.Tests` | Build succeeded, **606/606 tests pass** |
| `AIProviderConnectLib` library build | Succeeded (0 errors) |
| Protocol audit of all 76 `ai-providers/*.json` manifests | 38 files omit `"protocol"`, none empty/invalid → Phase 29 tightening cannot break catalog loading (converter is not invoked for absent properties) |

## Summary verdict

**The implementation is NOT complete and does NOT pass the plan's own Verification section.** 30 of 35 phases are correctly implemented (verified line by line). Five phases (7, 8, 13, 20, 24) were not implemented, one shipped phase (34) broke the library test build, and two phase implementations (10, 19) deviate from the plan's mandatory "no behavior change" rules.

Corrections to `01-implementation-review.md`:
1. 01 claimed "32 of 35 phases completed, no critical defects". **The library test project does not compile at HEAD** — this is a blocking defect for a pre-publish remediation whose own Verification step 2 requires all library tests to pass.
2. 01 claimed Phase 20 was "deferred by plan". **False** — the plan defers exactly one item (the `AiUrlFixService` god object, "Deferred (not in scope)" section). Phase 20 was in scope ("Evaluate … Extract … Refactor"); no implementation and no deferral record exists.
3. 01's completion table implicitly counted Phase 24 as done ("Completed: … 21–35"). **Phase 24 was not implemented** (see Finding 5).

---

## Findings

### Finding 1 — CRITICAL: Phase 34 broke the library test build; the tests that covered the removed behavior were not updated

**Location**: `AIProviderConnectLib.Tests/Providers/HybridGatewayProviderTests.cs` L15–37 (`Operations_RejectUnsupportedAuthBeforeSending`), L39–59 (`Operations_PreserveCaseInsensitiveBearerAuth`), L68 (`AuthType = authType,` in `CreateProvider`)

**What happened**: Phase 34 removed `HybridGatewayProviderOptions.AuthType` and the `ConfigureHeaders` override, but the commit contains **zero changes to any test file** (`git show --stat HEAD` confirms). Both theories still set the removed property and assert the removed `"Only 'Bearer' is supported."` error. `CS0117` fails the whole `AIProviderConnectLib.Tests` compilation, so no library test can run at all.

**Plan requirement**: Verification step 2 — `dotnet test AIProviderConnectLib.Tests` — all tests pass. Not met.

**Fix**: Delete `Operations_RejectUnsupportedAuthBeforeSending` (behavior intentionally removed), rework `Operations_PreserveCaseInsensitiveBearerAuth` to not pass `authType`, drop the parameter from `CreateProvider`.

### Finding 2 — MEDIUM: Phase 10 over-narrowed catches leak `InvalidOperationException`, violating the plan's own "do not change the fallback behavior" rule

**Locations**:
- `ScraperTool/Services/UrlResearch/DecisionTree/Adapters/DecisionTreeProgressAdapter.cs` L149 + L154 and L180 + L183
- `ScraperTool/Services/UrlResearch/DecisionTree/Actions/LlmExtractModelCountAction.cs` L235 + L256

**Issue**: The plan's mandatory rule states: *"Phase 10 (bare catches): do not change the fallback behavior at each site — only narrow the exception type."* At these three sites the `try` block contains `JsonElement.GetString()` calls on properties whose presence was checked but whose **type was not**:
- `DecisionTreeProgressAdapter.IsFailureOutcome`: `{"status": 200}` → `statusProp.GetString()` (L149) throws `InvalidOperationException`. Previously the bare catch ran the trimmed-string fallback (L156–158); now the exception escapes a progress-reporting adapter.
- `DecisionTreeProgressAdapter.FormatOutcome`: same pattern at L180; the plain-string fallback at L189 is skipped.
- `LlmExtractModelCountAction`: `{"count": 10, "quote": 123}` → `quoteProp.GetString()` (L235) throws; previously the bare catch routed to the deliberate "free-text reply is not a count source" path; now it crashes the decision-tree action.

These paths exist precisely to absorb malformed LLM output, so non-string property values are a realistic input, not a theoretical one. The other Phase 10 sites (`DataValidationAgent` L131, `PricingAgent` L145) are correct — their `try` blocks contain only `JsonSerializer.Deserialize`, which throws only `JsonException`.

**Fix**: at the three sites either check `ValueKind == JsonValueKind.String` before `GetString()` (preferred — keeps the narrow catch) or use `catch (Exception)` as before.

### Finding 3 — MEDIUM: Phase 34 left stale documentation of the removed `AuthType` API in two published docs

**Locations**:
- `docs/api-reference/di-extensions.md` L77: `| AuthType | string | "Bearer" |` still listed under *HybridGatewayProviderOptions* — documents a property that no longer exists.
- `docs/concepts/error-handling.md` L32: `| Unsupported AuthType with a nonempty API key (HybridGateway) | ai/configuration-error |` — documents an error path that was deleted.

**Plan requirement**: Phase 34 step 8 updated only `docs/concepts/wire-protocols.md`. The other two pages ship to the MkDocs site and now misdescribe the public API. (`AiErrorCodes.ConfigurationError` itself remains legitimately used by `ModelCatalogOverrideDecorator.cs` L83 — only the HybridGateway row is dead.)

### Finding 4 — MEDIUM: `ProviderBatchAnalysisService` DI registration is now unresolvable — the dual-instance problem Phase 2 removed has been re-created as a latent third construction path

**Locations**: `ScraperTool/App.xaml.cs` L305 (`services.AddSingleton<ProviderBatchAnalysisService>();`) vs `ScraperTool/Views/MainWindow.xaml.cs` L62 (`new ProviderBatchAnalysisService(analyzer, catalog, priceResolver, settings.PrimaryModel)`)

**Issue**: Phase 17 added a `string modelName` constructor parameter. The DI registration was untouched (the plan froze `App.xaml.cs`), so `GetRequiredService<ProviderBatchAnalysisService>()` would now fail at resolve time with *"Unable to resolve service for type 'System.String'"*. Today nothing resolves it — MainWindow hand-builds the only instance and passes it to `WorkPanelFactory` — so this is latent, not live. But it is exactly the "two suppliers, one service" trap the refactor set out to eliminate: the next developer who takes the DI registration at face value gets a runtime explosion, and the compiler cannot help because `AddSingleton<T>()` uses reflection.

**Fix**: remove the stale `AddSingleton<ProviderBatchAnalysisService>()` registration (the manual MainWindow composition is the single supplier), or register it with a factory delegate that supplies `settings.PrimaryModel`. The plan's "do not change App.xaml.cs" rule was exceeded by Phase 17's constructor change; the follow-up cleanup is required either way.

### Finding 5 — MEDIUM: Five planned phases not implemented

| Phase | Defect | Planned remedy | Actual state at HEAD (verified) |
|---|---|---|---|
| 7 | #7 — 4 methods repeat ~25-line try/catch/finally | `RunOperationAsync(label, operation, work)` helper in `CheckDataPanelViewModel` | **Missing.** No `RunOperationAsync` anywhere (grep zero matches); `_cts = new CancellationTokenSource()` still appears at L288, L379, L465, L755 of `CheckDataPanelViewModel.cs` |
| 8 | #8 — patch→reload→log duplicated in 3 ViewModels | shared `ApplyPatchAndReload…` helper on `SuggestionManagementViewModelBase` | **Missing.** `_catalog.ReloadFromDisk` still inline at `AiAnalysisPanelViewModel.cs` L119, `CheckDataPanelViewModel.cs` L939, `ProviderManualEditorViewModel.cs` L394; base class is 43 lines with no helper |
| 13 | #13 — `MainViewModel` depends on concrete `ProviderCatalog` | field/ctor → `IProviderCatalog` | **Missing.** `MainViewModel.cs` L18 `private readonly ProviderCatalog _catalog;`, L61 ctor param unchanged. `AiSetupViewModel` already takes `IProviderCatalog` (L64) so it is not an obstacle; the real blocker is `CreateCheckDataPanel`/`CreateManualEditor` (Phase 14 deliberately kept them concrete). The plan under-specified this; resolving it needs a decision (e.g. factory resolves the panels' catalog from DI itself), not a silent skip |
| 20 | #20 — duplicated transport in `KeyQueryProvider`/`MessagesApiProvider` | evaluate + extract shared template | **Missing, and falsely reported as deferred by review 01.** The plan's "Deferred (not in scope)" section lists only the `AiUrlFixService` god object. Either implement or record an explicit deferral decision — 01's acceptance is not a plan artifact |
| 24 | #24 — `MessagesApiOptions.DefaultMaxTokens`/`ApiVersion` never seeded | extend `ProviderDefinition` **or** add doc comment | **Missing.** `SeedFromDefinition` still seeds only `MessagesEndpoint`/`ModelsEndpoint`; `MessagesApiOptions.cs` L16 comment still just says "Default is 1024" with no mention of the seeding gap |

Phases 7 and 8 were the plan's two largest DRY items ("test each ViewModel independently after refactoring") — skipping them means defect #7 and #8 stand verbatim.

### Finding 6 — MEDIUM: Published-library behavior fixes shipped without regression tests

The commit contains no test changes at all, yet several phases fix or change public NuGet behavior:
- Phase 4 (`WithDynamicCatalog` no longer throws on missing metadata) — `ProviderCatalogTests.cs` L115 only covers the happy path.
- Phase 26 (`KeyQueryWireProtocol` malformed `candidates[0]` no longer throws) — no test drives a candidate without `content`/`parts`.
- Phase 29 (null protocol now throws `JsonException`) — no test; and the *old* lenient behavior was implicitly relied upon by nothing, but the new throw deserves a guard test.
- Phase 33 (`ResolveUrl` octet-stream fallback) — `ImageContentTests.cs` L113 covers `MediaType` set only.
- Phases 19/22/23/31 (new `UsageInfoParser`, `HttpErrorMessages`, `ResolveSystemInstruction`, `ProviderDefinitionListUpsert`) — zero direct tests for brand-new helpers.

Bug fixes in a published library without a regression test will regress.

### Finding 7 — LOW: Phase 19 silently changed `total` semantics while the plan promised identical output

**Location**: `AIProviderConnectLib/Protocols/UsageInfoParser.cs` L40–42

Old code: missing `usage.total_tokens` (OpenAI) / `totalTokenCount` (Gemini) → `TotalTokens = 0`. New helper: falls back to `prompt + completion`. The Compatibility section promised *"All three protocols produce identical `UsageInfo` output"*. The new behavior is defensible (and matches what Messages API always did), but it is a user-visible change to a published library that was neither declared nor tested. Either accept and document it, or reproduce the old zero-default for the two protocols that had it.

### Finding 8 — LOW: Same-class violation fixed in one service but left in another (`Dispatcher.Yield` in `ScraperTool/Services`)

**Location**: `ScraperTool/Services/ProviderDefinitionValidator.cs` L92 — `await Dispatcher.Yield(DispatcherPriority.Background);`

Phase 3's rationale ("a business-logic service becomes untestable without a live WPF dispatcher loop and impossible to reuse outside a WPF host") applies verbatim here. Pre-existing (not introduced by this commit) and outside the plan's listed sites, but it is the identical defect the refactor just condemned — worth folding into the follow-up. (The remaining yields in `ScrapePanelViewModel` are in a ViewModel and are fine.)

### Finding 9 — INFORMATIONAL: Phase 3 took the "remove yields entirely" branch whose acceptance condition is an unrecorded live run

Plan Phase 3 step 6 allows removing the yields without moving them to callers, conditional on *"verify the progress bar updates during a live run. If it does not, reintroduce the yield via the callback."* The implementation removed the yields (`ProviderBatchAnalysisService.cs` has zero `Dispatcher` references — verified); the conditional live verification is a manual check. Suggestion: run ScraperTool once against a small catalog (Check Data → Deep Analysis) before publishing, and watch the progress/operation log during the LLM round-trips.

---

## Correctly implemented (verified against source, no issues found)

Phases **1, 2, 3 (code), 4, 5, 6, 9, 11, 12, 14, 15, 16, 17, 18, 19*, 21, 22, 23, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34 (code), 35** — with * subject to Findings 6/7.

Highlights worth noting positively:
- The `UsageInfoParser` design (`totalKey: null` = compute-prompt+completion) cleanly absorbs all three protocols' property variations and correctly fixes defect 22 via Phase 19, as the plan intended.
- Phase 21 was done properly: non-keyed `AddSingleton` removed, and a repo-wide grep confirms no non-keyed `IAIProvider` consumer exists (only keyed two-arg usage in docs).
- Phase 16's `ExchangeAndDisposeAll<T1,T2,T3>` with `class, IAsyncDisposable` constraints is a faithful, behavior-preserving consolidation.
- Phase 2 (DI singleton catalog) + Phase 17 (`MainWindow` passes `settings.PrimaryModel`) compose correctly; `App.xaml.cs` L308 still maps `IProviderCatalog` to the same singleton.
- `AiSetupViewModel` already accepted `IProviderCatalog`, so the Phase 13 plan's widening concern is moot there — the only blockers are the two factory signatures (Finding 5, Phase 13).
- One-type-per-file respected for all new files (`CatalogPropertyNames.cs`, `UsageInfoParser.cs`, `HttpErrorMessages.cs`).

## Required actions before r23 can be closed

1. Fix the `HybridGatewayProviderTests` compile break (Finding 1) and get `dotnet test AIProviderConnectLib.Tests` green.
2. Repair the three catch-narrowing leaks (Finding 2).
3. Remove the two stale `AuthType` doc rows (Finding 3).
4. Delete or fix the unresolvable `ProviderBatchAnalysisService` DI registration (Finding 4).
5. Implement or explicitly record a decision for Phases 7, 8, 13, 20, 24 (Finding 5).
6. Add regression tests for the changed/published library behaviors and new helpers (Finding 6); declare or revert the `TotalTokens` semantics change (Finding 7).
7. Manual live smoke of Deep Analysis progress rendering (Finding 9) — suggestion, not a checkable step.
