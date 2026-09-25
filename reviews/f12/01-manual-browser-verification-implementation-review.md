# Feature 12 — Manual browser verification: implementation review

## Scope

Files reviewed:

- `ScraperTool/Services/Validation/EManualVerificationResult.cs`
- `ScraperTool/Services/Validation/IManualBrowserVerifier.cs`
- `ScraperTool/Services/Validation/ManualBrowserVerifier.cs`
- `ScraperTool/Views/ManualVerificationWindow.xaml`
- `ScraperTool/Views/ManualVerificationWindow.xaml.cs`
- `ScraperTool/Services/Validation/Checks/UrlFieldChecker.cs` (integration only)
- `ScraperTool/App.xaml.cs` (DI registration only)
- `ScraperTool.Tests/Validation/ManualBrowserVerifierTests.cs`
- `ScraperTool.Tests/Validation/UrlFieldCheckerTests.cs` (manual-verifier tests only)
- `ScraperTool.Tests/Validation/ValidatorGraphBuilder.cs`

Spec: `features/12/manual-browser-verification.md`

---

## Finding 1 — Cancellation token is not propagated to the dialog (bug)

**File:** `ManualBrowserVerifier.cs`

The `CancellationToken` passed to `TryVerifyAsync` is only used in `tcs.Task.WaitAsync(ct)`. It is never passed to the `ManualVerificationWindow`. If the token is cancelled while the dialog is open:

1. `WaitAsync(ct)` throws `OperationCanceledException` on the background thread.
2. The dialog remains open on the UI thread.
3. The CloakBrowser instance launched inside the dialog keeps running.
4. The `CloakBrowserHandle` is never disposed because the dialog's `OnClosed` is never reached through normal flow.

This is a resource leak (browser process) and a UX defect (orphaned dialog).

**Fix:** Pass the `CancellationToken` to the dialog. Register a cancellation callback that closes the dialog on the UI thread (which triggers `OnClosed` → `DisposeBrowserAsync`). Alternatively, observe `ct` inside the dialog and close itself when cancellation is requested.

---

## Finding 2 — View code-behind owns browser orchestration logic (SRP violation)

**File:** `ManualVerificationWindow.xaml.cs`

The dialog's code-behind contains significant business/infrastructure logic:

- CloakBrowser launch (`CloakLauncher.LaunchAsync`)
- Page navigation (`page.GotoAsync`)
- Challenge-wait script execution (`page.WaitForFunctionAsync`)
- Challenge detection (`IsBotChallengePageAsync`)
- Result evaluation (challenge-cleared judgment)

A WPF View should handle presentation and user interaction only. The browser orchestration belongs in a ViewModel or in the `ManualBrowserVerifier` service itself. This separation also makes the browser logic testable — right now it is entirely untestable because it lives in a `Window` subclass.

**Suggested approach:** Extract a `ManualVerificationSession` (or similar) that owns the browser lifecycle and challenge detection, and have the dialog delegate to it. The dialog remains responsible only for button clicks, status text, and result property.

---

## Finding 3 — Verified path re-enters headless content checks that will fail for Cloudflare sites (logic defect)

**File:** `UrlFieldChecker.cs`, lines 300–307

When manual verification returns `Verified`, the code emits a pass and then calls `HandleReachableResponseAsync` with a synthetic `UrlCheckResult(true, 200, ...)`. For `website`, `loginUrl`, and `documentationUrl` fields, this method calls `_pageContentProbe.ReadBodyForErrorPageAsync`, which fetches the page through the same headless browser that was blocked by Cloudflare in the first place.

The result: the content probe will likely fail (still blocked by Cloudflare), and `sink.HasIssuesSince(before)` will emit a failure right after the "manually verified" pass. The user sees both a pass and a failure for the same URL.

For `baseUrl`, the API probe uses a plain HTTP client which may succeed, so this defect is field-dependent.

**Fix options:**
- Skip content probes after manual verification (the user confirmed the URL is reachable; content analysis is not possible through a headless browser for Cloudflare-protected sites).
- Or pass the manually-verified browser page content to the content probe instead of re-fetching.

---

## Finding 4 — Browser context and page are not disposed (resource leak risk)

**File:** `ManualVerificationWindow.xaml.cs`, `OnOpenBrowser`

```csharp
var context = await _handle.RawBrowser.NewContextAsync();
var page = await context.NewPageAsync();
```

Only `_handle` (the `CloakBrowserHandle`) is disposed in `DisposeBrowserAsync`. The `IBrowserContext` and `IPage` are not explicitly disposed. While Playwright typically cleans up child objects when the parent browser closes, this is not guaranteed across all edge cases (e.g., if the browser process crashes or the disposal throws). Explicit disposal of the context (or at minimum the page) is more robust.

**Fix:** Store the context reference and dispose it in `DisposeBrowserAsync` before disposing the handle.

---

## Finding 5 — `ShowDialog()` contradicts spec's "modeless dialog" requirement (spec deviation)

**File:** `ManualVerificationWindow.xaml.cs`, line in `ManualBrowserVerifier.cs`

The spec states: "Show a modeless dialog (`ManualVerificationWindow`)". The implementation uses `dialog.ShowDialog()` which creates a **modal** dialog (blocks the owner window).

This is functionally the correct behavior for this use case — the validation pipeline needs to wait for the user's response before continuing. A modeless `Show()` would require separate lifetime management. However, the discrepancy between spec and implementation should be documented or the spec should be updated.

---

## Finding 6 — Duplicated challenge detection logic is consistent but fragile (DRY)

**Files:** `ManualVerificationWindow.xaml.cs` vs `WebTools.NET/Browsing/PlaywrightContentFetcher.cs` and `BrowserContentFetcherBase.cs`

The `ChallengeWaitScript` JavaScript constant and the `IsBotChallengePageAsync` method are duplicated from WebTools.NET. The spec explicitly acknowledges this as an intentional trade-off ("they are not accessible"). The duplication is currently consistent (same markers, same logic).

This is not a defect but a maintenance risk: if WebTools.NET adds or removes challenge markers, the ScraperTool copy will drift silently. A comment referencing the source of truth would mitigate this.

---

## What is correct

- The `EManualVerificationResult` enum and `IManualBrowserVerifier` interface match the spec exactly.
- `UrlFieldChecker` integration is correct: `Verified` → pass + content checks; `StillProtected`/`UserCancelled`/exception → `BotProtected` fallback.
- The `BotProtected` fallback behavior is preserved unchanged when manual verification is not offered, not wanted, or fails.
- DI registration as singleton is correct (the verifier holds no per-request state).
- Tests cover all five branches of `HandleAuthRequiredAsync` with `ProtectionType`: Verified, StillProtected, UserCancelled, exception, and no-ProtectionType.
- `Application.Current is null` guard is correct for non-WPF hosts.
- `Interlocked.Exchange` for browser handle disposal prevents double-dispose.
- `ValidatorGraphBuilder` correctly provides a default `UserCancelled` mock for existing tests that don't exercise the manual verification path.
