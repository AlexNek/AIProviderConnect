# Refactor 23 — Independent Third-Pass Implementation Review

Review of commit `3422bff "implementation r23"` (working tree clean at HEAD) against `refactors/refactor23/scraper-tool-catalog-and-service-coupling.md` (defects 1–35, Phases 1–35).

This pass was produced independently of `01-implementation-review.md` and `02-implementation-verification-review.md`; every finding below was re-verified first-hand against the source tree and against a fresh build run. Where it agrees or disagrees with the two prior reviews, that is stated explicitly.

## Verification runs (first-hand evidence)

| Check | Result |
|---|---|
| `dotnet build AIProviderConnectLib.Tests` | **BUILD FAILED** — `HybridGatewayProviderTests.cs(68,13): error CS0117: 'HybridGatewayProviderOptions' does not contain a definition for 'AuthType'` (re-run for this review) |
| `git show --stat HEAD` | 40 files changed — **zero test files** touched by the implementation commit |
| `grep RunOperationAsync\|ApplyPatchAndReload ScraperTool/` | zero matches (Phases 7, 8 not implemented) |
| `grep "private readonly ProviderCatalog _catalog" MainViewModel.cs` | still present at L18 (Phase 13 not implemented) |
| `grep "AddSingleton<ProviderBatchAnalysisService>" App.xaml.cs` | still present at L305 |
| `grep AuthType docs/` | still present in `di-extensions.md` L77 and `error-handling.md` L32 |
| `git show HEAD~1:...OpenAICompatibleWireProtocol.cs` | old code defaulted `TotalTokens = 0` when `total_tokens` absent |

## Summary verdict

**30 of 35 phases implemented; 5 phases missing (7, 8, 13, 20, 24). The implementation does not pass the plan's own Verification section: the library test project does not compile at HEAD.** Beyond the build break, one shipped phase (10) violates the plan's mandatory "do not change the fallback behavior" rule at three sites, and one shipped phase (19) silently changes published-library behavior that the Compatibility section promised would be identical.

This verdict **confirms review 02 in full** and **corrects review 01** on two points:
1. 01's "32 of 35 completed, no critical defects" is wrong — the `AIProviderConnectLib.Tests` compilation is broken by Phase 34, so no library test can run at all.
2. 01's "Phase 20 deferred by plan" is false — the plan's "Deferred (not in scope)" section lists only the `AiUrlFixService` god object. Phase 20 was in scope and was not implemented; no deferral decision was recorded.

---

## Findings

### Finding 1 — CRITICAL: Phase 34 shipped without updating the tests that covered the removed behavior

**Location**: `AIProviderConnectLib.Tests/Providers/HybridGatewayProviderTests.cs` L68 (`AuthType = authType,` in `CreateProvider`), plus the two theories at L15–59 that assert the removed `"Only 'Bearer' is supported."` error.

**Problem**: Phase 34 removed `HybridGatewayProviderOptions.AuthType` and the `ConfigureHeaders` override, but the commit touched zero test files. `CS0117` fails the entire `AIProviderConnectLib.Tests` compilation, so Verification step 2 of the plan (`dotnet test AIProviderConnectLib.Tests` — all tests pass) cannot be met at all — not just for the affected tests, but for every library test.

**Fix**: delete the `Operations_RejectUnsupportedAuthBeforeSending` theory (behavior intentionally removed), rework `Operations_PreserveCaseInsensitiveBearerAuth` to not pass an `authType` argument, and drop the parameter from `CreateProvider`. Then get the test project green.

### Finding 2 — MEDIUM: Phase 10 catch narrowing breaks the fallback paths at 3 decision-tree sites (violates the plan's mandatory rule)

**Locations** (all verified at HEAD):
- `ScraperTool/Services/UrlResearch/DecisionTree/Adapters/DecisionTreeProgressAdapter.cs` — `IsFailureOutcome` L146–159, `FormatOutcome` L174–189
- `ScraperTool/Services/UrlResearch/DecisionTree/Actions/LlmExtractModelCountAction.cs` — quote extraction L234–236 inside the `catch (JsonException)` at L256

**Problem**: Mandatory rule: *"Phase 10: do not change the fallback behavior at each site — only narrow the exception type."* In each of the three sites the `try` block calls `JsonElement.GetString()` on a property whose presence was checked (`TryGetProperty`) but whose **JSON type was not**. `GetString()` throws `InvalidOperationException` — not `JsonException` — on a non-string value (e.g. `{"status": 200}`, `{"quote": 123}`). Where a bare `catch` previously routed such malformed input to the deliberate fallback (trimmed-string comparison / plain-string handling / "not a count source" path), the exception now escapes. These code paths exist precisely to absorb arbitrary LLM output, so non-string values are realistic input, not theoretical.

**Fix**: at each site guard with `x.ValueKind == JsonValueKind.String` before calling `GetString()` (preferred — keeps the narrow catch), or widen that one site's catch back to `catch (Exception)` as the plan's fallback-preserving alternative.

### Finding 3 — MEDIUM: Phase 17 left an unresolvable DI registration — a latent re-creation of the dual-supplier problem Phase 2 fixed

**Locations**: `ScraperTool/App.xaml.cs` L305 (`services.AddSingleton<ProviderBatchAnalysisService>();`) vs `ScraperTool/Services/ProviderBatchAnalysisService.cs` constructor, which now takes an unresolvable `string modelName`; the only live instance is hand-built in `ScraperTool/Views/MainWindow.xaml.cs` L62.

**Problem**: `GetRequiredService<ProviderBatchAnalysisService>()` would throw *"Unable to resolve service for type 'System.String'"*. Nothing resolves it today, so this is latent — but it is exactly the "two suppliers, one service" trap the refactor set out to eliminate, and reflection-based registration means the compiler cannot catch it. The plan's "do not change App.xaml.cs" rule was already forced open by Phase 17's constructor change; the follow-up cleanup is mandatory either way.

**Fix**: remove the stale `AddSingleton<ProviderBatchAnalysisService>()` (MainWindow composition is the single supplier), or register via a factory delegate that supplies `settings.PrimaryModel`.

### Finding 4 — MEDIUM: Phase 34 shipped stale published documentation of the removed `AuthType` API

**Locations** (verified at HEAD):
- `docs/api-reference/di-extensions.md` L77 — `| AuthType | string | "Bearer" |` still listed under *HybridGatewayProviderOptions*
- `docs/concepts/error-handling.md` L32 — `| Unsupported AuthType with a nonempty API key (HybridGateway) | ai/configuration-error |` — error path deleted from the code

**Problem**: Phase 34 step 8 updated only `docs/concepts/wire-protocols.md`. The two pages above ship to the MkDocs site and now document a property and an error path that no longer exist.

**Fix**: remove the `AuthType` row from the options table and the HybridGateway row from the error-handling table.

### Finding 5 — MEDIUM: Five planned phases not implemented

| Phase | Defect | Required remedy | State at HEAD (verified) |
|---|---|---|---|
| 7 | #7 — 4 methods repeat ~25-line try/catch/finally | `RunOperationAsync(label, operation, work)` helper in `CheckDataPanelViewModel` | **Missing** — no `RunOperationAsync` anywhere; `_cts = new CancellationTokenSource()` still appears at four separate methods of `CheckDataPanelViewModel.cs` |
| 8 | #8 — patch→reload→log duplicated in 3 ViewModels | shared `ApplyPatchAndReload…` helper on `SuggestionManagementViewModelBase` | **Missing** — `_catalog.ReloadFromDisk` still inline in `AiAnalysisPanelViewModel`, `CheckDataPanelViewModel`, `ProviderManualEditorViewModel` |
| 13 | #13 — `MainViewModel` depends on concrete `ProviderCatalog` | field/ctor → `IProviderCatalog` | **Missing** — L18/L61 unchanged. Real blocker: `CreateCheckDataPanel`/`CreateManualEditor` factory signatures (Phase 14 intentionally kept them concrete). Needs an explicit decision (e.g. let `WorkPanelFactory` resolve the concrete catalog from DI itself), not a silent skip |
| 20 | #20 — duplicated transport in `KeyQueryProvider`/`MessagesApiProvider` | evaluate + extract shared template | **Missing, and NOT deferred** — the plan's only deferred item is the `AiUrlFixService` god object. Either implement or record an explicit deferral decision |
| 24 | #24 — `MessagesApiOptions.DefaultMaxTokens`/`ApiVersion` never seeded | extend `ProviderDefinition` **or** document the gap | **Missing** — `SeedFromDefinition` (extensions L134–137) still seeds only `MessagesEndpoint`/`ModelsEndpoint`; no doc comment on the seeding gap in `MessagesApiOptions.cs` |

Phases 7 and 8 were the plan's two largest DRY items; skipping them leaves defects #7 and #8 standing verbatim — which is squarely in scope of this review's "what is missing / DRY violation" question.

### Finding 6 — MEDIUM: Behavior changes in the published NuGet library shipped with zero regression tests

**Evidence**: `git show --stat HEAD` — no test file appears in the commit, yet the commit changes published library behavior:
- Phase 4 (`WithDynamicCatalog` no longer throws on missing metadata)
- Phase 26 (`KeyQueryWireProtocol` guarded `content`/`parts` parsing)
- Phase 29 (null/empty `"protocol"` now handled strictly)
- Phase 33 (`ImageContent.ResolveUrl` octet-stream fallback)
- New helpers `UsageInfoParser`, `HttpErrorMessages`, `MessageTextResolver.ResolveSystemInstruction`, `ProviderDefinitionListUpsert` — no direct tests

Bug fixes in a published library without a regression test will silently regress. Each of the above deserves at least one test at the fixed behavior boundary.

### Finding 7 — LOW: Phase 19 silently changed `TotalTokens` semantics, contradicting the Compatibility promise

**Locations**: `AIProviderConnectLib/Protocols/UsageInfoParser.cs` L40–42 vs `HEAD~1` protocol code (verified via `git show`).

**Problem**: Old behavior: absent `usage.total_tokens` / `usageMetadata.totalTokenCount` → `TotalTokens = 0`. New helper: falls back to `prompt + completion`. The plan's Compatibility section for Phase 19 promised *"All three protocols produce identical `UsageInfo` output"*. The new semantics are defensible (they match what the Messages API always did) but are a user-visible change to a published library that was neither declared nor tested.

**Fix**: either accept and document it (plan Compatibility note + CHANGELOG-relevant awareness at release time), or add an explicit `fallbackTotalToSum`-style parameter so OpenAI/KeyQuery keep the old zero default.

### Finding 8 — INFORMATIONAL: One defect of Phase 3's class survives in a service the commit touched

**Location**: `ScraperTool/Services/ProviderDefinitionValidator.cs` L92 — `await Dispatcher.Yield(DispatcherPriority.Background);` in a business-logic service.

**Problem**: Pre-existing and outside the plan's listed sites, but it is the identical WPF-dispatcher coupling that Phase 3 condemned for `ProviderBatchAnalysisService` (the same file was touched by Phase 15). Worth folding into the follow-up pass for consistency. (Yields remaining in `ScrapePanelViewModel` are correctly placed in a ViewModel.)

### Finding 9 — INFORMATIONAL: Phase 3's acceptance is conditional on an unrecorded live run

The implementation took the plan's "remove the yields entirely" branch, whose stated acceptance condition is *"verify the progress bar updates during a live run"*. Code state is correct (zero `Dispatcher` references in `ProviderBatchAnalysisService.cs` — verified); the live check is a manual suggestion before publishing: run Check Data → Deep Analysis on a small catalog and watch the progress rendering.

---

## Phases verified correctly implemented (28 clean + 2 with caveats)

1, 2, 3*, 4, 5, 6, 9, 11, 12, 14, 15, 16, 17*, 18, 19*†, 21, 22, 23, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34 (code only†), 35
— * Phase 3: code correct, live-run acceptance open (Finding 9). \* Phase 17: code correct, DI follow-up missed (Finding 3). † Phase 19/22: logic sound but semantics changed (Finding 7). † Phase 34: library code correct; tests/docs not updated (Findings 1, 4).

Highlights: Phase 2 singleton resolve, Phase 4/26/29/33 published-library bug fixes, Phase 16 `ExchangeAndDisposeAll<T1,T2,T3>` consolidation, Phase 21 non-keyed registration removal, and the one-type-per-file discipline for all new files (`CatalogPropertyNames.cs`, `UsageInfoParser.cs`, `HttpErrorMessages.cs`) are all faithful to the plan.

## Required actions before r23 can be closed

1. Fix `HybridGatewayProviderTests` so `dotnet build AIProviderConnectLib.Tests` and `dotnet test AIProviderConnectLib.Tests` go green (Finding 1).
2. Repair the three catch-narrowing fallback leaks (Finding 2).
3. Remove or fix the unresolvable `ProviderBatchAnalysisService` DI registration (Finding 3).
4. Delete the two stale `AuthType` doc rows (Finding 4).
5. Implement Phases 7, 8, 13, 20, 24 — or record an explicit, reasoned deferral decision for each, Phase 13's factory-signature constraint included (Finding 5).
6. Add regression tests for the changed library behaviors and the new helpers (Finding 6); declare or revert the `TotalTokens` semantics change (Finding 7).
7. Manual live smoke of Deep Analysis progress rendering before publishing (Finding 9) — suggestion, not a checkable step.
