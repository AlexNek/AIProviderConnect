# Feature 12 — Manual browser verification: third-pass implementation review

## Scope

Independent pass over the feature 12 implementation. All findings re-verified against current source. Prior reviews `01-` and `02-` are acknowledged; this document carries forward unresolved items and adds new findings.

**Files reviewed:**

- `ScraperTool/Services/Validation/EManualVerificationResult.cs`
- `ScraperTool/Services/Validation/IManualBrowserVerifier.cs`
- `ScraperTool/Services/Validation/ManualBrowserVerifier.cs`
- `ScraperTool/Views/ManualVerificationWindow.xaml`
- `ScraperTool/Views/ManualVerificationWindow.xaml.cs`
- `ScraperTool/Services/Validation/Checks/UrlFieldChecker.cs` (integration)
- `ScraperTool/App.xaml.cs` (DI registration)
- `ScraperTool.Tests/Validation/ManualBrowserVerifierTests.cs`
- `ScraperTool.Tests/Validation/UrlFieldCheckerTests.cs`
- `ScraperTool.Tests/Validation/ValidatorGraphBuilder.cs`
- `WebTools.NET/Browsing/PlaywrightContentFetcher.cs` (duplication source)
- `WebTools.NET/Browsing/BrowserContentFetcherBase.cs` (duplication source)

**Spec:** `features/12/manual-browser-verification.md`

**Build:** `dotnet build ScraperTool/ScraperTool.csproj` — succeeds, 0 warnings, 0 errors.
**Tests:** 6/6 pass (1 `ManualBrowserVerifier` + 5 `HandleAuthRequired`).

---

## New findings

### Finding 1 — Cancel button does not close the dialog (UX bug)

**File:** `ManualVerificationWindow.xaml.cs`, lines 123–126

```csharp
private void OnCancel(object sender, RoutedEventArgs e)
{
    Result = EManualVerificationResult.UserCancelled;
}
```

`OnCancel` sets `Result` but never calls `Close()` or sets `DialogResult`. The dialog remains open after the user clicks Cancel or presses Escape (`IsCancel="True"` in XAML). The user must click the window chrome X to dismiss it.

By contrast, `OnDone` closes the dialog in its `finally` block (lines 117–119):

```csharp
await DisposeBrowserAsync();
DialogResult = true;
Close();
```

The X button works because `OnClosed` (line 128) calls `DisposeBrowserAsync`, and the `Closed` event (registered in `ManualBrowserVerifier`) completes the `TaskCompletionSource`. So the feature is functionally correct — the result is `UserCancelled` and the browser is disposed — but the UX is broken: the user clicks Cancel and nothing visible happens.

**Fix:** Add `DialogResult = false; Close();` (or just `Close()`) at the end of `OnCancel`.

---

### Finding 2 — Verified-path test masks the production defect (test gap)

**File:** `UrlFieldCheckerTests.cs`, lines 319–358

The test `HandleAuthRequired_ProtectionType_Verified_PassesManuallyVerified` mocks `_probeMock` to return success:

```csharp
_probeMock.Setup(p => p.ReadBodyForErrorPageAsync(...))
    .ReturnsAsync(new PageContentReadResult("page body"));
```

This makes the test pass, but in production the headless content probe will fail against the same Cloudflare protection the user just solved manually. The test does not assert:

- That `_probeMock.ReadBodyForErrorPageAsync` was called (it should NOT be called after manual verification — the user confirmed reachability; re-probing through headless is the bug).
- That no `FailAppended` issue was emitted after the "manually verified" pass.
- That the final sink state contains exactly one pass with the "manually verified" message.

**Fix:** Either (a) update the test to verify the probe is NOT called after `Verified` (which requires fixing the production code first — see carried Finding 3), or (b) add a separate test that asserts the probe is skipped when manual verification succeeds.

---

## Carried findings (re-verified, still unresolved)

### Finding 3 — Verified path re-enters headless content checks that will fail for Cloudflare sites (logic defect)

**File:** `UrlFieldChecker.cs`, lines 300–307

```csharp
if (result == EManualVerificationResult.Verified)
{
    context.Sink.Pass(context.FileName, context.Field, context.Url,
        "URL is reachable — manually verified");
    var verifiedStatus = new UrlCheckResult(true, 200, null, 0, context.Url);
    await HandleReachableResponseAsync(context, verifiedStatus, 200);
    return;
}
```

After the "manually verified" pass, `HandleReachableResponseAsync` is called. For `website`, `loginUrl`, and `documentationUrl` fields, this calls `_pageContentProbe.ReadBodyForErrorPageAsync` (line 197), which fetches the page through the same headless browser that was blocked by Cloudflare. The probe will likely fail, and `sink.HasIssuesSince(before)` at line 200 will emit a `FailAppended` right after the "manually verified" pass. The user sees both a pass and a failure for the same URL.

For `baseUrl`, the API probe uses a plain HTTP client which may succeed — this defect is field-dependent.

**Fix:** Skip content probes after manual verification. Return immediately after the "manually verified" pass.

---

### Finding 4 — Cancellation token is not propagated to the dialog (bug, resource leak)

**File:** `ManualBrowserVerifier.cs`, lines 25–39

The `CancellationToken` is only used in `tcs.Task.WaitAsync(ct)`. When the token is cancelled while the dialog is open:

1. `WaitAsync(ct)` throws `OperationCanceledException` on the caller.
2. The `ManualVerificationWindow` remains open on the UI thread (nothing closes it).
3. The `CloakBrowserHandle` launched inside the dialog is never disposed — `OnClosed` is never reached through normal flow.
4. The browser process leaks.

**Fix:** Register a cancellation callback on `ct` that closes the dialog on the UI thread (which triggers `OnClosed` → `DisposeBrowserAsync`).

---

### Finding 5 — View code-behind owns browser orchestration logic (SRP violation)

**File:** `ManualVerificationWindow.xaml.cs` (173 lines)

The dialog's code-behind contains significant infrastructure logic:

- CloakBrowser launch (`CloakLauncher.LaunchAsync`, line 50)
- Page navigation (`page.GotoAsync`, line 56)
- Challenge-wait script execution (`page.WaitForFunctionAsync`, line 90)
- Challenge detection (`IsBotChallengePageAsync`, line 94 and defined at line 150)
- Result evaluation (lines 95–97)

A WPF View should handle presentation and user interaction only. This coupling also makes the browser logic entirely untestable — it lives in a `Window` subclass with no seam for mocking.

**Suggested approach:** Extract a `ManualVerificationSession` (or similar) that owns the browser lifecycle and challenge detection. The dialog delegates to it and remains responsible only for button clicks, status text, and the `Result` property.

---

### Finding 6 — Browser context and page are not disposed (resource leak)

**File:** `ManualVerificationWindow.xaml.cs`, lines 53–54

```csharp
var context = await _handle.RawBrowser.NewContextAsync();
var page = await context.NewPageAsync();
```

Only `_handle` (the `CloakBrowserHandle`) is stored and disposed in `DisposeBrowserAsync`. The `IBrowserContext` and `IPage` are local variables that go out of scope without explicit disposal.

The codebase pattern in `CloakBrowserSearchProvider` (WebTools.NET) explicitly disposes both the context and the handle. While Playwright typically cleans up child objects when the parent browser closes, this is not guaranteed in edge cases (browser crash, disposal exception).

**Fix:** Store the `IBrowserContext` reference as a field and dispose it in `DisposeBrowserAsync` before disposing the handle.

---

### Finding 7 — `ShowDialog()` contradicts spec's "modeless dialog" requirement (spec deviation)

**File:** `ManualBrowserVerifier.cs`, line 36

The spec states: *"Show a modeless dialog (`ManualVerificationWindow`)"*. The implementation uses `dialog.ShowDialog()` which creates a **modal** dialog (blocks the owner window).

This is functionally correct for the use case — the validation pipeline needs to wait for the user's response before continuing. The spec wording should be updated to match.

**Severity:** Low — the modal behavior is the right design choice.

---

### Finding 8 — Duplicated challenge detection logic is consistent but fragile (DRY maintenance risk)

**Files:** `ManualVerificationWindow.xaml.cs` lines 15–26 and 150–171, vs `WebTools.NET/Browsing/PlaywrightContentFetcher.cs` lines 15–26 and `BrowserContentFetcherBase.cs` lines 170–191

**Verification:** The duplication is currently character-for-character consistent (same markers, same logic, same case-insensitive comparisons). The ScraperTool copy correctly omits the `CancellationToken` parameter from `IsBotChallengePageAsync` since the dialog does not need it.

**Severity:** Low — not a defect today, but a maintenance risk. A comment referencing the source of truth in WebTools.NET would mitigate drift.

---

### Finding 9 — `OnDone` proceeds with misleading status when no browser is open (dead code path)

**File:** `ManualVerificationWindow.xaml.cs`, lines 72–121

If `_handle` is null (browser was never opened or launch failed and was disposed), the `if (_handle is not null)` block is skipped. `Result` remains at its default `UserCancelled`, but the status text at lines 106–108 reads "Challenge still present — falling back to BotProtected." The user sees a misleading message even though no verification was attempted.

In practice the Done button starts disabled (`IsEnabled="False"` in XAML) and is only enabled after successful navigation (line 62), so this path is not reachable through normal interaction. However, the code should guard against it explicitly.

**Severity:** Low — unreachable through normal interaction.

---

## Summary

| # | Finding | Severity | Status |
|---|---------|----------|--------|
| 1 | Cancel button does not close dialog | Medium | **New** |
| 2 | Verified-path test masks production defect | Medium | **New** |
| 3 | Verified path re-enters headless content checks | Medium | Carried (01-F3, 02-F3) |
| 4 | Cancellation token not propagated to dialog | Medium | Carried (01-F1, 02-F1) |
| 5 | View code-behind owns browser orchestration | Medium | Carried (01-F2, 02-F2) |
| 6 | Browser context and page not disposed | Low | Carried (01-F4, 02-F4) |
| 7 | ShowDialog contradicts spec modeless | Low | Carried (01-F5, 02-F5) |
| 8 | Duplicated challenge detection logic | Low | Carried (01-F6, 02-F6) |
| 9 | OnDone misleading status when no browser | Low | Carried (02-F7) |

**Top remediation priorities:**

1. Fix Cancel button to close the dialog (Finding 1) — one-line fix, clear UX defect.
2. Skip content probes after manual verification (Finding 3) — logic defect that produces contradictory pass+failure output for the user.
3. Propagate cancellation to the dialog (Finding 4) — resource leak on cancellation.
4. Add test that verifies probe is NOT called after `Verified` (Finding 2) — prevents regression of Finding 3.

## What is correct

- The `EManualVerificationResult` enum and `IManualBrowserVerifier` interface match the spec exactly.
- `UrlFieldChecker` integration is correct for the `StillProtected`, `UserCancelled`, and exception branches: all fall back to the `BotProtected` informational pass unchanged.
- The `BotProtected` fallback behavior is preserved when manual verification is not offered, not wanted, or fails.
- DI registration as singleton is correct (the verifier holds no per-request state).
- Tests cover all five branches of `HandleAuthRequiredAsync` with `ProtectionType`: Verified, StillProtected, UserCancelled, exception, and no-ProtectionType. The verifier is correctly NOT called when `ProtectionType` is null.
- `Application.Current is null` guard in `ManualBrowserVerifier` is correct for non-WPF hosts.
- `Interlocked.Exchange` for browser handle disposal prevents double-dispose.
- `ValidatorGraphBuilder` correctly provides a default `UserCancelled` mock for existing tests that do not exercise the manual verification path.
- `OnClosed` override ensures `DisposeBrowserAsync` is called on any dialog close path.
- `OnOpenBrowser` correctly disposes the browser handle on launch failure.
