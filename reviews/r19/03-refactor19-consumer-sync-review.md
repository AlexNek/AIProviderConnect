# Refactor 19 Implementation Review (v3)

**Review Date:** 2026-09-18  
**Scope:** Every ScraperTool and ScraperTool.Tests file changed by commits `6066de0..0d44e9f` (r18 + r19), reviewed against `refactors/refactor19/consume-library-remediation.md` (design) and `refactors/refactor19/implementation-checklist.md` (checklist).  
**Method:** every finding verified against source at HEAD (`0d44e9f`); build is clean (0 warnings, 0 errors), 529 tests pass.

## Verdict

The consumer sync is **functionally complete and correct**. Every binding decision in the design document is honored: `IProviderCatalog` adoption is split correctly between pure readers and `ReloadFromDisk` callers, `EChatRole` rename is total, `ProviderJsonFields` consolidates all manifest-name constants, the `ProviderResearchMetadata` split is applied at every site, and the factory reshape preserves the unsaved-key connection test. No mandated edit is missing.

The remaining issues are one pre-existing functional defect the refactor preserved, one DRY violation that the refactor demonstrably doubled, and three code-quality problems in code the refactor touched.

## Findings

### F1 (Major) — `DefaultHeaders` silently dropped for four of five protocol families

**File:** `ScraperTool/Services/AIProviderFactory.cs` lines 44–117

`BuildOptions` (line 54) sets `DefaultHeaders` with `HTTP-Referer` and `X-Title` on the `OpenAICompatibleProviderOptions` object. `BuildProvider` then passes that object directly to `OpenAICompatibleProvider` (line 72–75), so OpenAI-compatible providers send the headers correctly.

For every other protocol, `BuildProvider` constructs a **new** options object (e.g. `MessagesApiOptions` at line 78–84) and copies only `ApiKey`, `BaseUrl`, `Enabled`, and `ProviderId`. `DefaultHeaders` is never copied. Since `AIProviderBase.SendAsync` (library line 113) iterates `options.DefaultHeaders` to attach request headers, MessagesApi, HybridGateway, KeyQuery, and Catalog providers all send requests **without** the identification headers.

**Root cause:** Pre-existing — the pre-refactor file at commit `6066de0` has the identical shape. The refactor preserved the behavior faithfully. Not a regression, but the refactor touched this file and the plan's "surgical edits only" rule kept the defect in place.

**Impact:** Providers on non-OpenAI protocols (Anthropic via MessagesApi, any HybridGateway provider, KeyQuery providers like Gemini, Catalog-only providers) do not identify the requesting application. This may affect rate-limit treatment, logging, or abuse detection on the provider side.

**Fix:** Extract the headers dictionary to a local variable and assign it on every options branch, or set it on the base-class property after the switch:

```csharp
var commonHeaders = new Dictionary<string, string>
{
    ["HTTP-Referer"] = "https://github.com/opencode-ai",
    ["X-Title"] = "AI Provider Catalog Researcher"
};
```

Then pass `DefaultHeaders = commonHeaders` in every options initializer.

---

### F2 (Major) — DRY: deep-analysis workflow duplicated across two ViewModels, refactor had to edit both

**Files:**
- `ScraperTool/ViewModels/AiAnalysisPanelViewModel.cs` lines 200–293 (`StartWorkAsync`)
- `ScraperTool/ViewModels/CheckDataPanelViewModel.cs` lines 854–948 (`RunDeepAnalysisAsync`)

Both methods contain the same ~100-line workflow:
1. Filter providers by `GetResearchMetadata(p.Id)?.ApiPricingUrl` being set and not the `"-"` sentinel
2. Iterate with progress logging, `Dispatcher.Yield`, and cancellation
3. Call `_analyzer.AnalyzeProviderAsync` per provider
4. Accumulate prompt/completion tokens
5. Resolve pricing via `_priceResolver.ResolveAsync(null)` and compute cost
6. Format summary text

**Evidence the duplication has real cost:** Refactor 19 had to apply the identical `GetResearchMetadata` rewrite to **both** copies (lines 224–229 and 875–880). This is exactly the double-edit cost the plan's "surgical edits only" rule multiplied. The divergence is already visible: the AI panel has pause/resume handling, the Check panel adds token/cost summary properties the other lacks.

**Fix:** Extract the core analysis loop into a shared service (e.g. `IProviderDeepAnalysisService`) that both ViewModels call. ViewModels keep only UI state (pause, progress binding, result collection). This predates refactor 19 and is not covered by refactor 21 — it should be tracked as a separate item.

---

### F3 (Minor) — Stale `using AIProviderConnect.Services` in `AiUrlFixService`

**File:** `ScraperTool/Services/AiUrlFixService.cs` line 9

The file depends on `IProviderCatalog` (from `AIProviderConnect.Abstractions`), not on `ProviderCatalog` (the concrete type from `AIProviderConnect.Services`). The using is a leftover from the Phase 3 migration. The only type in `AIProviderConnect.Services` is `ModelOverrideMerger`, which this file does not use.

**Fix:** Remove the using statement.

---

### F4 (Minor) — `AIProviderFactory` options build: unnecessary `IOptions` wrapping, post-construction mutation, double catalog lookup

**File:** `ScraperTool/Services/AIProviderFactory.cs` lines 44–127

Three intertwined problems:

1. `BuildOptions` wraps a fresh `OpenAICompatibleProviderOptions` in `Options.Create(...)`; `BuildProvider` immediately unwraps `.Value`. The `IOptions<T>` layer is consumed by nobody — it mimics DI options behavior in a purely local helper.
2. `opts.ProviderId = definition.Id` (line 68) mutates the options object after construction instead of setting it in the initializer.
3. `CreateProvider` resolves `_catalog.Get(providerId)` at line 123, then `BuildOptions` resolves the same id again at line 52 — two catalog lookups per provider creation.

**Fix:** One construction site: `BuildOptions(string apiKey, ProviderDefinition definition)` returning the plain options object with `ProviderId = definition.Id` and `BaseUrl = definition.BaseUrl` in the initializer; `BuildProvider` consumes it unwrapped. The `using Microsoft.Extensions.Options;` import drops out.

---

### F5 (Minor) — `DecisionTreeResearchService.ResolveFieldValue` uses magic strings instead of `ProviderJsonFields` constants

**File:** `ScraperTool/Services/UrlResearch/DecisionTree/DecisionTreeResearchService.cs` lines 486–498

The switch arms use `"website"`, `"loginUrl"`, `"apiPricingUrl"`, `"subscriptionPricingUrl"`, `"documentationUrl"`, `"baseUrl"` — literals that duplicate `ProviderJsonFields.Website`, `.LoginUrl`, `.ApiPricingUrl`, `.SubscriptionPricingUrl`, `.DocumentationUrl`, `.BaseUrl`. The plan's mandatory rules state: "Keep one ScraperTool-side constant class for manifest field names." These are the only manifest-name literals left in ScraperTool; everything else already goes through the constants.

**Fix:** Replace the string literals with the `ProviderJsonFields` constants. `switch` arms accept `const string`, so this is a drop-in substitution with no behavior change.

---

### F6 (Minor) — Bare `catch` swallows `OperationCanceledException` in `FetchPricingPageAsync`

**File:** `ScraperTool/Services/AiDefinitionAnalyzer.cs` lines 191–204

The method accepts a `CancellationToken` and forwards it to `GetStringAsync`, then catches everything including `OperationCanceledException`. After the user cancels, the in-flight fetch is disguised as "no pricing page content" and the analysis continues into a full provider round-trip instead of stopping promptly.

**Fix:**
```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    return null;
}
```

---

## Plan-Compliance Summary

| Binding decision | Status | Evidence |
| --- | --- | --- |
| Keep `AIProviderFactory`; do not adopt `DefaultAIProviderFactory` | ✅ | File present, consumer-specific |
| `GetProvider(string)` against saved `AppSettings.ApiKey` | ✅ | Lines 32–33 |
| Concrete-only `GetProvider(string, string)` for unsaved-key test | ✅ | Lines 39–40; used by `AiSetupViewModel` line 344 |
| `GetDefaultBaseUrl` switch removed | ✅ | grep clean; base URL via `_catalog.Get` |
| `App.xaml.cs` registers concrete singleton + maps interface | ✅ | Lines 264–265 |
| Pure readers on `IProviderCatalog` | ✅ | `AIProviderFactory`, `AiDefinitionAnalyzer`, `AiUrlFixService`, `ScrapePanelViewModel`, `AiSetupViewModel` |
| `ReloadFromDisk` callers keep concrete `ProviderCatalog` | ✅ | `MainViewModel`, `WorkPanelFactory`, `IssueSyncService`, `AiAnalysisPanelViewModel`, `CheckDataPanelViewModel`, `ProviderManualEditorViewModel` |
| `EChatRole` rename fully applied | ✅ | No `ChatRole` references remain |
| One `ProviderJsonFields` class for all manifest names + sentinel | ✅ | 24 constants, one type |
| No `ProviderDefinition.JsonXxx` / `.NotApplicable` references remain | ✅ | grep clean |
| Research properties via `GetResearchMetadata` at all sites | ✅ | All listed files verified |
| No post-construction mutation of record-converted types | ✅ | All sites use object initializers |
| `EPriceUnit` enum used, no `"Per1M"` logic comparisons | ✅ | Only LLM prompt text contains the string |
| No `ai-providers/*.json` edits; no CHANGELOG update; no agent commits | ✅ | Verified |

## Verified Clean

- DI wiring is correct: one shared `ProviderCatalog` instance behind both `ProviderCatalog` and `IProviderCatalog` registrations.
- All factory callers except `AiSetupViewModel` (which needs the two-argument overload) use the single-argument interface form.
- The `"-"` sentinel is used consistently via `ProviderJsonFields.NotApplicable`.
- Touched tests follow AAA + Moq + FluentAssertions, use fake keys only, one-type-per-file holds.

## Recommendations (priority order)

1. **F1** (DefaultHeaders) — pre-existing but the refactor touched this code; fix is small and affects four protocol families.
2. **F6** (cancellation swallow) — behavior-correct, small fix.
3. **F5** (magic strings) — drop-in substitution, aligns with the plan's own rule.
4. **F3** (stale using) — trivial cleanup.
5. **F4** (options build simplification) — removes the fake `IOptions` signal and double lookup.
6. **F2** (deep-analysis duplication) — track as a separate refactor item; not blocking.
