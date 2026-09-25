# Refactor 20 — Implementation Review (full scope, second pass)

Scope: the whole of `refactors/refactor20/scraper-tool-god-class-decomposition.md` — validator side (Phases 0–6, landed), URL-fix side (Phases 7–9) and `ScanProviderLinksAction` (Phase 10). `reviews/r20/01-…` reviewed Phases 0–6 only; this pass covers the plan end to end and does not re-argue its findings (see §5 for their status).

Evidence base: working tree at `c415c41`; the r20 code delta is commit `d777e5a` (33 files, `ScraperTool/**` + `ScraperTool.Tests/**`). Re-run at review time: `dotnet build ScraperTool/ScraperTool.csproj` → **0 warnings, 0 errors**; `dotnet test ScraperTool.Tests/ScraperTool.Tests.csproj` → **588 passed, 0 failed, 2 s**. No source file was modified by this review.

## Verdict

The validator decomposition is **behaviorally sound and honestly structured** — the transcript freeze is real (exact string equality over ordered progress + calls + requests + returned issues, 14 scenarios), no assertion was weakened, and no host any longer holds a transport. But the refactor is **half-delivered and under-verified**:

- Phases 7–10 are **entirely unimplemented** — `AiUrlFixService.cs` is still 1189 lines with a 454-line method, none of its 8 planned collaborators exists, `ScanProviderLinksAction.ExecuteAsync` still spans L37–L300.
- The plan's own **budget rule is broken** by the class that absorbed the control flow, and the checklist records that check as passed.
- The **seams were created but never exercised**: the highest-risk new class has no unit test and no new interface has a test double.
- The extraction **moved duplication instead of removing it** (raw `JsonElement` + 13 hand-rolled property reads + a duplicated `"SelfHosted"` category test), and **re-created the exact pass-through defect** the plan cited as a reason to delete `TryHeadWithFallbackAsync`.

None of these is a shipped defect: the working code is correct. They are plan violations and design debt that will price the next phase.

---

## 1. Major

### 1.1 Phases 7–10 not implemented; the plan's Goal is half delivered

**Severity: Major (scope completeness)**

| Planned | Reality |
| --- | --- |
| `Services/UrlFix/`: `AiFixReadinessGuard`, `ResearchContextFactory`, `IssueSemanticsEvaluator`, `SuggestionVerifier`, `BatchOutcomeCollector`, `BatchResultWriter`, `TokenUsageRecorder`, `UrlFixProgressReporter`, `UrlFixOutcome`, `FieldAnswer`, `BatchInternalResult` | `ScraperTool/Services/UrlFix/` holds only the pre-existing `UrlIntelligenceRuleKind.cs` / `UrlIntelligenceRules.cs` — no new file |
| `AiUrlFixService.cs` under 250 lines, `ProcessBatchAsync` split | 1189 lines; `ProcessBatchAsync` still L618–1072 (454 lines) — the file is untouched by `d777e5a` |
| Seven duplicated outcome-write blocks collapsed into `BatchOutcomeCollector` | still seven copies in place |
| `ScanProviderLinksAction.ExecuteAsync` split into private steps | L37 → next member at L302 ⇒ still ≈264 lines, over the 150-line rule |
| `HttpClient` gone from `AiUrlFixService` | still injected — `App.xaml.cs` L320–321 passes `IHttpClientFactory.CreateClient(ScraperHttpClientName)` into the host |

The checklist is honest (7–10 unchecked) and the Phase 0 freeze for that side already exists (`AiUrlFixServiceCharacterizationTests.cs`, 1225 lines, 18 `[Fact]`s), so the remaining work is unblocked. Two records do need fixing: `refactors/overview.md` row 20 (modified in the working tree) still reads `planned` although Phases 0–6 are committed, and it describes the whole design in the completed tense ("`HttpClient` is replaced … in every new type", "Also shortens `ScanProviderLinksAction.ExecuteAsync`") — the plan's Verification item "Update `refactors/overview.md` row 20 … and set the status" is still open.

**Fix:** treat Phases 7–9 as the continuation of this task rather than a new one; set overview row 20 status to reflect the validator side landed / URL-fix side pending.

### 1.2 Collaborator budget broken by `UrlFieldChecker`; host over budget under physical-line measure

**Severity: Major (plan violation, false verification record)**

Mandatory rule: *"Budgets: `ProviderDefinitionValidator.cs` and `AiUrlFixService.cs` under 250 lines each; every collaborator under 250 lines."*

| File | physical | non-blank | ≤250 |
| --- | --- | --- | --- |
| `Services/Validation/Checks/UrlFieldChecker.cs` | **381** | **341** | **✗ under both measures** |
| `Services/ProviderDefinitionValidator.cs` | **260** | 211 | ✗ physical / ✓ non-blank |
| `ApiEndpointProbe.cs` 155 · `PricingPageVerifier.cs` 129 · `SelfHostedApplicabilityEvaluator.cs` 138 · `ServiceRetirementProbe.cs` 122 · `WebsiteOwnershipJudge.cs` 118 · `PageContentProbe.cs` 72 · `SubscriptionConfiguredChecker.cs` 53 · `ValidationIssueSink.cs` 110 · `ProviderManifestReader.cs` 36 | | | ✓ |

`UrlFieldChecker` is now the largest file in the validation layer, 52 % over the ceiling. The split removed the 742-line method but concentrated all of its branching in one class: 8 methods, of which three are complete side-rules — `TryCheckWebsiteRootAsync` (L270–295), `TryCheckLoginUrlEqualityAsync` (L297–330), `HandleAuthRequired` (L332–361) — plus the 104-line `HandleReachableResponseAsync` (L165–268). Phase 4's verification *"Verify `ProviderDefinitionValidator.cs` is under 250 lines"* is checked `[x]` while the file is 260 lines long.

**Correction to `r20/01`:** its table (validator 211 ✓, `UrlFieldChecker` 341) used **non-blank** counts without saying so; on the conventional physical-line measure the host was also recorded as passing while it fails. The plan never defined the measure, which is how both records drifted.

**Fix:** (a) add one line to the plan defining "lines" (recommend physical, `Get-Content .Count`); (b) move the website-path pair (emission at L207–240 + L270–330) behind `IWebsiteOwnershipJudge`/`IPageContentProbe` as verdict-returning rules, or into one `WebsiteFieldRule` collaborator, which brings the checker to roughly 240 lines and is the same remedy `r20/01` §1 asked for — with 1.3's `UrlFieldCheckerTests` written first.

### 1.3 Zero direct tests for the new control-flow class; 8 of 9 new production types untested at their seam

**Severity: Major (test gap against the plan's Goal)**

The Goal is *"each field check, probe, verification … unit-testable without constructing the 8- or 11-dependency host."* Testable was delivered; tested was not:

- `Mock<IPageContentProbe>`, `Mock<IPricingPageVerifier>`, `Mock<IWebsiteOwnershipJudge>`, `Mock<IApiEndpointProbe>`, `Mock<IUrlFieldChecker>` → **0 occurrences** in `ScraperTool.Tests`.
- No `UrlFieldCheckerTests.cs`, `PageContentProbeTests.cs`, `PricingPageVerifierTests.cs`, `WebsiteOwnershipJudgeTests.cs`, `ApiEndpointProbeTests.cs`, `ServiceRetirementProbeTests.cs`, `ProviderManifestReaderTests.cs`, `ValidationIssueSinkTests.cs` exist (only 3 check tests were added: `UrlDomainRules`, `SelfHostedApplicabilityEvaluator`, `SubscriptionConfiguredChecker`).
- Phase 6's own text names the missing artifact — *"five for `UrlFieldCheckerTests`"* — yet the item is `[x]`. The four field-scope interfaces therefore add a mocking layer that nobody mocks and a DI indirection with exactly one consumer each; the plan's stated benefit (arrange one collaborator's own dependencies) is not yet realized anywhere.

**Fix:** add `UrlFieldCheckerTests` with the five mocks. It is the only cheap place to pin: branch dispatch (pricing vs reachability), the 401/403 / 429 / 404 / other classification, `TaskCanceledException` → `UrlTimeout` vs `OperationCanceledException` rethrow, the redirect cap, and — required by the plan's invariant 2 and its "same network-call count" promise — **`loginUrl` performs exactly one fetch and `AnalyzeLoginSurfaceAsync` consumes the body the probe already returned**. The freeze does not count per-field requests, so that invariant is currently unguarded.

### 1.4 The behavior freeze is hand-wired to the post-refactor object graph — one graph, three copies

**Severity: Major (DRY + the freeze is not design-independent)**

`ProviderDefinitionValidatorCharacterizationTests.cs` L867–882 and `ProviderDefinitionValidatorTests.cs` L1153–1169 each construct the identical graph —

```csharp
new UrlFieldChecker(urlChecker,
    new PageContentProbe(fetcher, analyzer),
    new PricingPageVerifier(fetcher, analyzer),
    new WebsiteOwnershipJudge(fetcher),
    new ApiEndpointProbe(httpClientFactory.Object, urlChecker));
// + new ProviderManifestReader(), new SelfHostedApplicabilityEvaluator(),
//   new SubscriptionConfiguredChecker(), new ServiceRetirementProbe(fetcher)
```

—the third copy living in `App.xaml.cs` L246–289. Three consequences:

1. **DRY:** adding one collaborator now costs three files; Phase 8–9 will do exactly that again on the URL-fix side, where the host takes eleven dependencies.
2. **The freeze can't freeze across a design change.** The plan's Verification item *"the characterization tests are untouched since Phase 0 — they are the behavior freeze"* is already false in spirit: Phase 1's own landing note records that *"the two test construction sites gained that argument in their arrange blocks only."* A characterization test whose arrange breaks when the object graph changes guards output but repins itself at every step.
3. The DI contract the plan asked for was skipped: `App.ConfigureServices` was widened from `private` to `internal` (App.xaml.cs L183) *"so tests can inspect the descriptors"* — **no test calls it** (grep: only `App.xaml.cs` L142). `AppServiceRegistrationTests.cs` asserts only reflection facts about constructors (L42–92) and a parameter *count* (`HaveCount(8)`, L61–72, a change-detector that will need editing in Phase 8–9 anyway). So the visibility widening has no consumer and the descriptor assertions the checklist claims were never written.

**Fix:** build the graph once — resolve the host from `App.ConfigureServices` + `BuildServiceProvider()` (already `internal` for this purpose) or via one `ScraperTool.Tests` graph helper — and have both test classes arrange fakes through it. Add the descriptor assertions `AppServiceRegistrationTests` was supposed to make, which also gives the Phase 9 URL-fix wiring the same guard for free.

### 1.5 DRY: raw `JsonElement` threaded through five collaborators; manifest reads re-implemented 13×

**Severity: Major (DRY / leaky representation)**

The plan's Goal is stated in terms of removing duplication, and `d777e5a` introduced new copies of the same read:

| Duplication | Sites |
| --- | --- |
| `TryGetProperty(...) && ValueKind == JsonValueKind.String ? GetString() : null` | 13 across the new/moved files: `SelfHostedApplicabilityEvaluator` 4, `ServiceRetirementProbe` 3, `SubscriptionConfiguredChecker` 2, `ProviderDefinitionValidator` 1, `WebsiteOwnershipJudge` 1, `ApiEndpointProbe` 1, `UrlFieldChecker` 1 |
| `"SelfHosted"` category test | `SelfHostedApplicabilityEvaluator.IsSelfHosted` L134–137 **and** `SubscriptionConfiguredChecker` L20–25 (plus pre-existing `ProviderSchemaValidator.cs:187`, `IsSelfHostedProviderPredicate.cs:19`) |
| `JsonElement Root` passed into collaborators | `IWebsiteOwnershipJudge.CheckWebsiteRootOwnershipAsync(uri, root, fileName, ct)`, `IApiEndpointProbe.TryModelsEndpointAsync(url, root, ct)`, `FieldCheckContext.Root`, host loop L196, `TryCheckLoginUrlEqualityAsync` L302 |

Every collaborator therefore re-parses the same manifest it was handed, and `FieldCheckContext` (whose purpose is to carry per-field state) carries raw JSON rather than resolved values. `IsSelfHosted` is additionally evaluated three times per run inside one `Evaluate` call (L39 / L74 / L109) re-reading the same property.

**Fix:** parse once into a small immutable read-model — `ProviderManifestView` (`IsSelfHosted`, `DisplayName`, `LoginUrl`, `ModelsEndpoint`, `TryGetString(field)`) built by the host next to `IProviderManifestReader` — and pass that instead of `JsonElement`. This removes 13 reads, both category literals, and the `Root` parameter from four signatures, and lets 1.1's Phase 8 heuristics (`ExtractRootDomain`, `IsDomainRelatedToSuggestion`) read the same view instead of re-parsing.

---

## 2. Moderate

### 2.1 A zero-logic pass-through was reintroduced in a new seam

**Severity: Moderate (the plan's own anti-pattern)**

`PageContentProbe.AnalyzeLoginSurfaceAsync` (L53–58) is `return await _contentAnalyzer.AnalyzeLoginUrlAsync(pageHtml, finalUrl);` — no fetch, no defaulting, no interpretation. The plan justified deleting `TryHeadWithFallbackAsync` precisely for this shape (*"a pass-through … with no fallback logic — its name describes behavior it does not have"*) and rejected static helpers because they *"mean … no mock seam"*. Here the pass-through adds a second indirection the login branch must traverse (checker → probe → analyzer) for a call that is one line.

**Fix:** fold the judgment into the read the body came from — `ReadBodyForErrorPageAsync` returns `(Body, LoginVerdict, LoginReason)` (the probe already holds `IContentAnalyzer`) — so `UrlFieldChecker` makes one call, L217–239 shrinks, and the 1.2 budget and the 1.3 mock count both improve.

### 2.2 Two competing carriers for the same state; field-name magic strings bypass `ProviderJsonFields`

**Severity: Moderate (DRY / parameter redundancy)**

`FieldCheckContext` exists precisely to carry `FileName / Field / Url / Uri / Root / Sink / ct`, yet the collaborator interfaces re-declare it as positional parameters: `IPricingPageVerifier.VerifyPricingUrlAsync(uri, url, fileName, fieldName, sink, ct)` and `IPageContentProbe.ReadBodyForErrorPageAsync(url, fileName, field, sink, ct)`. Worse, `CheckPricingFieldAsync` is called with the literals `"apiPricingUrl"` (L74) and `"subscriptionPricingUrl"` (L80), which **equal** `context.Field` (`ProviderJsonFields.ApiPricingUrl = "apiPricingUrl"`, `SubscriptionPricingUrl = "subscriptionPricingUrl"` — verified) and are then string-compared a third time inside the verifier (L110, L116). The single-field-name vocabulary that `ProviderJsonFields` exists to provide is bypassed in 4 new places, and the verifier's only caller passes an argument that is already in the object it also passes.

**Fix:** take `FieldCheckContext` (or `field` once, as `ProviderJsonFields.*`) instead of `fileName + fieldName`, and branch on the constant, not the literal.

### 2.3 `CheckFieldAsync(context, bool, bool, bool)` — suppression flags as positional booleans, and a doc that misstates where the `continue`s live

**Severity: Moderate (clean code / plan deviation)**

`IUrlFieldChecker.CheckFieldAsync(context, useLocalProviders, loginUrlIsNotApplicable, subscriptionUrlIsNotApplicable, apiPricingUrlIsNotApplicable)` (L42–47). Three same-typed flags in a row, whose order differs from `SelfHostedApplicabilityFlags`' own positional order, and any future suppression (or per-field option) widens the signature again. The plan assigned this state to the context: *"`FieldCheckContext.cs` … carry the loop state"*. `SelfHostedApplicabilityFlags` is then destructured into three locals in the host (L188–190) only to be re-passed positionally — the record exists and is immediately un-packed. The interface doc also claims *"All eleven `continue` dispositions from the original loop body are handled internally"* while three of them (blank, sentinel, unparseable URI) demonstrably remain in the host (L200, L206, L209).

**Fix:** pass `SelfHostedApplicabilityFlags` (plus `useLocalProviders`) inside `FieldCheckContext`/a `FieldCheckOptions` record so the checker takes one argument, and correct the doc comment.

---

## 3. Minor

**3.1 Dead code shipped by the commit.** `Checks/EFieldStep.cs` — 19 lines, plan-mandated, and the only match for `EFieldStep` in the repository is its own definition (`git grep` = 1 hit): the implementation used `bool`/`return` instead. `ProviderDefinitionValidatorTests.CreateWebMock()` L1172–1178 — no caller after the `IWebAccessService` parameter was dropped. Unused usings: `ScraperTool.Services.UrlFix` (`AppServiceRegistrationTests.cs` L7, nothing from it is referenced) and `AIProviderConnect.Models` (`ProviderDefinitionValidator.cs` L5, no type from it is used). Delete `EFieldStep` (or wire it in per 2.3), restore one helper or delete it, drop the two usings.

**3.2 `UrlFieldChecker.UrlTimeout` states a number that governs nothing.** `UrlTimeout = 10s` (L19) is used only to word `"request timed out after 10s"` (L94–95) and `"timed out after 10s"`; the actual cutoff for the injected fetcher/reachability path is the named client's `Timeout = 12s` (`App.xaml.cs` L200). Contrast `ApiEndpointProbe.ApiProbeTimeout` (3s), which really does cancel via a linked CTS. Pre-existing and protected by the plan's freeze on verdict wording, but the refactor moved the lie into a new file where the true source is no longer adjacent. Fix in a follow-up: expose the configured timeout to the checker (or bind the message to the client's value).

**3.3 Open items from `r20/01`, unchanged.** The bare `catch {}` in `ServiceRetirementProbe` L117–120 (now narrowed to nothing at all — it also swallows `OperationCanceledException`, so a user cancel during the retirement fetch is reported as "not retired"); retirement issues without `Field`/`CurrentValue` L103–107, which the AI-fix routes by `(FileName, Field)`; mixed null-guard style in the host ctor L51–58 (three bare assignments, five `?? throw`); empty `url` in `SubscriptionConfiguredChecker` L45–49. All four remain behavior-preserved and remain deferrable; the cancellation swallow in 3.3 is the one worth folding into the next touch.

---

## 4. Plan adherence

| Contract item | Status | Evidence |
| --- | --- | --- |
| Characterization tests pin ordered progress **and** ordered issues, and assert exact output | **Done** | `AssertTranscriptAsync` → `actual.Should().Be(golden)` (L768–775), 14 scenarios incl. self-hosted, subdomain website, loginUrl==website, pricing pair, baseUrl-404+models, bot-protected, retired, sidecar skip + expiry; CALLS/REQUESTS/RETURNED all in the transcript |
| No assertion edits in `ProviderDefinitionValidatorTests` (29 facts) | **Done** | `git show d777e5a` on that file: arrange/`CreateValidator` only |
| Host keeps 4 public members, unchanged signatures | **Done** | `ValidateDirectoryAsync` L69, `ValidateFileAsync` L103, `ValidateFileSync` L246, `ReadValidationMetadata` L64 |
| Invariant 1 (suppression before any request) | **Done, order changed harmlessly** | suppression now inside the checker (L58–60), after the sentinel/URI gates; safe because a flag is only set for a non-`"-"`, non-blank value (`SelfHostedApplicabilityEvaluator` L47–49, L82–84, L117–119) |
| Invariant 2 (loginUrl never fetches twice; the equality sub-check keeps its own second fetch) | **Done in code, untested** | `context.PageHtml` set L199 → read L220–223; second fetch L312–313. No test counts per-field requests (1.3) |
| Invariant 3 (website path emits `WebsiteIsSubdomain` / loginUrl-equals-website findings, in order) | **Done** | L209–215 + L270–330; frozen by `WebsiteOnProviderSubdomain_…`, `WebsiteThatAlsoServesAsLoginUrl_…` |
| Invariant 4 (eleven `continue` dispositions 1:1) | **Done** | 11 in the original loop (counted on `a3288fe` L224–841) = 3 host continues + 3 suppression + 1 private-host + 4 branch exits; transcript equality confirms |
| Invariants 5–7 (skip path = manifest re-read + subscription check only; sidecar after all work, gated on `persistValidation`; duplicate-ids stay directory-scoped) | **Done** | L139–171, L237–241, L97–98 |
| `TryHeadWithFallbackAsync` deleted, both callers use `IUrlReachabilityChecker` | **Done** | `git grep` = 0 hits; checker L136, probe L110 |
| No collaborator takes `HttpClient`; `IHttpClientFactory` + named client | **Done (validator side)** | `ApiEndpointProbe` L32–38; `AppServiceRegistrationTests` L26–58 |
| Hosts hold no transport / fetcher / analyzer / reachability checker | **Done (validator side), pending (URL-fix)** | ctor L41–58 vs `AiUrlFixService` 11 deps incl. `HttpClient` |
| Budgets: both hosts <250, every collaborator <250, no method >150 in touched files | **Failed** | §1.2 (host 260, checker 381/341); `AiUrlFixService` 1189 with a 454-line method; `ScanProviderLinksAction.ExecuteAsync` ≈264 |
| Scope collaborators for document + field level | **Done** | 9 classes + 9 interfaces + `UrlDomainRules`, all `sealed`, one type per file, `E`-prefixed enums |
| `EFieldStep` carries the settle disposition | **Not done** | dead file (§3.1) |
| Sink collapses 36 progress blocks + 26 `issues.Add` | **Done** | 36 `progress?.Report` sites in `a3288fe` → `git grep` for them in the new code = 1, inside `ValidationIssueSink` |
| URL-fix scope collaborators (run/batch/item), `ProcessBatchAsync` split | **Missing** | §1.1 |
| `App.xaml.cs`: descriptor per new collaborator, no `CreateClient` in the validator lambda | **Done**; descriptor assertion **Missing** | L246–289 has no `CreateClient`; tests never call `App.ConfigureServices` (1.4) |
| Phase 10 method-length fix | **Missing** | §1.1 |
| Update `refactors/overview.md` row 20 status | **Partial** | row added in working tree, status still `planned` |
| No `CHANGELOG.md`, no `ai-providers/*.json`, no library change | **Done** | `git show --stat d777e5a` touches `ScraperTool/**` and `ScraperTool.Tests/**` only |

## 5. Status of `reviews/r20/01` (re-verified, current lines)

| # | Prior finding | Status today |
| --- | --- | --- |
| 1 | `UrlFieldChecker` over 250-line budget (recorded 341) | **Still open, worse**: 381 physical / 341 non-blank — and the prior non-blank basis hid that the host (260) also fails. See 1.2 |
| 2 | `EFieldStep` dead code | **Still open** (§3.1) |
| 3 | `SelfHostedApplicabilityEvaluator` triplication | **Still open**, and part of a wider duplication than the prior review saw (1.5) |
| 4 | Bare `catch {}` in `ServiceRetirementProbe` | **Still open** (L117–120) |
| 5 | Empty URL in `SubscriptionConfiguredChecker` | **Still open** (L45–49) |
| 6 | Mixed null-guard style | **Still open** (L51–58) |
| 7 | Retirement issues missing `Field`/`CurrentValue` | **Still open** (L103–107) |
| 8 | Sink threaded everywhere (observation) | **Confirmed as designed**; note it is also carried inside `FieldCheckContext`, so the parameter and the record now both do it (2.2) |

None of the eight was remediated by the commit under review, and its line numbers were reported as non-blank counts without that being stated.

## 6. Checked and clean

- Freeze strength: exact-equality transcripts (no `Contains`/subset assertions) for both hosts, including sidecar calls, per-address transport requests and returned issues — a change in order or wording fails.
- Hermetic: 588 tests in 2 s; the probe transport is a `StubProbeHandler` and an unregistered address answers 404 rather than reaching the network; every baseUrl test that probes passes a handler.
- Validator DI shape matches the plan's list exactly (L246–289), lifetimes correct for stateless collaborators, host still `AddTransient`, duplicate-id check still directory-scoped.
- `sealed` on every new class, one type per file throughout, enums `E`-prefixed, public members documented.
- Error handling at field scope preserved: `catch (TaskCanceledException) when (!ct.IsCancellationRequested)` → `UrlTimeout`, and `catch (Exception) when (not OperationCanceledException and not TaskCanceledException)` → `UrlError`, wrapping the same region the original `try` did; `ApiEndpointProbe` distinguishes probe timeout from caller cancellation.
- No mutable state leaks between fields: `FieldCheckContext` is constructed inside the loop, so `PageHtml` cannot carry over.
- `ProviderManifestReader` keeps the `ReadError` / `FileNotFound` findings and the Serilog warning on a genuinely narrow catch.
- Library, `ai-providers/*.json`, and `CHANGELOG.md` untouched, as the plan requires.

## 7. Suggested order

1. 1.3 + 1.2 together: write `UrlFieldCheckerTests` against the five seams, then move the website-path out (budget + coverage in one change).
2. 1.5 + 2.2 + 2.3: introduce `ProviderManifestView`, let `FieldCheckContext` carry options/flags, retire the positional-boolean and duplicated-literal signatures — these three touch the same files and should land once.
3. 1.4: one graph builder + the descriptor assertions; this is also the precondition for not repeating the mistake in Phases 8–9.
4. 2.1 with them (same class, one fewer seam).
5. Phases 7–9 on the URL-fix side — reusing the validator-side lessons: `IValidationIssueSink`-shaped reporter, one graph builder, no hand-wired arrange, and a `SuggestionVerifier` that consumes `IApiEndpointProbe`/`UrlDomainRules` instead of re-implementing `ProbeBaseUrlIsApiAsync`/`ExtractRootDomain`.
6. Phase 10 (`ScanProviderLinksAction`) last, as the plan orders it.
