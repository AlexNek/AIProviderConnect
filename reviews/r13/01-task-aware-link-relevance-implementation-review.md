# Review: Refactor 13 — Decision-tree pipeline data quality and classification intelligence

**Review timestamp:** 2026-08-29  
**Repository:** `Y:\user_alex_new\dot_net2022\Github\AIProviderConnect_private`  
**Branch:** `master`  
**Review basis:** Working tree (uncommitted changes). No specific commit reviewed.  
**Reviewed plan:** `refactors/refactor13/refactor13-task-aware-link-relevance.md`

## Review basis and inspected material

**Verified fact:** The review inspected the following source files:

- **Formatting abstractions and implementations:**
  - `ScraperTool/Services/UrlResearch/DecisionTree/Formatting/IHtmlTagCleaner.cs`
  - `ScraperTool/Services/UrlResearch/DecisionTree/Formatting/HtmlTagCleaner.cs`
  - `ScraperTool/Services/UrlResearch/DecisionTree/Formatting/IStringListFormatter.cs`
  - `ScraperTool/Services/UrlResearch/DecisionTree/Formatting/StringListFormatter.cs`
  - `ScraperTool/Services/UrlResearch/DecisionTree/Formatting/ITextSummarizer.cs`
  - `ScraperTool/Services/UrlResearch/DecisionTree/Formatting/TextSummarizer.cs`

- **Quality abstractions and implementations:**
  - `ScraperTool/Services/UrlResearch/DecisionTree/Quality/ICandidateRegionQualityAssessor.cs`
  - `ScraperTool/Services/UrlResearch/DecisionTree/Quality/CandidateRegionQualityAssessor.cs`
  - `ScraperTool/Services/UrlResearch/DecisionTree/Quality/ICandidateUrlProvider.cs`
  - `ScraperTool/Services/UrlResearch/DecisionTree/Quality/CandidateUrlProvider.cs`
  - `ScraperTool/Services/UrlResearch/DecisionTree/Quality/UrlResearchDecisionDataPolicy.cs`

- **Actions (all files under `ScraperTool/Services/UrlResearch/DecisionTree/Actions/`):**
  - `ScanProviderLinksAction.cs`, `FetchNextCandidateAction.cs`, `FetchModelsPageAction.cs`, `WebSearchAction.cs`, `QueryModelsEndpointAction.cs`, `CompareModelCountAction.cs`, `InitVerificationStateAction.cs`, `RecordCurrentFactAction.cs`, `VerifyReachableAction.cs`

- **Infrastructure:**
  - `DeterministicLinkScanner.cs`
  - `DecisionTreeResearchService.cs`
  - `DependencyInjection/UrlResearchServiceCollectionExtensions.cs`

- **Tree JSON configs (all 8 files under `ScraperTool/Config/trees/`)**

- **Test files:**
  - `ScraperTool.Tests/DecisionTreeDataQualityTests.cs`
  - `ScraperTool.Tests/DecisionTreeScannerTests.cs`
  - `ScraperTool.Tests/DecisionTreeTokenUsageTests.cs`
  - `ScraperTool.Tests/DecisionTreeActionTests.cs`

**Verified fact:** No library source code (`AiClevernessLib`) was modified. No bug doc was created in `bugs/` for token tracking (D6).

## Validation performed

**Verified fact:** The following command was run independently in this review:

```text
dotnet test ScraperTool.Tests --no-restore --verbosity minimal
```

Result: `Passed! - Failed: 0, Passed: 155, Skipped: 0, Total: 155, Duration: 435 ms`. All tests pass.

**Verified fact:** No full solution build, live network test, or end-to-end tree execution against a real provider was performed in this review.

## Phase-by-phase assessment

### Phase 1 — Clean data in (D1): Complete

**Verified fact:** `HtmlTagCleaner` implements `IHtmlTagCleaner` using compiled `GeneratedRegex` for tag stripping (`<[^>]+>`) and whitespace normalization. It decodes HTML entities via `WebUtility.HtmlDecode`. `DeterministicLinkScanner.CleanText` delegates to `_htmlTagCleaner.Clean()` before applying length truncation.

**Verified fact:** `DeterministicLinkScanner` receives `IHtmlTagCleaner` via constructor injection with `ArgumentNullException` guard.

**Verified fact:** Tests cover nested HTML tag stripping, entity decoding, empty input, and plain text passthrough.

**Assessment:** Clean implementation. Single responsibility, interface-backed, testable. No issues.

### Phase 2 — Readable state (D2, D3): Incomplete

**Verified fact:** The following actions correctly use formatting abstractions for state values:

- `ScanProviderLinksAction` stores `candidateUrls` via `IStringListFormatter.FormatSummary()` — named constants `MaxUrlsInSummary = 5`, `MaxUrlLengthInSummary = 80`.
- `WebSearchAction` stores `candidateUrls` via `IStringListFormatter.FormatSummary()` — same constants.
- `FetchNextCandidateAction` stores `lastFetchedContent` via `ITextSummarizer.Summarize()` — named constant `MaxFetchedContentSummaryLength = 500`.
- `FetchModelsPageAction` stores `modelsPageContent` via `ITextSummarizer.Summarize()` — named constant `MaxModelsPageContentSummaryLength = 500`.

**Verified fact — defect:** `QueryModelsEndpointAction` line 116 stores `allModelIds` (a `List<string>`) directly into `context.State.Properties["modelIds"]`. When the library calls `.ToString()` on this value, the LLM receives `System.Collections.Generic.List`1[System.String]` instead of model IDs. This is exactly the D2 defect that Phase 2 was designed to fix. The `IStringListFormatter` abstraction exists and is used by `ScanProviderLinksAction` and `WebSearchAction`, but was not applied here.

**Verified fact:** The corresponding test `QueryModelsEndpoint_Success_ParsesModelIds` asserts `ctx.State.Properties["modelIds"] as List<string>`, confirming the raw type is stored. This test will need updating alongside the fix.

**Verified fact — minor issue:** `FetchNextCandidateAction.FormatVisitedUrls` joins all visited URLs into an unbounded comma-separated string stored in `context.State.Properties["visitedUrls"]`. As the tree visits more candidates, this string grows without bound — the same D3 problem (unbounded state size) that Phase 2 was meant to address.

### Phase 3 — Focused evidence (D4): Complete

**Verified fact:** `DeterministicLinkScanner.MaxCandidateLinks` is 15, reduced from the previous 50.

**Verified fact:** `UrlResearchDecisionDataPolicy` implements `IDecisionDataPolicy`, wraps `DefaultDecisionDataPolicy`, and filters out `CandidateLink` and `SearchResult` evidence items before classify nodes. For non-classify nodes, all evidence passes through unchanged.

**Verified fact:** The policy is registered in DI via `services.Replace(ServiceDescriptor.Singleton<IDecisionDataPolicy>(...))`, replacing the default policy.

**Verified fact:** Tests verify filtering for classify nodes and pass-through for non-classify nodes.

**Assessment:** Solid implementation. The decorator pattern over `DefaultDecisionDataPolicy` is clean and open for extension.

### Phase 4 — Content quality (D7): Complete with duplication concern

**Verified fact:** `CandidateRegionQualityAssessor` implements `ICandidateRegionQualityAssessor` with two named constants: `MinimumCombinedContentLength = 200` and `MinimumAverageRegionLength = 50`. Returns false for empty regions, below-threshold combined length, or below-threshold average length.

**Verified fact:** Both `FetchNextCandidateAction` and `FetchModelsPageAction` use the quality assessor and fall back to full Markdown when regions are low quality.

**Verified fact:** Tests verify the low-quality fallback behavior.

**Verified fact — duplication concern:** The region analysis → quality check → Markdown fallback logic block is nearly identical in both `FetchNextCandidateAction` (lines 116–141) and `FetchModelsPageAction` (lines 82–107). The refactor plan's mandatory rules state: *"No duplication — if two actions format state values the same way, extract a shared method"* and *"Every new data quality operation must be a separate, testable class behind an interface."* This pattern should be extracted into a dedicated class (e.g., `ICandidateRegionContentSelector`).

### Phase 5 — Task-aware classification prompts (D5): Complete

**Verified fact:** All 6 tree JSONs with classify nodes have been updated with task-aware guidance:

| Tree | Task guidance pattern |
|------|-----------------------|
| `subscriptionPricingUrl.json` | "Base your answer on what the page content itself is about. Navigation links and links to other pages may be signals but do not define what this page IS; prefer the page's own content and links directly relevant to subscription or API pricing." |
| `apiPricingUrl.json` | Same universal pattern, task-specific to API pricing. |
| `documentationUrl.json` | Same pattern, task-specific to API documentation. |
| `loginUrl.json` | Same pattern, task-specific to login/authentication. |
| `website.json` | Same pattern, task-specific to official website. |
| `modelDescription.json` | Adapted for model list context: "Links to other pages or model names may provide signals but do not override the actual list content; prefer direct evidence from the model list itself." |

**Verified fact:** `baseUrl.json` and `minModelCount.json` were not modified. `baseUrl.json` has no classify node that receives page content. `minModelCount.json` has no classify node at all.

**Assessment:** Consistent, universal, task-specific. No provider-specific hardcoding.

### Phase 6 — Token tracking (D6): Complete

**Verified fact:** `DecisionTreeTokenUsageTests` verifies that token usage from a scripted LLM pipeline flows through `DecisionTreeExecutor` to the result's `Usage.InputTokens` and `Usage.OutputTokens`.

**Verified fact:** `DecisionTreeResearchService` lines 128–129 accumulate tokens: `context.PromptTokens += result.Usage.InputTokens; context.CompletionTokens += result.Usage.OutputTokens;`.

**Verified fact:** No bug doc was created in `bugs/` for D6. The test confirms the library already propagates token usage correctly, so no library defect exists.

**Assessment:** Token tracking works. No bug doc needed since the library behavior is correct.

## Findings

### F-01 — Critical: `QueryModelsEndpointAction` stores raw `List<string>` in state (D2 not fixed)

**Verified fact:** `QueryModelsEndpointAction.ExecuteAsync` line 116:

```csharp
context.State.Properties["modelIds"] = allModelIds;
```

`allModelIds` is `List<string>`. The library's context builder calls `.ToString()` on state values, producing `System.Collections.Generic.List`1[System.String]` instead of readable model IDs.

**Impact:** The LLM receives a .NET type name instead of model data when classifying against model list evidence. This directly contradicts the D2 defect the refactor was meant to fix and the plan's mandatory rule: *"The consumer must ensure every stored value produces a meaningful string."*

**Recommended action:** Inject `IStringListFormatter` into `QueryModelsEndpointAction` and replace the raw list storage with a bounded summary string. Update `QueryModelsEndpoint_Success_ParsesModelIds` test to assert the formatted string instead of casting to `List<string>`.

### F-02 — High: Region quality check and Markdown fallback logic is duplicated

**Verified fact:** `FetchNextCandidateAction` lines 116–141 and `FetchModelsPageAction` lines 82–107 contain nearly identical logic: initialize `llmContent` from Markdown, attempt HTML analysis, take top N regions, check quality assessor, join region text or fall back to Markdown, swallow non-cancel exceptions.

**Impact:** A future change to the region selection strategy (e.g., different joining separator, additional quality metrics, different exception handling) must be applied in two places. The refactor plan's mandatory rules explicitly prohibit this duplication.

**Recommended action:** Extract a shared `ICandidateRegionContentSelector` (or similar) with a method like `SelectLlmContent(markdownContent, htmlContent, sourceUri)` that encapsulates the analysis → quality check → fallback pipeline. Both actions would depend on this abstraction via constructor injection.

### F-03 — Medium: `visitedUrls` stored as unbounded comma-joined string

**Verified fact:** `FetchNextCandidateAction.FormatVisitedUrls` joins all visited URLs without any bound:

```csharp
return string.Join(VisitedUrlSeparator.ToString(), visitedUrls);
```

**Impact:** As the tree visits more candidates, this state value grows without bound. The LLM receives an increasingly long string of URLs in the state context, consuming tokens and diluting attention. This is the same D3 problem (unbounded state) that Phase 2 was meant to address.

**Recommended action:** Use `IStringListFormatter` for visited URLs, or store only the count. The full URL history is available in the data store as `PageText` evidence items if needed.

### F-04 — Low: `UrlResearchDecisionDataPolicy` has an unnecessary null guard

**Verified fact:** Line 24: `ArgumentNullException.ThrowIfNull(context.ClassifyNode);`. The `ClassifyNode` property on `DecisionDataSelectionContext` is a structural requirement of the library — it is the node currently being visited. If it were null, the tree execution would have failed before reaching the policy.

**Impact:** Minor noise. The guard is defensive against an impossible state.

**Recommended action:** Consider removing the guard. If the library contract guarantees non-null `ClassifyNode`, the check is unnecessary. If it does not, the guard should remain but the policy should handle the null case gracefully rather than throwing.

### F-05 — Low: `ScanProviderLinksAction` depends on concrete `DeterministicLinkScanner`

**Verified fact:** `ScanProviderLinksAction` constructor takes `DeterministicLinkScanner` (concrete class) rather than an interface.

**Impact:** Minor DIP violation. The scanner cannot be replaced with a mock or alternative implementation without modifying the action. This is a pre-existing condition, not introduced by this refactor.

**Recommended action:** Consider introducing `IDeterministicLinkScanner` if the scanner needs to be mockable or replaceable. Low priority since this is not a new issue.

## SOLID and clean code assessment

| Principle | Assessment |
|-----------|------------|
| **Single Responsibility** | Each formatting/quality operation is a dedicated class with one job. |
| **Open/Closed** | All quality operations are behind interfaces — new implementations addable without modification. |
| **Dependency Inversion** | All new dependencies are via constructor injection with `ArgumentNullException` guards. Exception: `ScanProviderLinksAction` → `DeterministicLinkScanner` (pre-existing). |
| **Meaningful names** | All classes, methods, and constants are well-named. |
| **No magic numbers** | All limits are named constants (`MaxCandidateLinks`, `MaxDescriptionLength`, `MinimumCombinedContentLength`, etc.). |
| **Fail fast** | All constructors validate inputs. |
| **Testability** | Every new class is testable in isolation. 155 tests pass, all hermetic. |
| **No duplication** | **Violated** — the region-quality-and-fallback pattern is duplicated across two actions (F-02). |

## Calibrated verdict

**Confirmed complete:** Phases 1, 3, 4, 5, and 6 are fully implemented per the plan. Phase 2 is partially complete — 3 of 4 target actions are fixed, but `QueryModelsEndpointAction` was missed.

**Confirmed defects remaining:**

1. `QueryModelsEndpointAction` stores raw `List<string>` in state (F-01) — the D2 defect is not fixed for this action.
2. Region quality/fallback logic duplication (F-02) — violates the plan's mandatory DRY rule.
3. Unbounded `visitedUrls` state (F-03) — the D3 defect is not fully addressed for this state property.

**Validation actually performed:** `ScraperTool.Tests` — 155/155 passed. No full solution build, no live network test, no end-to-end tree execution against a real provider.
