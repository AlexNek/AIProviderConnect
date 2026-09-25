# Refactor 20 — Consolidated Implementation Review (Phases 0–10)

Scope: the full plan (`scraper-tool-god-class-decomposition.md`). Phases 0–6 are implemented and committed; Phases 7–10 are not started. This review consolidates actionable findings from `r20/01` and `r20/02` (verified against current HEAD) and adds issues those reviews did not cover.

Evidence: working tree at HEAD. `dotnet build ScraperTool/ScraperTool.csproj` → 0 warnings, 0 errors. `ScraperTool/Services/Validation/Checks/` holds 20 files (10 interfaces + 9 implementations + 1 dead enum).

---

## Verdict

The validator decomposition (Phases 0–6) is **behaviorally correct** — the transcript freeze is real (exact string equality, 14 scenarios, per-field request counts), no assertion was weakened, and the host no longer holds a transport. But the implementation carries plan violations, test gaps, and clean-code debt that will compound when Phases 7–10 are attempted on the same patterns.

---

## 1. Major

### 1.1 `UrlFieldChecker.cs` exceeds 250-line budget (382 physical lines)

**Severity: Plan violation**

Mandatory rule: *"every collaborator under 250 lines"*.

| File | Physical lines | Budget | Status |
| --- | --- | --- | --- |
| `UrlFieldChecker.cs` | **382** | ≤ 250 | **✗** |
| `ProviderDefinitionValidator.cs` | **261** | ≤ 250 | **✗** |
| All other collaborators | 16–156 | ≤ 250 | ✓ |

`UrlFieldChecker` concentrates the entire field-loop branching into one class: 8 methods, of which `HandleReachableResponseAsync` (L165–268, 104 lines) alone dispatches redirect caps, content probes, website-root checks, login-equality checks, login-surface analysis, baseUrl API probing, and a generic pass — at least six distinct responsibilities in one method. `TryCheckWebsiteRootAsync` (L270–295) and `TryCheckLoginUrlEqualityAsync` (L297–330) are complete side-rules that belong behind `IWebsiteOwnershipJudge` / `IPageContentProbe` verdicts.

**Fix:** move the website-path pair (`TryCheckWebsiteRootAsync`, `TryCheckLoginUrlEqualityAsync`, and the login-surface judgment at L218–239) into `WebsiteOwnershipJudge` / `PageContentProbe` as verdict-returning calls. The checker keeps only the emission and the `continue`.

### 1.2 Zero direct tests for 6 of 9 new production types

**Severity: Test gap against the plan's Goal**

The Goal is *"each field check, probe, verification … unit-testable without constructing the 8-dependency host."* Testable was delivered; tested was not:

| New type | Has unit test? |
| --- | --- |
| `UrlFieldChecker` | **✗** |
| `PageContentProbe` | **✗** |
| `PricingPageVerifier` | **✗** |
| `WebsiteOwnershipJudge` | **✗** |
| `ApiEndpointProbe` | **✗** |
| `ServiceRetirementProbe` | **✗** |
| `ProviderManifestReader` | **✗** |
| `ValidationIssueSink` | **✗** |
| `SelfHostedApplicabilityEvaluator` | ✓ |
| `SubscriptionConfiguredChecker` | ✓ |
| `UrlDomainRules` | ✓ |

`Mock<IPageContentProbe>`, `Mock<IPricingPageVerifier>`, `Mock<IWebsiteOwnershipJudge>`, `Mock<IApiEndpointProbe>`, `Mock<IUrlFieldChecker>` → **0 occurrences** in `ScraperTool.Tests`. The characterization tests cover the full pipeline end-to-end but cannot isolate branch dispatch (pricing vs reachability), HTTP status classification (401/403/429/404), or the loginUrl single-fetch invariant (invariant 2).

**Fix:** add at minimum `UrlFieldCheckerTests` (5 mocks, branch dispatch + status classification + `TaskCanceledException` → `UrlTimeout` vs `OperationCanceledException` rethrow + redirect cap + loginUrl single-fetch).

### 1.3 Phases 7–10 not implemented

**Severity: Major (scope completeness)**

| Planned | Reality |
| --- | --- |
| `Services/UrlFix/`: 11 new files | Only pre-existing `UrlIntelligenceRuleKind.cs` / `UrlIntelligenceRules.cs` |
| `AiUrlFixService.cs` under 250 lines | 1189 lines; `ProcessBatchAsync` still L618–1072 (454 lines) |
| Seven duplicated outcome blocks collapsed into `BatchOutcomeCollector` | Still seven copies in place |
| `ScanProviderLinksAction.ExecuteAsync` split (Phase 10) | L37–L296, ≈260 lines — over the 150-line rule |
| `HttpClient` gone from `AiUrlFixService` | Still injected via `IHttpClientFactory.CreateClient(ScraperHttpClientName)` |

The checklist is honest (Phases 7–10 unchecked) and the Phase 0 characterization freeze for that side exists (`AiUrlFixServiceCharacterizationTests.cs`, 18 `[Fact]`s), so the remaining work is unblocked.

### 1.4 DRY: raw `JsonElement` threaded through 5 collaborators; `"SelfHosted"` category test duplicated

**Severity: DRY / leaky representation**

The `TryGetProperty(...) && ValueKind == JsonValueKind.String ? GetString() : null` pattern appears **12 times** across the new/moved files:

| File | Occurrences |
| --- | --- |
| `SelfHostedApplicabilityEvaluator` | 4 |
| `ServiceRetirementProbe` | 3 |
| `SubscriptionConfiguredChecker` | 2 |
| `UrlFieldChecker` (L302) | 1 |
| `WebsiteOwnershipJudge` (L95) | 1 |
| `ApiEndpointProbe` (L96) | 1 |

The `"SelfHosted"` category string comparison is independently implemented in two files:
- `SelfHostedApplicabilityEvaluator.IsSelfHosted` (L134–137)
- `SubscriptionConfiguredChecker.ValidateAsync` (L20–25)

Both do `TryGetProperty(Category) → ValueKind check → GetString() → OrdinalIgnoreCase compare`. A third copy exists in pre-existing `ProviderSchemaValidator.cs:187` and `IsSelfHostedProviderPredicate.cs:19`.

**Fix:** parse once into a small immutable read-model (`ProviderManifestView`: `IsSelfHosted`, `DisplayName`, `LoginUrl`, `ModelsEndpoint`, `TryGetString(field)`) built by the host next to `IProviderManifestReader`, and pass that instead of `JsonElement`. This removes 12 reads, both category literals, and the `Root` parameter from `IWebsiteOwnershipJudge.CheckWebsiteRootOwnershipAsync` and `IApiEndpointProbe.TryModelsEndpointAsync`.

---

## 2. Moderate

### 2.1 `PricingPageVerifier` branches on string literals instead of `ProviderJsonFields` constants

**Severity: Fragile coupling / DRY**

[PricingPageVerifier.cs L110, L116](file:///Y:/user_alex_new/dot_net2022/Github/AIProviderConnect_private/ScraperTool/Services/Validation/Checks/PricingPageVerifier.cs):

```csharp
var (verdict, reason) = fieldName == "subscriptionPricingUrl"
    ? await _contentAnalyzer.AnalyzeSubscriptionPricingContentAsync(...)
    : await _contentAnalyzer.AnalyzeApiPricingContentAsync(...);

var expectedContent = fieldName == "subscriptionPricingUrl"
    ? "subscription/plan content (tiers, monthly pricing, etc.)"
    : "API pricing content (per-token, per-1M, cost, etc.)";
```

The verifier receives `fieldName` as a `string` and compares it against the literals `"subscriptionPricingUrl"` — which happen to equal `ProviderJsonFields.SubscriptionPricingUrl` and `ProviderJsonFields.ApiPricingUrl`. The caller (`UrlFieldChecker.CheckPricingFieldAsync` L74, L80) passes the constant values as string arguments, so it works today. But the single-field-name vocabulary that `ProviderJsonFields` exists to provide is bypassed: a rename of the constant would silently break the verifier's branch without a compile error.

**Fix:** branch on `ProviderJsonFields.SubscriptionPricingUrl` (the constant), not the literal.

### 2.2 `IUrlFieldChecker` doc comment misstates where the `continue` dispositions live

**Severity: Misleading contract**

The interface doc claims: *"All eleven `continue` dispositions from the original loop body are handled internally."*

In reality, of the 11 original `continue` sites:
- **4 are inside `UrlFieldChecker`**: applicability suppression (L58–60), private-host skip (L65–66)
- **7 remain in the host** (`ProviderDefinitionValidator`): blank value (L200), sentinel (L203–206), URI parse failure (L209), and the `TryGetProperty` / `ValueKind` gate (L196–197)

The doc describes the design the plan intended (`FieldCheckContext` was to carry the loop state) but the implementation split the continues across host and checker.

**Fix:** correct the doc to state which continues the checker handles, or move the remaining gates into the checker as the plan intended.

### 2.3 `FieldCheckContext` carries both data and infrastructure; interfaces re-declare its fields as positional parameters

**Severity: Parameter redundancy / SRP**

`FieldCheckContext` carries `FileName`, `Field`, `Url`, `Uri`, `Root`, `Sink`, `CancellationToken`, and a settable `PageHtml`. Yet the collaborator interfaces re-declare these as positional parameters:

- `IPricingPageVerifier.VerifyPricingUrlAsync(uri, url, fileName, fieldName, sink, ct)` — `fileName`, `fieldName`, and `sink` are already in the context
- `IPageContentProbe.ReadBodyForErrorPageAsync(url, fileName, field, sink, ct)` — same redundancy
- `IWebsiteOwnershipJudge.CheckWebsiteRootOwnershipAsync(uri, root, fileName, ct)` — `root` and `fileName` are in the context

The checker extracts `context.FileName`, `context.Field`, `context.Sink`, etc. into locals (L49–55) and then passes them individually to collaborators that also receive `context` in some cases. The context object exists to unify these parameters but is only passed to itself; collaborators get the deconstructed values instead.

**Fix:** pass `FieldCheckContext` to collaborators (or at minimum drop the parameters that duplicate context fields).

### 2.4 A zero-logic pass-through was reintroduced in `PageContentProbe.AnalyzeLoginSurfaceAsync`

**Severity: Unnecessary indirection**

[PageContentProbe.cs L53–58](file:///Y:/user_alex_new/dot_net2022/Github/AIProviderConnect_private/ScraperTool/Services/Validation/Checks/PageContentProbe.cs):

```csharp
public async Task<(ELoginUrlVerdict Verdict, string Reason)> AnalyzeLoginSurfaceAsync(
    string pageHtml, string finalUrl)
{
    return await _contentAnalyzer.AnalyzeLoginUrlAsync(pageHtml, finalUrl);
}
```

This is `return await _contentAnalyzer.AnalyzeLoginUrlAsync(pageHtml, finalUrl);` — no fetch, no defaulting, no interpretation. The plan justified deleting `TryHeadWithFallbackAsync` precisely for this shape (*"a pass-through with no fallback logic — its name describes behavior it does not have"*). Here the pass-through adds a second indirection (checker → probe → analyzer) for a call that is one line.

**Fix:** fold the judgment into `ReadBodyForErrorPageAsync` — return `(Body, LoginVerdict, LoginReason)` from the probe (it already holds `IContentAnalyzer`), so `UrlFieldChecker` makes one call and the L217–239 block shrinks.

### 2.5 `ScanProviderLinksAction.ExecuteAsync` ≈260 lines; Phase 10 not started

**Severity: Plan violation (method-length rule)**

Mandatory rule: *"no method over 150 lines in any touched file"*. `ExecuteAsync` spans L37–L296 (≈260 lines). The plan's Phase 10 explicitly targets this method for splitting into private steps (candidate collection, scoring/filtering, result construction).

Additionally, `ExecuteAsync` contains dead allocations:

```csharp
var candidateUrls = sortedList.Select(l => l.Url).ToList();  // L275
context.State.Properties["candidateCount"] = candidateUrls.Count;  // L276
["linkCount"] = candidateUrls.Count.ToString()  // L284
```

`candidateUrls` allocates a full `List<string>` via LINQ `Select` but is only used for `.Count`. `sortedList.Count` gives the same value (the `Select` is 1:1 with no filter).

**Fix:** implement Phase 10 as the plan orders it (last, after Phases 7–9). Replace `candidateUrls.Count` with `sortedList.Count` and delete the dead allocation.

---

## 3. Minor

### 3.1 No constructor null guards in any `Checks/` collaborator

**Severity: Inconsistency**

Zero files under `ScraperTool/Services/Validation/Checks/` contain `ArgumentNullException` or `ThrowIfNull`. All five `UrlFieldChecker` constructor parameters, both `PageContentProbe` parameters, both `PricingPageVerifier` parameters, `WebsiteOwnershipJudge`'s single parameter, `ApiEndpointProbe`'s two parameters, and `ServiceRetirementProbe`'s single parameter are assigned without guards.

Contrast: `ProviderDefinitionValidator` (5 of 8 guarded), `ScanProviderLinksAction` (3 of 3 guarded), `ProviderManifestReader` (1 of 1 guarded), `ValidationIssueSink` (1 of 2 guarded).

A DI misconfiguration passing `null` would produce a `NullReferenceException` at first use rather than a clear `ArgumentNullException` at construction time.

**Fix:** apply `ArgumentNullException.ThrowIfNull(x)` uniformly across all new collaborators.

### 3.2 `EFieldStep.cs` is dead code

**Severity: Dead code**

[EFieldStep.cs](file:///Y:/user_alex_new/dot_net2022/Github/AIProviderConnect_private/ScraperTool/Services/Validation/Checks/EFieldStep.cs) defines `EFieldStep { Next, SettleField }` — plan-mandated, 19 lines — and the only match for `EFieldStep` in the entire solution is its own definition. The implementation uses `Task<bool>` return values and early `return` statements instead.

**Fix:** wire `EFieldStep` into `UrlFieldChecker`'s control flow (replacing the `bool` returns from `TryCheckWebsiteRootAsync` / `TryCheckLoginUrlEqualityAsync`) or delete the file.

### 3.3 Bare `catch {}` in `ServiceRetirementProbe` swallows all exceptions including `OperationCanceledException`

**Severity: Error-handling policy violation**

[ServiceRetirementProbe.cs L117–120](file:///Y:/user_alex_new/dot_net2022/Github/AIProviderConnect_private/ScraperTool/Services/Validation/Checks/ServiceRetirementProbe.cs):

```csharp
catch
{
    // Retirement check is best-effort — don't fail validation on fetch errors.
}
```

A bare `catch {}` swallows not only `HttpRequestException` and `TaskCanceledException` (expected from a fetch) but also `NullReferenceException`, `ArgumentException`, and any other programming error. Worse, it also catches `OperationCanceledException`, so a user cancel during the retirement fetch is silently reported as "not retired" rather than propagating the cancellation.

**Fix:** catch only `HttpRequestException` and `TaskCanceledException`, and let unexpected exceptions (and `OperationCanceledException`) propagate.

### 3.4 `UrlFieldChecker.UrlTimeout` states a number that governs nothing

**Severity: Misleading constant**

`UrlTimeout = 10s` (L19) is used only to word `"request timed out after 10s"` (L94–95) and `"timed out after 10s"`. The actual cutoff is the named client's `Timeout = 12s` (`App.xaml.cs` L200). Contrast `ApiEndpointProbe.ApiProbeTimeout` (3s), which genuinely cancels via a linked CTS. The refactor moved the lie into a new file where the true source is no longer adjacent.

**Fix:** expose the configured timeout to the checker (or bind the message to the client's value).

### 3.5 `SubscriptionConfiguredChecker` passes empty URL to progress report

**Severity: Minor UX degradation**

[SubscriptionConfiguredChecker.cs L45–49](file:///Y:/user_alex_new/dot_net2022/Github/AIProviderConnect_private/ScraperTool/Services/Validation/Checks/SubscriptionConfiguredChecker.cs):

```csharp
sink.FailAppended(fileName, ProviderJsonFields.SubscriptionPricingUrl, "",
    "subscriptionPricingUrl not set");
```

The empty string `""` appears as a blank URL in the progress transcript. Behavior-preserved from the original code, but a degraded user experience.

### 3.6 `ServiceRetirementProbe` issues lack `Field` and `CurrentValue`

**Severity: Downstream impact on AI-fix routing**

[ServiceRetirementProbe.cs L103–107](file:///Y:/user_alex_new/dot_net2022/Github/AIProviderConnect_private/ScraperTool/Services/Validation/Checks/ServiceRetirementProbe.cs):

```csharp
sink.Append(new ValidationIssue(fileName, ValidationIssueCodes.ServiceRetired, message));
// ← no .Field = field, no .CurrentValue = fieldValue
```

The AI-fix pipeline routes suggestions by `(FileName, Field)`. Issues without a `Field` value cannot be individually routed. Behavior-preserved from the original code.

### 3.7 Mixed null-guard style in `ProviderDefinitionValidator` constructor

**Severity: Inconsistency**

[ProviderDefinitionValidator.cs L51–58](file:///Y:/user_alex_new/dot_net2022/Github/AIProviderConnect_private/ScraperTool/Services/ProviderDefinitionValidator.cs): three parameters are assigned without null guards (`_schemaValidator`, `_duplicateIdChecker`, `_metadataService`); five use `?? throw new ArgumentNullException(...)`.

### 3.8 `ValidationIssueSink` passed as method parameter through every collaborator interface

**Severity: Design observation (not a defect)**

Every collaborator interface takes `IValidationIssueSink` as a parameter on every method. The sink is also carried inside `FieldCheckContext`. The parameter and the record now both carry it — the context has it, but collaborators that receive the context also get the sink as a separate parameter. This is consistent with the plan's deliberate choice and is testable, but it doubles the carrier.

---

## 4. Plan adherence summary

| Contract item | Status |
| --- | --- |
| Characterization tests pin ordered progress + ordered issues, exact output | **Done** — 14 scenarios, exact string equality |
| No assertion edits in `ProviderDefinitionValidatorTests` (29 facts) | **Done** — arrange/`CreateValidator` only |
| Host keeps 4 public members, unchanged signatures | **Done** |
| Invariants 1–7 (suppression, single-fetch, website emission, continues, skip path, sidecar, duplicate-ids) | **Done** — transcript equality confirms |
| `TryHeadWithFallbackAsync` deleted | **Done** — 0 hits in solution |
| No collaborator takes `HttpClient`; `IHttpClientFactory` + named client | **Done** (validator side) |
| Host holds no transport / fetcher / analyzer / reachability checker | **Done** (validator side), **pending** (URL-fix) |
| Budgets: both hosts <250, every collaborator <250, no method >150 | **Failed** — validator 261, checker 382; `AiUrlFixService` 1189; `ScanProviderLinksAction.ExecuteAsync` ≈260 |
| `EFieldStep` carries the settle disposition | **Not done** — dead file |
| Sink collapses 36 progress blocks + 26 `issues.Add` | **Done** |
| URL-fix scope collaborators (run/batch/item), `ProcessBatchAsync` split | **Missing** |
| Phase 10 method-length fix | **Missing** |
| No `CHANGELOG.md`, no `ai-providers/*.json`, no library change | **Done** |
| `App.xaml.cs`: descriptor per new collaborator, no `CreateClient` in validator lambda | **Done** |
| Descriptor assertion tests (`AppServiceRegistrationTests`) | **Missing** — `ConfigureServices` widened to `internal` but no test calls it |

---

## 5. Suggested remediation order

1. **Budget + coverage together** (§1.1 + §1.2): write `UrlFieldCheckerTests` against the five seams, then move the website-path out of the checker (budget + coverage in one change).
2. **DRY consolidation** (§1.4 + §2.1 + §2.3): introduce `ProviderManifestView`, let `FieldCheckContext` carry options/flags, use `ProviderJsonFields` constants in the verifier — these touch the same files and should land once.
3. **Dead code + doc fix** (§3.2 + §2.2): wire `EFieldStep` or delete it; correct the `IUrlFieldChecker` doc.
4. **Error handling** (§3.3): narrow the bare `catch {}` in `ServiceRetirementProbe`.
5. **Null guards** (§3.1): apply `ArgumentNullException.ThrowIfNull` uniformly.
6. **Phases 7–9** (§1.3): URL-fix decomposition — reuse the validator-side lessons (sink-shaped reporter, one graph builder, no hand-wired arrange, `SuggestionVerifier` reuses `IApiEndpointProbe` / `UrlDomainRules` instead of re-implementing).
7. **Phase 10** (§2.5): `ScanProviderLinksAction` method-length fix last, as the plan orders. Delete the dead `candidateUrls` allocation.
