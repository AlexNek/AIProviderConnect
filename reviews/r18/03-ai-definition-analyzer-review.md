# AiDefinitionAnalyzer — SOLID & Clean Code Review

**Scope:** `ScraperTool/Services/AiDefinitionAnalyzer.cs` (229 lines)
**Reference:** `refactors/refactor18/library-solid-and-clean-code.md`

---

## Finding 1 — SRP violation: god method `AnalyzeProviderAsync`

**Severity:** High
**Category:** SRP / Clean Code

`AnalyzeProviderAsync` (lines 53–166) is a single ~110-line method that performs five distinct responsibilities:

1. Configuration guard (`IsAvailable` check)
2. HTML page fetching (two HTTP calls)
3. System prompt construction (10-line raw string)
4. User prompt construction (25-line string concatenation with HTML truncation)
5. AI invocation, response deserialization, and result mapping

The method mixes orchestration (fetch → build → call → parse) with low-level details (string truncation, JSON serialization). A change to prompt wording, truncation thresholds, or result mapping all require touching the same method.

**Recommendation:** Extract focused private methods:
- `BuildSystemPrompt()` — returns the system prompt string
- `BuildUserPrompt(provider, research, apiHtml, subHtml, scrapeError)` — encapsulates truncation + assembly
- `TruncateForPrompt(string html, int maxLength)` — shared truncation helper
- `MapAnalysisToBatch(analysis, provider)` — converts deserialized response to batch result

---

## Finding 2 — Dead deserialized fields waste LLM output tokens

**Severity:** High
**Category:** Clean Code / Waste

`AiAnalysisResponse` declares 8 data properties (`ApiPricingUrl`, `SubscriptionPricingUrl`, `TableXPath`, `RowOffset`, `ModelCellIndex`, `PromptCellIndex`, `CompletionCellIndex`, `PriceUnit`). The user prompt (lines 112–127) explicitly instructs the LLM to return all of them. The deserialization reads them. **None are consumed anywhere in the codebase** — only `analysis.Suggestions` is used (line 147).

This wastes output tokens on every call. The LLM is prompted to produce a JSON structure whose fields are immediately discarded.

**Recommendation:** Either consume the structured fields (e.g. surface them in `AiAnalysisBatch` for UI display) or remove them from both the prompt and `AiAnalysisResponse`. If the intent was future use, it should be documented or deferred until actually needed (YAGNI).

---

## Finding 3 — Dual configuration access path bypasses the abstraction

**Severity:** Medium
**Category:** SOLID / DIP

The constructor takes both `AiAnalysisService ai` and `AppSettings settings`. The `AiAnalysisService` already wraps `AppSettings` and exposes `SelectedProviderId`, `PrimaryModel`, `IsConfigured`, and `IsModelConfigured`. However, `SendPromptWithUsageAsync` (line 212–215) reads `_settings.SelectedProviderId` and `_settings.PrimaryModel` directly, bypassing the `AiAnalysisService` abstraction.

This creates two paths to the same configuration state:
- `IsAvailable` → `_ai.IsConfigured` (via `AiAnalysisService`)
- `SendPromptWithUsageAsync` → `_settings.SelectedProviderId` (direct)

If `AiAnalysisService.UpdateSettings` is called (which it is at runtime from the settings UI), the `AiDefinitionAnalyzer` sees the change through `_ai` but also directly through `_settings` — the same object, but the indirection is inconsistent and confusing.

**Recommendation:** Remove the `AppSettings` dependency. Use `_ai.SelectedProviderId` and `_ai.PrimaryModel` in `SendPromptWithUsageAsync`. This eliminates a constructor parameter and makes the abstraction consistent.

---

## Finding 4 — DRY violation: duplicated truncation pattern

**Severity:** Medium
**Category:** DRY

Lines 92–102 repeat the same null-check → length-check → slice → ellipsis pattern twice:

```csharp
var apiHtmlPreview = apiPricingHtml is not null
    ? apiPricingHtml.Length > 12000
        ? apiPricingHtml[..12000] + "..."
        : apiPricingHtml
    : "Page could not be fetched";

var subscriptionHtmlPreview = subscriptionPricingHtml is not null
    ? subscriptionPricingHtml.Length > 8000
        ? subscriptionPricingHtml[..8000] + "..."
        : subscriptionPricingHtml
    : "Page not set or could not be fetched";
```

Only the threshold (12000 vs 8000) and the fallback string differ. The structure is identical.

**Recommendation:** Extract a single `TruncateForPrompt(string? html, int maxLength, string fallback)` helper. Both call sites become one-liners.

---

## Finding 5 — Hardcoded provider name in error message

**Severity:** Medium
**Category:** Clean Code / Boundary Leak

Line 67: `"OpenRouter API key not configured."` — The string "OpenRouter" is hardcoded, but the actual provider is configurable via `_settings.SelectedProviderId`. If the user selects a different provider, this error message is misleading.

**Recommendation:** Use a provider-agnostic message: `"AI provider API key not configured."` or interpolate the actual provider ID from settings.

---

## Finding 6 — Silent exception swallowing with no diagnostics

**Severity:** Medium
**Category:** Clean Code / Robustness

`FetchPricingPageAsync` (lines 197–204) catches all exceptions with a bare `catch` and returns `null`:

```csharp
catch
{
    return null;
}
```

If the HTTP call fails (DNS error, TLS issue, timeout, 403, etc.), the failure is completely invisible. The calling prompt says "Page could not be fetched" but gives no indication of why. This makes debugging pricing-page analysis failures extremely difficult.

**Recommendation:** At minimum, log the exception via `ILogger<AiDefinitionAnalyzer>` (inject it). Alternatively, return a result type that carries the failure reason so it can be surfaced in the analysis output.

---

## Finding 7 — Passthrough properties enable transitive coupling

**Severity:** Low–Medium
**Category:** SOLID / ISP

Lines 31–33 expose internal dependencies as public properties:

```csharp
public AiAnalysisService AiService => _ai;
public ProviderCatalog Catalog => _catalog;
```

`AiFixConfigurationAdapter` reaches through `_analyzer.AiService.HasApiKey`, `_analyzer.AiService.HasProviderSelection`, etc. Multiple ViewModels reach through `_analyzer.Catalog.GetResearchMetadata(...)`. The analyzer is used as a service locator for its dependencies rather than having those dependencies injected where they are actually needed.

This creates transitive coupling: callers depend on `AiDefinitionAnalyzer` but actually need `AiAnalysisService` or `ProviderCatalog`. If the analyzer is refactored or replaced, every passthrough consumer breaks.

**Recommendation:** Inject `AiAnalysisService` and `ProviderCatalog` directly into the consumers that need them (`AiFixConfigurationAdapter`, ViewModels). Remove the passthrough properties from `AiDefinitionAnalyzer`.

---

## Finding 8 — Inconsistent `JsonSerializerOptions` strategy

**Severity:** Low
**Category:** Clean Code / DRY

Two separate `JsonSerializerOptions` instances exist:
- Line 15–19: `private static readonly JsonSerializerOptions JsonOptions` — `PropertyNameCaseInsensitive = true` (for deserialization)
- Line 176: `var options = new JsonSerializerOptions { WriteIndented = true }` — created fresh on every `BuildProviderJson` call (for serialization)

The deserialization options are cached as a static field, but the serialization options are allocated per call. Neither is shared with other classes that may need the same configuration.

**Recommendation:** Make both static readonly fields. If the pattern is reused elsewhere, consider a shared `JsonOptions` helper in the `Models` namespace.

---

## Finding 9 — Magic numbers without explanation

**Severity:** Low
**Category:** Clean Code

Lines 93, 99: `12000` and `8000` are character-count truncation thresholds for HTML content sent to the LLM prompt. They have no named constant, no comment explaining their origin (token budget? provider context window?), and no relationship to each other (why 12K for API and 8K for subscription?).

**Recommendation:** Extract to named constants: `private const int ApiPricingHtmlMaxChars = 12000;` and `private const int SubscriptionPricingHtmlMaxChars = 8000;`. Add a brief comment explaining the token-budget rationale.

---

## Summary

| # | Finding | Severity | Category |
|---|---------|----------|----------|
| 1 | God method `AnalyzeProviderAsync` — 5 responsibilities in 110 lines | High | SRP |
| 2 | 8 deserialized `AiAnalysisResponse` fields are never consumed; wasted LLM tokens | High | Clean Code / YAGNI |
| 3 | Dual config access path: `AppSettings` bypasses `AiAnalysisService` abstraction | Medium | SOLID / DIP |
| 4 | Duplicated truncation pattern (null → length → slice → fallback) | Medium | DRY |
| 5 | Hardcoded "OpenRouter" in error message despite configurable provider | Medium | Clean Code / Boundary Leak |
| 6 | Silent `catch` in `FetchPricingPageAsync` hides failure diagnostics | Medium | Clean Code / Robustness |
| 7 | Passthrough properties (`AiService`, `Catalog`) enable transitive coupling | Low–Medium | SOLID / ISP |
| 8 | Inconsistent `JsonSerializerOptions` — static for read, per-call for write | Low | Clean Code / DRY |
| 9 | Magic numbers `12000` / `8000` without named constants or rationale | Low | Clean Code |
