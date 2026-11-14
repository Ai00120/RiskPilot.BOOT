# Lifecycle integration audit — dispatcher + menu + Diagnostic Center window

Audit of the three merged lifecycle PRs in `Ai00120/RiskPilot.BOOT`:

1. **Dispatcher lifecycle** — `Core/Run/SwitchableUiDispatcher.cs`, `Host/WpfUiDispatcher.cs`,
   `Host/DiagnosticAddOn.Ui.cs` (commit range `9aed71a`, `71ce7cb`, `c741bd4`, `22f8378`, `30baf31`).
2. **Menu integration lifecycle** — `Core/Run/MenuOwnershipRegistry.cs`, `Host/MenuIntegration.cs`
   (`143a44e`, `abe7ea3`, `3b9bdcd`).
3. **Diagnostic Center window manager lifecycle** — `Core/Run/DiagnosticWindowLifecycle.cs`,
   `Core/Run/IDiagnosticWindow.cs`, `Host/DiagnosticCenterWindowManager.cs`
   (PR #23, `2d956ec`, `ab8dedf`).

The audit reviewed the **combined** behavior of the three changes, ran the offline build and the
complete test suite, added the missing combined-integration tests, and recorded the residual risks
and the manual NinjaTrader 8 acceptance tests that remain.

> **No real-host verification is claimed.** The WPF adapters are compiled only for the
> `UseStubs=false` (real-host) build; everything below is offline evidence plus the code review of
> the host-only files. Only running inside NinjaTrader 8 can prove real-host behavior.

---

## 1. Scope and method

- Read the final merged sources of all three lifecycle areas and their host hooks
  (`DiagnosticAddOn.cs`, `DiagnosticAddOn.Ui.cs`) plus the supporting contracts
  (`IUiDispatcher`, `IDiagnosticWindow`, `DiagnosticRunCoordinator`, `DiagnosticViewState`).
- Diffed each lifecycle PR against its merge base to confirm what each change touched and to detect
  cross-PR contradictions.
- Built the offline stub project and ran the complete xUnit suite and both static verification
  scripts (baseline and after adding tests).
- Added `tests/RiskPilot.Diagnostics.Nt8.Tests/LifecycleIntegrationTests.cs`, which drives the real
  host-free primitives through the exact combined host sequence the AddOn performs. The thin WPF
  adapters are mirrored as test scaffolding (they cannot be compiled offline).

**Files not modified:** the three original implementation files
(`SwitchableUiDispatcher.cs`, `MenuOwnershipRegistry.cs`/`MenuIntegration.cs`,
`DiagnosticWindowLifecycle.cs`/`DiagnosticCenterWindowManager.cs`) are unchanged. No concrete
integration defect was found, so no implementation PR was opened. The only code change is the added
integration test file plus its registration in the test `.csproj`.

`Properties/AssemblyInfo.cs` and the source-packaging mechanism (`tools/nt8-source-package/`) were
not touched.

---

## 2. Build and test results

Environment: .NET SDK 8.0.428 (installed under `$HOME/.dotnet`), `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`,
Linux. Commands mirror the repo's documented offline CI.

| Step | Command | Result |
|---|---|---|
| Stub build (Debug) | `dotnet build src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=true` | **Build succeeded, 0 warnings, 0 errors** |
| Stub build (Release, as CI) | `dotnet build ... --configuration Release -p:UseStubs=true` | **Build succeeded, 0 warnings, 0 errors** |
| Full xUnit suite | `dotnet test tests/RiskPilot.Diagnostics.Nt8.Tests/...csproj` | **721 passed, 0 failed, 0 skipped** (711 baseline + 10 new) |
| Static safety scan | `python3 verification/addon-smoke/test_diagnostic_safety.py` | **PASS** (129 production files, 34 forbidden patterns) |
| NT8 compilation regression | `python3 verification/addon-smoke/test_nt8_compilation_regression.py` | **PASS** |
| Real-host build | `dotnet build ... -p:UseStubs=false` | Fails as expected offline (`CS0246 NinjaTrader...`); requires NT8 assemblies on Windows |

Baseline before the audit: 711 tests passed. The audit added 10 integration tests, all passing.

---

## 3. Verified scenarios

Each objective from the review brief is listed with the evidence that supports it. "Offline" means
the host-free primitive or a stub-backed path; "host-only (review)" means the code was reviewed but
can only be exercised inside NT8.

### Objective 1 — Dispatcher ownership matches the real NT window lifecycle

- **Verified (offline).** `SwitchableUiDispatcher.Attach(owner, dispatcher)` records the owning
  window and refuses a different owner while a **live** target is held
  (`UnrelatedWindowCreation_DoesNotTakeOverTheActiveDispatcher`,
  `ControlCenterOwnsDispatcher_MenuAndWindow_AcrossTheFullHostSequence`). A shut-down target is
  replaced regardless of owner so a re-created Control Center can take over
  (`Attach_ReplacesAShutDownTarget_OwnedByAnotherWindow`).
- **Verified (offline).** With no target, `Post` runs inline and `HasShutdownStarted` stays false, so
  a run started before any window exists is not lost
  (`RunStartedBeforeAnyWindow_IsNotLost_AndReachesTheUiOnceAttached`).
- **Verified (host-only, review).** `DiagnosticAddOn.Ui.cs` gates `OnWindowCreated` on
  `window is NinjaTrader.Gui.ControlCenter` and passes the window as the dispatcher owner. This cast
  is the documented NinjaScript pattern; it is not compilable offline.

### Objective 2 — Closing an unrelated window cannot detach the active dispatcher

- **Verified (offline).** `Detach(object owner)` clears the target only for the current owner;
  `HostOnWindowDestroyed(chart)` is a no-op and notifications keep routing through the Control Center
  (`UnrelatedWindowDestruction_DoesNotDetachTheActiveDispatcher`,
  `HostHookSequence_CreateChart_DestroyChart_DestroyControlCenter`,
  `ControlCenterOwnsDispatcher_MenuAndWindow_AcrossTheFullHostSequence`).

### Objective 3 — Menu registration/removal is consistent with the owning window

- **Verified (offline).** The registry accepts only `MenuSurfaceKind.ControlCenter`, is idempotent per
  owner (`AlreadyAttached`), refuses a second owner of the same surface (`SurfaceAlreadyOwned`),
  refuses a dead owner (`OwnerNotAlive`) and a failed add (`AddFailed`), and removes the item,
  detaches its click handler, and drops the owner/surface/callback references on removal
  (`MenuIntegrationLifecycleTests`, `MenuOwnerClassifier` cases).
- **Verified (offline).** Repeated `OnWindowCreated` for the same Control Center adds exactly one
  item (`RepeatedControlCenterCreate_DuringOneLifetime_AddsExactlyOneMenuItem`).
- **Verified (host-only, review).** `MenuIntegration.Attach` serialises the registry entry and the
  window-`Closed` subscription under one `Gate`, and the registry's removal callback
  (`UnsubscribeCloseLocked`) runs while that gate is held, so a concurrent `Reset` cannot leave a
  dangling subscription or an unsubscribed entry. `MenuIntegration.MenuItemId` equals
  `DiagnosticMenu.ItemId`.

### Objective 4 — Closing the Diagnostic Center leaves no stale static reference

- **Verified (offline).** The `DiagnosticCenterWindow` unsubscribes its `Changed` handler on `Closed`;
  `DiagnosticWindowController.OnWindowClosed` clears the tracked reference only when it still points
  at the window that closed. Closing the window does not touch the dispatcher or the menu
  (`ClosingTheDiagnosticCenter_LeavesNoStaleReference_AndDoesNotTouchTheDispatcher`,
  `WindowClosed_ClearsTheTrackedReference`).
- **Verified (offline).** An asynchronous close of an older window cannot clear a newer one
  (`AsyncClose_DoesNotClearANewerWindow`).

### Objective 5 — Reopening the Diagnostic Center works correctly

- **Verified (offline).** `CloseIfOpen` clears the tracked reference **before** queuing the close, so
  a reopen is never blocked; a fresh window is created and the old one is never reused
  (`ReopeningTheDiagnosticCenter_CreatesExactlyOneFreshWindow`, `ReopeningAfterClosing_CreatesAFreshWindow`,
  `ReopeningAfterWindowClosed_CreatesAFreshWindow`).

### Objective 6 — AddOn termination cleans up all owned resources

- **Verified (offline).** `ShutdownUiLayer` order is: unconditional `_uiDispatcher.Detach()` → drop
  `_windowManager` → `DiagnosticCenterWindowManager.CloseIfOpen()` → `MenuIntegration.Reset()`.
  Modeled by `TerminateThenReinitialise_LeavesNoStaleState_AndNoDuplicates` (dispatcher cleared,
  window closed once, menu empty, registry empty).
- **Verified (host-only, review).** `SafeDispose` detaches market-data capture and disposes the run
  coordinator **before** `ShutdownUiLayer`, so no notification can race the teardown.

### Objective 7 — Reinitialisation creates no duplicate menus/handlers/windows

- **Verified (offline).** After terminate + reinitialise, the second lifetime has exactly one menu
  item, one registry entry and one window (`TerminateThenReinitialise_LeavesNoStaleState_AndNoDuplicates`).
  `WindowLifecycleTests.AddOnTermination_ThenReinitialization_RecoversTheLifecycle` covers the
  process-wide controller.
- **Verified (offline).** A late destroy callback for the old Control Center or for a chart after
  reinitialisation is a no-op (`UnrelatedWindowDestroyedAfterTermination_CannotClearAFreshDispatcher`).

### Objective 8 — Dispatcher shutdown causes no deadlock or invalid cross-thread operation

- **Verified (offline).** No blocking `Dispatcher.Invoke` exists anywhere; every post uses
  `InvokeAsync` (grep confirms only `InvokeAsync`, never `Invoke(`). `WpfUiDispatcher.Post` checks
  **both** shutdown flags and contains an `InvalidOperationException` from `InvokeAsync`, reporting
  the post as not queued (`WpfUiDispatcher_*` cases).
- **Verified (offline).** `SwitchableUiDispatcher` holds no lock while invoking the inner dispatcher
  (it copies the target under the lock, then posts outside it); `MenuOwnershipRegistry` runs surface
  mutations under its lock but the host removal callback outside it; `DiagnosticWindowController`
  holds no lock while invoking a window/dispatcher operation. Concurrent attach/detach/post never
  deadlocks (`ConcurrentAttachDetachPost_DoesNotThrow_AndKeepsInvariants`,
  `ThreadedAttachDetachWhilePosting_NeverDeadlocks_AndKeepsOneOwner`).
- **Verified (offline).** A shutting-down Control Center dispatcher makes the coordinator drop
  notifications instead of posting to a dead dispatcher, and the run still completes
  (`ShuttingDownControlCenterDispatcher_DoesNotFaultARun`).

### Objective 9 — The three implementations do not contradict each other's ownership assumptions

- **Verified (offline).** All three key their ownership on the **same** Control Center window object:
  the dispatcher owner, the menu owner and the window-manager lifecycle are driven by the same host
  sequence. `LifecycleIntegrationTests` asserts the combined sequence end-to-end.
- **Verified (offline).** `MenuIntegration.MenuItemId` and `DiagnosticMenu.ItemId` are the same
  constant; the window manager is real-host-only and holds a process-wide controller (static), the
  dispatcher is per-AddOn-instance, and the menu registry is static with an explicit `Reset`.

### Objective 10 — No unrelated functionality or trading-safety behavior changed

- **Verified (offline).** The static safety scan passes over 129 production files with 34 forbidden
  patterns (order/account mutation, live-trading enable, reflection/unsafe/interop, identity-literal
  confinement, trading-disabled declaration).
- **Verified (review).** The three lifecycle PRs touched only: the two host-free lifecycle primitives,
  the WPF adapters, the stub `Dispatcher` fault-injection switches, the `DiagnosticAddOn.Ui.cs` hooks,
  the two `.csproj` files and `AGENTS.md`. No probe, evidence, identity, security-scan or
  trading-safety file was changed. The added integration tests use only production code.

---

## 4. Findings (no integration defect; observations only)

1. **Post-termination stale-destroy is safe but relies on same-instance semantics.** If the Control
   Center is destroyed *after* `SafeDispose` ran, `OnWindowDestroyed` calls `_uiDispatcher.Detach(window)`
   on the **same** `SwitchableUiDispatcher` instance whose owner was already cleared by
   `ShutdownUiLayer`; `Detach` returns false (owner is null), so the guard body does not run. This is
   safe (no stale manager/menu can be cleared incorrectly), but it means the `_windowManager`/
   `MenuIntegration.Reset()` cleanup in `OnWindowDestroyed` only runs when the Control Center is
   destroyed **before** termination — which is the normal ordering. In a hypothetical reload where a
   **new** AddOn instance is created while the old Control Center window still exists, the new
   instance's `OnWindowCreated` may not fire for the already-open window until a new window appears.
   This is a real-host timing question, not a demonstrable offline defect; it is on the manual
   acceptance list (test M2).
2. **`MenuIntegration.Detach(Window)` is production-dead code.** The AddOn drives menu cleanup only
   through `MenuIntegration.Reset()`. The method is correct and harmless; it is left in place. Do not
   wire it without re-auditing the gate/registry interaction.
3. **Deferred UI render is a display-timing property, not a lost result.** A run started before the
   window exists applies notifications inline; once a real dispatcher attaches, later notifications
   are queued and only rendered when the UI thread drains them. The live probe and the exported
   package both read the thread-safe `DiagnosticViewState`, so no result is lost — only its on-screen
   appearance is deferred.

---

## 5. Remaining risks

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| R1 | The `window is NinjaTrader.Gui.ControlCenter` cast is unverified against the real NT8 type hierarchy (a derived Control Center type would still match by inheritance; a differently-named window would not). | Medium | Manual test M1 in NT8. |
| R2 | Per-window `OnWindowCreated`/`OnWindowDestroyed` threading and the exact `Dispatcher.HasShutdownStarted`/`HasShutdownFinished` timing at NT8 shutdown are unobservable offline. | Medium | Manual tests M3, M4. |
| R3 | The Control-Center-destroyed-after-termination timing in Finding 1. | Low | Manual test M2. |
| R4 | A future edit could wire `MenuIntegration.Detach` or change the ownership key and break the combined invariant. | Low | `LifecycleIntegrationTests` + existing suites; keep them in the `.csproj`. |
| R5 | The real WPF `Menu` visual-tree search (`FindMainMenu`) and the real `MenuItem` click wiring are not exercised offline. | Low | Manual test M1. |

---

## 6. Required manual NinjaTrader 8 acceptance tests

Run inside NT8 8.1.9.0 (target) with the packaged sources
(`tools/nt8-source-package/package_addon_sources.py`), the account/connection identity configured,
and the diagnostic run disabled (read-only). Record the observed result of each step.

- **M1 — Control Center menu + window open/close.** Start NT8 with the AddOn compiled. Confirm the
  "RiskPilot NT8 Diagnostic Center" item appears **once** on the Control Center main menu, opening it
  shows a single window, and closing and reopening it shows a single fresh window (never two).
- **M2 — Control Center destroyed after AddOn terminate.** Terminate the AddOn, then close the Control
  Center, then re-open it. Confirm no duplicate menu item and no exception in the NinjaTrader Log.
  (Covers Finding 1 / R3.)
- **M3 — Unrelated window close does not detach.** With the Diagnostic Center open, start a Quick run
  and, while it runs, open and close an unrelated chart/DOM/SuperDOM. Confirm progress/status keep
  updating (the dispatcher was not detached) and no cross-thread exception is logged.
- **M4 — Shutdown during a run.** Start a Full run and shut NT8 down mid-run. Confirm no deadlock, no
  cross-thread `InvalidOperationException` in the log, and that the process exits cleanly.
- **M5 — Reload reinitialisation.** Terminate and recompile/reload the AddOn (NinjaScript reload)
  without restarting NT8. Confirm exactly one menu item and one window after reload, and that a run
  still marshals to the UI.
- **M6 — Identity/read-only unchanged.** Confirm the window still shows "Trading: DISABLED", the
  run produces the evidence package outside the Git tree, and no order/account/connection was
  mutated (no orders submitted, no account state changed).

---

## 7. Changes made by this audit

- **Added** `tests/RiskPilot.Diagnostics.Nt8.Tests/LifecycleIntegrationTests.cs` (10 tests) and
  registered it in `tests/RiskPilot.Diagnostics.Nt8.Tests/RiskPilot.Diagnostics.Nt8.Tests.csproj`.
  It covers the combined dispatcher + menu + window sequence, pre-window runs, termination/
  reinitialisation, stale-destroy after termination, dispatcher-shutdown-finished handling and the
  shared menu item id.
- **Updated** `AGENTS.md` with the combined-lifecycle audit notes.
- **Added** this report (`docs/LIFECYCLE_INTEGRATION_AUDIT.md`).
- No production source file was modified; no separate implementation PR was required.
