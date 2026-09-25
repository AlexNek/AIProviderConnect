# Feature 12 — Manual browser verification: source-verified review

## Scope

Files reviewed (all paths verified against current source):

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

Spec: `features/12/manual-browser-verification.md`

Build: `dotnet build ScraperTool/ScraperTool.csproj` — **succeeds, 0 warnings, 0 errors**.
Tests: 6/6 pass (1 `ManualBrowserVerifier` + 5 `HandleAuthRequired`).

---

## Finding 1 — Cancellation token is not propagated to the dialog (bug, resource leak)

**File:** `ManualBrowserVerifier.cs`, lines 25–39

The `CancellationToken` is only used in `tcs.Task.WaitAsync(ct)`. When the token is cancelled while the dialog is open:

1. `WaitAsync(ct)` throws `OperationCanceledException` on the caller.
2. The `ManualVerificationWindow` remains open on the UI thread (nothing closes it).
3. The `CloakBrowserHandle` launched inside the dialog is never disposed — `OnClosed` is never reached through normal flow.
4. The browser process leaks.

**Fix:** Register a cancellation callback on `ct` that closes the dialog on the UI thread (which triggers `OnClosed` → `DisposeBrowserAsync`). The dialog reference must be captured before `ShowDialog` blocks.

---

## Finding 2 — View code-behind owns browser orchestration logic (SRP violation)

**File:** `ManualVerificationWindow.xaml.cs` (172 lines)

The dialog's code-behind contains significant infrastructure logic:

- CloakBrowser launch (`CloakLauncher.LaunchAsync`, line 50)
- Page navigation (`page.GotoAsync`, line 56)
- Challenge-wait script execution (`page.WaitForFunctionAsync`, line 90)
- Challenge detection (`IsBotChallengePageAsync`, line 94 and defined at line 150)
- Result evaluation (lines 95–97)

A WPF View should handle presentation and user interaction only. This coupling also makes the browser logic entirely untestable — it lives in a `Window` subclass with no seam for mocking.

**Suggested approach:** Extract a `ManualVerificationSession` (or similar) that owns the browser lifecycle and challenge detection. The dialog delegates to it and remains responsible only for button clicks, status text, and the `Result` property.

---

## Finding 3 — Verified path re-enters headless content checks that will fail for Cloudflare sites (logic defect)

**File:** `UrlFieldChecker.cs`, lines 300–307

```csharp
if (result == EManualVerificationResult.Verified)
{
    context.Sink.Pass(context.FileName, context.Field, context.Url,
        "URL is reachable — manually verified");
    var verifiedStatus = new UrlCheckResult(
        true, 200, null, 0, context.Url);
    await HandleReachableResponseAsync(context, verifiedStatus, 200);
    return;
}
```

When manual verification returns `Verified`, the code emits a pass and then calls `HandleReachableResponseAsync` with a synthetic `UrlCheckResult(true, 200, ...)`. For `website`, `loginUrl`, and `documentationUrl` fields, `HandleReachableResponseAsync` (lines 196–204) calls `_pageContentProbe.ReadBodyForErrorPageAsync`, which fetches the page through the same headless browser that was blocked by Cloudflare in the first place.

The content probe will likely fail (still blocked by Cloudflare), and `sink.HasIssuesSince(before)` at line 200 will emit a failure via `sink.FailAppended` right after the "manually verified" pass. The user sees both a pass and a failure for the same URL.

For `baseUrl`, the API probe uses a plain HTTP client (not the headless browser), so it may succeed — this defect is field-dependent.

**Test gap:** The existing test `HandleAuthRequired_ProtectionType_Verified_PassesManuallyVerified` mocks `_probeMock` to return success, so the test passes but does not exercise the production scenario where the probe fails against Cloudflare.

**Fix:** Skip content probes after manual verification. The user confirmed the URL is reachable; content analysis is not possible through a headless browser for Cloudflare-protected sites. Return immediately after the "manually verified" pass.

---

## Finding 4 — Browser context and page are not disposed (resource leak)

**File:** `ManualVerificationWindow.xaml.cs`, lines 53–54

```csharp
var context = await _handle.RawBrowser.NewContextAsync();
var page = await context.NewPageAsync();
```

Only `_handle` (the `CloakBrowserHandle`) is stored and disposed in `DisposeBrowserAsync`. The `IBrowserContext` and `IPage` are local variables that go out of scope without explicit disposal.

The codebase pattern in `CloakBrowserSearchProvider` (WebTools.NET) explicitly disposes all three resource types in its `DisposeFallbackResourcesAsync`:

```csharp
await CloseContextQuietlyAsync(context);   // IBrowserContext.CloseAsync
await DisposeHandleQuietlyAsync(handle);   // CloakBrowserHandle.DisposeAsync
```

While Playwright typically cleans up child objects when the parent browser closes, this is not guaranteed in edge cases (browser crash, disposal exception). Explicit context disposal is more robust.

**Fix:** Store the `IBrowserContext` reference as a field and dispose it in `DisposeBrowserAsync` before disposing the handle.

---

## Finding 5 — `ShowDialog()` contradicts spec's "modeless dialog" requirement (spec deviation)

**File:** `ManualBrowserVerifier.cs`, line 36

The spec states: *"Show a modeless dialog (`ManualVerificationWindow`)"*. The implementation uses `dialog.ShowDialog()` which creates a **modal** dialog (blocks the owner window).

This is functionally correct for the use case — the validation pipeline needs to wait for the user's response before continuing. A modeless `Show()` would require separate lifetime management. However, the discrepancy between spec and implementation should be acknowledged.

**Severity:** Low — the modal behavior is the right design choice; the spec wording should be updated to match.

---

## Finding 6 — Duplicated challenge detection logic is consistent but fragile (DRY maintenance risk)

**Files:** `ManualVerificationWindow.xaml.cs` lines 15–26 and 150–171, vs `WebTools.NET/Browsing/PlaywrightContentFetcher.cs` lines 15–26 and `BrowserContentFetcherBase.cs` lines 170–191

The `ChallengeWaitScript` JavaScript constant and the `IsBotChallengePageAsync` method are duplicated from WebTools.NET. The spec explicitly acknowledges this as an intentional trade-off ("they are not accessible" — `private const` and `protected static`).

**Verification:** The duplication is currently character-for-character consistent (same markers, same logic, same case-insensitive comparisons). The ScraperTool copy correctly omits the `CancellationToken` parameter from `IsBotChallengePageAsync` since the dialog does not need it.

**Severity:** Low — not a defect today, but a maintenance risk. If WebTools.NET adds or removes challenge markers, the ScraperTool copy will drift silently. A comment referencing the source of truth in WebTools.NET would mitigate this.

---

## Finding 7 — `OnDone` proceeds with misleading status when no browser is open (dead code path, misleading UX)

**File:** `ManualVerificationWindow.xaml.cs`, lines 72–121

If `_handle` is null (browser was never opened or launch failed and was disposed), the `if (_handle is not null)` block is skipped entirely. `Result` remains at its default `UserCancelled`, but the status text at line 106–108 reads:

```csharp
StatusText.Text = Result == EManualVerificationResult.Verified
    ? "Challenge cleared — URL verified."
    : "Challenge still present — falling back to BotProtected.";
```

The user sees "Challenge still present" even though no verification was attempted. The dialog then closes with `DialogResult = true`, suggesting a completed operation.

In practice the Done button starts disabled (`IsEnabled="False"` in XAML) and is only enabled after successful navigation (line 62), so this path is not reachable through normal interaction. However, the code should guard against it explicitly — either by checking `_handle` is null and returning early with a correct message, or by not setting `DialogResult = true` when no verification occurred.

---

## What is correct

- The `EManualVerificationResult` enum and `IManualBrowserVerifier` interface match the spec exactly.
- `UrlFieldChecker` integration is correct for the `StillProtected`, `UserCancelled`, and exception branches: all fall back to the `BotProtected` informational pass unchanged.
- The `BotProtected` fallback behavior is preserved when manual verification is not offered, not wanted, or fails.
- DI registration as singleton is correct (the verifier holds no per-request state).
- Tests cover all five branches of `HandleAuthRequiredAsync` with `ProtectionType`: Verified, StillProtected, UserCancelled, exception, and no-ProtectionType. The verifier is correctly NOT called when `ProtectionType` is null.
- `Application.Current is null` guard in `ManualBrowserVerifier` is correct for non-WPF hosts.
- `Interlocked.Exchange` for browser handle disposal prevents double-dispose.
- `ValidatorGraphBuilder` correctly provides a default `UserCancelled` mock for existing tests that do not exercise the manual verification path.
- `OnCancel` correctly sets `Result = UserCancelled` and the Cancel button has `IsCancel="True"` for keyboard Escape support.
- `OnClosed` override ensures `DisposeBrowserAsync` is called on any dialog close path.
