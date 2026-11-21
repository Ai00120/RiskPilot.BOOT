# AGENTS.md — RiskPilot.BOOT

Repository-specific knowledge for agents working in this repo. Keep it current.

## What this repo is

Independent development repository for the **RiskPilot NT8 Diagnostic Center** — a
standalone, **read-only** NinjaTrader 8 AddOn (`RiskPilot.Diagnostics.Nt8`). It has **no
execution dependency** on the reference repo `Ai00120/RiskPilot.BOT`.

## Hard safety rules (never violate)

- Read-only. Never call an order-submission/modification/cancellation API.
- Never mutate an account, connection, position or RiskPilot setting.
- No reflection, no `unsafe`, no native interop, no undocumented APIs.
- Trading stays disabled. There is no code path that enables it.
- Missing/unobservable data must never be reported as `PASS` (fail-closed).
- No account identifiers or secrets in any report or log; use `SecretRedactor` / `SyntheticRef`.
- Report artifacts are written **outside** the Git working tree.

These are enforced by `verification/addon-smoke/test_diagnostic_safety.py` — run it after
any source change.

## Layout

- `src/RiskPilot.Diagnostics.Nt8/Core/` — pure domain, **no NinjaTrader types**.
- `src/RiskPilot.Diagnostics.Nt8/Core/Run/` — modes, run coordinator, view state (host-free).
- `src/RiskPilot.Diagnostics.Nt8/Core/Evidence/` — 18-artifact evidence package builder.
- `src/RiskPilot.Diagnostics.Nt8/Host/` — the **only** layer that touches NinjaTrader types.
- `src/RiskPilot.Diagnostics.Nt8/Stubs/` — offline compile-time stubs (`#if USE_STUBS` only).
- `tests/RiskPilot.Diagnostics.Nt8.Tests/` — xUnit tests.
- `verification/addon-smoke/` — static safety scan (no NT8 needed).
- `docs/` — Phase 1–4 records.

## Build & test (offline CI)

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$PATH:$HOME/.dotnet"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1   # only if the runner lacks ICU

dotnet build src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=true
dotnet test  tests/RiskPilot.Diagnostics.Nt8.Tests/RiskPilot.Diagnostics.Nt8.Tests.csproj
python3 verification/addon-smoke/test_diagnostic_safety.py
```

- `UseStubs=true` (default) → `net8.0` + stubs, for offline validation only.
- `UseStubs=false` → `net48`, references real NT8 assemblies (Windows only).
- **A green stub build is NOT proof of real host compatibility.** Never claim it is.

## Conventions

- Production targets C# 8.0 / .NET Framework 4.8; keep `LangVersion=8.0` compatible.
- `EnableDefaultCompileItems=false`: every source file must be listed in the `.csproj`
  `<Compile Include=...>` items, otherwise it is silently not compiled.
- Tests use real production code; the only doubles are a deterministic clock and an
  in-memory `IDiagnosticDataProvider` that replaces the NinjaTrader host boundary.
- New forbidden-API checks go in the static safety scan.

## Known facts about the real NT8 surface (learned here)

- No `Provider.Rithmic` enum member is guaranteed — resolve provider identity by
  `ConnectOptions.Provider.ToString()` string comparison.
- `ConnectionStatus` has no `Unknown`-style member mismatch; map by closed switch → fail closed.
- Connection exposes separate `PriceStatus` and `Status` (order channel).
- `Instrument.MasterInstrument.*` must be read only after `DataLoaded`/`Realtime`.
- No public API exposes the exact NT8 host version or the provider build — mark unobservable.
- `Dispatcher` exposes both `HasShutdownStarted` and `HasShutdownFinished`; check both (the
  stub file declares the latter too).

## Diagnostic Center window lifecycle

- The single-window reference discipline lives in the host-free
  `Core/Run/DiagnosticWindowLifecycle.cs` (`WindowLifecycle` + `DiagnosticWindowController`), not in
  the WPF `Host/DiagnosticCenterWindowManager.cs`. `IDiagnosticWindow` (`Core/Run/IDiagnosticWindow.cs`)
  is the minimal view the policy drives; the manager adapts a real `DiagnosticCenterWindow` to it.
  This is what makes the lifecycle unit-testable offline (`WindowLifecycleTests.cs`).
- `WindowLifecycle.CanInvoke` checks **both** `HasShutdownStarted` and `HasShutdownFinished`;
  `IsUsable` additionally requires the window to be loaded and not closed. A closed, unloaded or
  shutting-down window is never reused — it is dropped and replaced.
- `DiagnosticWindowController.CloseIfOpen` clears the tracked reference **before** queuing the
  close, so it is idempotent and a reopen is never blocked by a half-closed window. `OnWindowClosed`
  only clears the reference when it still points at the window that closed, so an asynchronous close
  of an older window can never clear a newer one. No lock is held while invoking a window/dispatcher
  operation, so a re-entrant callback cannot deadlock.
- `DiagnosticCenterWindowManager.Controller` is a process-wide static so the "only one window"
  guarantee survives an AddOn terminate/reload within the same NT8 process; `ShutdownUiLayer` clears
  it on termination and a re-initialised AddOn reuses the now-empty controller. `Show` refuses a
  dispatcher that has started **or** finished shutting down, and returns true only meaning the request
  was queued (the original contract).
- The manager is real-host-only (`UseStubs != true`); a green stub build and `WindowLifecycleTests`
  are **not** real-host proof. When changing the manager, type-check it separately (the offline stub
  build never compiles it).

## Phase 4 notes (UI + evidence)

- The WPF UI files (`DiagnosticCenterWindow`, `MenuIntegration`, `DiagnosticCenterWindowManager`,
  `DiagnosticAddOn.Ui.cs`) are in the `UseStubs != true` ItemGroup — they are **not compiled** in
  the offline stub build. All UI-adjacent logic that must be testable lives in `Core/Run`.
- `DiagnosticRunCoordinator` owns the single-run guarantee; `Start` returns false when a run is
  active. It marshals notifications through `IUiDispatcher` (async post only).
- The AddOn wires `ProgressChanged`/`RunCompleted` to `DiagnosticViewState` in
  `SafeInitialize` (`WireViewState`); the window renders from that state. `SwitchableUiDispatcher`
  runs notifications inline until a window attaches (then `InvokeAsync`), so a run before the
  window opens is still reflected.
- Cancel is polled by the engine between modules via `Func<bool> shouldStop`; a cancelled run
  never writes an evidence package.
- `EvidencePackageBuilder.Build` is pure (no host/FS/clock). `VerifyIntegrity` checks all 18
  artifacts + SHA-256 before `WriteEvidencePackage` writes anything.
- In the manifest, `MaskedRef` is **always** a synthetic `account#<hash>` token, never a raw ref.
- `EvidencePackage.IsSecretFree` skips `manifest.json` (dates/hashes trip the digit-run rule);
  use `ManifestIsIdentitySafe` for the manifest.
- M9's `PostTradeAudit` observation is built from BOTH the ingest collector AND the host's
  already-read `Account.Orders` snapshot (`Snapshot.OrderLifecycle`), deduped by order ref, so
  the audit has a real surface even though the tool subscribes to no host event feed.
- The audit observation must reach BOTH paths: the live probe (via the `EnrichWithPostTradeAudit`
  enricher on `EnrichedDataProvider`) and the exported package (`BuildEvidenceDataset`). Setting
  it only in `BuildEvidenceDataset` leaves the live M9 probe UNOBSERVABLE.
- `PostTradeAuditObservation.ExecutionsInScope` must be derived, never hardcoded true: the host
  reads only `Account.Orders` (`AccountRecord.ExecutionsInScope=false`), so claiming execution
  coverage would let `M9.CORRELATION_TIMING` PASS without an observed execution (fail-closed).
- Never hardcode an observation in an artifact: `market-data-summary.sourceTimeBasis` is derived
  from the observed records (weakest basis) and is `UNOBSERVABLE` with no records.

## Phase 5 notes (final audit)

- `SecretRedactor` must not blanket-mask every 6+ digit run: a run that is the value of a
  structured `key=value` / `"key":value` field is legitimate evidence (counters like
  `ALL_CANDIDATES_INVALID=311179`) and is preserved; a free-text run and any run whose key looks
  like an account field are masked. See `MaskAccountDigitRuns` and `SecretRedactionTests`.
- A zero gap/drop count only means "clean" once the relevant stream was **observed**. Both
  `M5.DATA_INTEGRITY` and `M6.SEQUENCE_CONTINUITY` must stay `INCONCLUSIVE` with no observed
  stream, while a positive count fails regardless (it is itself evidence). The same rule applies to
  `M9.MISSING_OR_DUPLICATE` (needs `AuditSurfaceObservable`), `M8.EVIDENCE_INTEGRITY` (needs
  `EnqueuedCount > 0`) and `M2.ACCOUNT_CONNECTION_ASSOCIATION` (needs `withConnection > 0`).
- `WriteEvidencePackage` enforces four invariants before writing: `VerifyIntegrity`,
  `HasCompleteArtifactSet`, `IsSecretFree`, `ManifestIsIdentitySafe`. A package that fails any is
  never written.
- Every package carries `evidenceProvenance` (`RUNTIME`/`SAMPLE`); a `SAMPLE` package also shows a
  banner in `summary.html`. Never present a `SAMPLE` package as a runtime result.
- All `DateTime`/numeric `ToString` on evidence must use `CultureInfo.InvariantCulture`.
- The `UseStubs=false` configuration references the two real NT8 assemblies (`NinjaTrader.Core`
  and `NinjaTrader.Gui`) plus WPF (`PresentationCore`, `PresentationFramework`, `System.Xaml`,
  `WindowsBase`). The real-host build is not verifiable offline; never claim it is.

## Exact account identity notes (P16 / D-095 / D-096 / D-097)

- M2 verifies the **exact** permanent RiskPilot.BOT identity under D-097: account `SimRP.BOT`,
  connection `Rithmic`, provider `Rithmic` (the generic slot `Provider14` resolved by BOOT's own
  compiled mapping), mode `Simulation` **or** `Live` for that one SIM account, currency `USD`.
- The only place the raw account name literal lives is
  `Core/Identity/AccountIdentityVerifier.cs` (`ExpectedAccountIdentity`). The static safety scan
  fails if `SimRP.BOT` appears in any other production source file, so an accidental identity
  leak into a diagnostic/log/report is caught. Tests and docs are not scanned. Under D-097 the
  connection name equals the public provider name (`Rithmic`), which legitimate provider evidence
  reports, so the connection name is not a confinement literal; `SecurityScanner` confines it only
  when it differs from the provider name.
- The Host adapter (`Nt8HostDataProvider.BuildAccountIdentityObservation()`) does the exact
  ordinal name comparison and reduces everything to booleans/counts + `SyntheticRef`; the raw
  names never enter `AccountIdentityObservation`, a finding, or an artifact.
- Fail-closed: no account surface / stub value / zero accounts / ambiguous multi-match /
  any unobserved required field ⇒ `INCONCLUSIVE`; a similar or differently-cased name, wrong
  provider/connection/mode/currency ⇒ `FAIL`. Only a fully observed, fully matching identity is
  `PASS`. A mismatch outranks an unobservable field (proven wrong beats unproven).
- `Account.Denomination` is reduced by the single USD-only helper
  `Nt8HostDataProvider.CurrencyForUsdIdentity()`: `Currency.UsDollar` → `USD`; any other
  *observed* NT8 member stays its real member name (a definite `FAIL`, never converted into USD);
  an unreadable value is `INCONCLUSIVE`. There is no generic USD/EUR/JPY/GBP table in the
  identity path. The real NT8 `Currency` enum has **no `Yen`** member (JPY is `JapaneseYen`).
- `Properties/AssemblyInfo.cs` is required by the standalone DLL build
  (`GenerateAssemblyInfo=false`) and must NEVER be staged into
  `Documents\NinjaTrader 8\bin\Custom\AddOns\`: NT8 compiles every `.cs` there into one
  `NinjaTrader.Custom.dll` with its own `bin\Custom\AssemblyInfo.cs` (CS0579). Use
  `tools/nt8-source-package/package_addon_sources.py`; it fails closed on conflicting files.
- `accounts.json` carries an `identityVerification` block (status, reason codes, masked ref);
  it never carries the raw identity. `EvidencePackageBuilder` re-evaluates the verifier from
  `dataset.Snapshot.AccountIdentity`.

## Exact account→connection→provider association (P2)

- `M2.ACCOUNT_CONNECTION_ASSOCIATION` **proves** the target account's own binding: the host reads
  the matched account's `Account.Connection` (never the first of `Connection.Connections`) and
  reduces it to booleans/counts + `SyntheticRef` in `TargetAccountConnectionObservation`; the
  decision is `Core/Identity/TargetAccountConnectionVerifier` (fail-closed).
- Ambiguity is detected, not accepted: `ConnectionCount`/`MatchingConnectionCount` (more than one
  connection matching the required identity ⇒ `INCONCLUSIVE`,
  `TARGET_ACCOUNT_CONNECTION_AMBIGUOUS`) and `MatchingAccountCount > 1` ⇒ `INCONCLUSIVE`.
  `ConnectionObserved=false` (account reports no connection) ⇒ `INCONCLUSIVE`. An observed field
  that contradicts the requirement ⇒ `FAIL` (outranks an unobservable field).
- Provider attribution: `Nt8HostDataProvider.GetProvider()`/`BuildProviderEvidence()` attribute the
  provider to the target account's own connection, else to a **single** unambiguous connection;
  `ProviderEvidence.ResolutionBasis` records which. Never the first of several connections. If it
  cannot be attributed, `M2.PROVIDER_IDENTITY` stays `INCONCLUSIVE` (`PROVIDER_NOT_ATTRIBUTED`).
- `M2.ACCOUNT_PROVIDER_AGREEMENT` is the generic per-account provider-agreement check (kept for
  breadth); it does not prove the target identity.
- The check is emitted even with no connections (as `INCONCLUSIVE`), so it is never omitted.
- No raw account/connection name ever reaches a finding, log or artifact; `accounts.json` carries a
  `connectionAssociation` block with only status/reason codes/counts + a synthetic `accountRef`.
  The generic `M2.ACCOUNT_PROVIDER_AGREEMENT` evidence and the `connections.json` /
  `accounts.json` association rows reduce the connection name to a `SyntheticRef`
  `connection#<hash>` token (`connectionRef`), so the name is never written.
- The host adapter is exercised over the offline stub surface by
  `tests/.../HostTargetAccountConnectionTests.cs`; a green stub run is **not** real-host proof.

## Canonical ES/CME instrument identity (M4)

- **Deterministic target selection (P18 — never auto-discover).** `M4.INSTRUMENT_IDENTITY` proves the
  **explicitly selected** target instrument, not one auto-discovered from the loaded surface. The
  single source of truth is the host-free `Core/Instruments/InstrumentSelector.cs`:
  `InstrumentSelector.Select(requestedId, surfaceObservable, loaded, isStubOrMock)` returns a
  `InstrumentSelectionResult` (`IsResolved`, `SelectedIndex`, `Status`, `CandidateCount`,
  `ObservedCount`, `ReasonCode`). Rules: no id ⇒ `NotConfigured` (never "pick the first"); no surface
  ⇒ `SurfaceUnavailable`; a stub/mock value ⇒ `StubOrMock`; an **exact** full-name match ⇒ `Resolved`;
  else an **exact** root match ⇒ `Resolved` only when exactly one candidate matches, otherwise
  `MultipleCandidates` (ambiguous); otherwise `NotLoaded`. A candidate whose root was not readable is
  never matched by the root rule. The selector never picks a first/default instrument and never
  selects by a name **substring** — an `ES`-like but different symbol can never be chosen.
- The host adapter `Nt8HostDataProvider.SelectLoadedInstrument` / `ResolveLoadedInstrument` enumerates
  `Instrument.All` only to compare each candidate's own `FullName`/`MasterInstrument.Name`; the raw
  name is reduced to booleans/counts + a `SyntheticRef` `instrument#<hash>`. `ResolveAttestationInstrument`,
  `BuildInstrumentIdentityObservation` and `BuildEsProductSpecificationObservation` all go through the
  same selector, so **one** explicit identity is used by the metadata inspection **and** the
  market-data capture. `MarketDataCapture` takes the same `requestedInstrumentId` and refuses to
  subscribe with no selection (`Capture_NoSelection_DoesNotSubscribe`).
- `DiagnosticAddOn.RequestedInstrumentId` is the operator/host-set explicit target. It now defaults
  to the compiled D-097 target `ES 12-26` (`ExpectedInstrumentIdentity.TargetInstrumentId`), threaded
  into both `new Nt8HostDataProvider(...)` and `new MarketDataCapture(...)`. Setting it (or the
  Diagnostic Center picker) is read-only — it never mutates the host selection. If the operator
  clears the selection the tool never becomes ready (`HostReadinessState.AwaitingInstrument`) and M4
  stays `INCONCLUSIVE` (`TARGET_INSTRUMENT_NOT_CONFIGURED`). The compiled default only resolves when
  that exact contract is loaded; a missing contract stays `INCONCLUSIVE`, never a fabricated match.
- Fail-closed mapping (verifier `Core/Identity/InstrumentIdentityVerifier`): no selection ⇒
  `INCONCLUSIVE` (`TARGET_INSTRUMENT_NOT_CONFIGURED`); the selected contract is not loaded ⇒
  `INCONCLUSIVE` (`TARGET_INSTRUMENT_NOT_LOADED`, never a false root mismatch); the selection matches
  more than one loaded contract ⇒ `INCONCLUSIVE` (`TARGET_INSTRUMENT_AMBIGUOUS`); no readable root at
  all ⇒ `INCONCLUSIVE` (`INSTRUMENT_ROOT_UNOBSERVABLE`); a **resolved** contract whose root was read
  and disagrees ⇒ `FAIL` (`INSTRUMENT_ROOT_MISMATCH`); more than one matching root ⇒
  `INCONCLUSIVE` (`..._AMBIGUOUS`); a stub/mock value ⇒ `INCONCLUSIVE`. A mismatch outranks an
  unobservable field. The expiry is **not** pinned: only the canonical root/venue form
  (`ES_FUTURES_CME_*`) is required, so a manual contract roll keeps the identity valid.
- The venue is confirmed only by the D-096 configured-route metadata: selected route `Globex`,
  configured exchange set exactly `{Globex}` and the sole **connected** provider path `Rithmic`.
  CME is **never** inferred from the `ES` symbol text. An unconfirmed exchange ⇒
  `INCONCLUSIVE` (`EXCHANGE_METADATA_UNOBSERVABLE`), never PASS; an observed route that contradicts
  ⇒ `FAIL` (`EXCHANGE_MISMATCH`).
- `MasterInstrument.*` is only read when the provider's **live** readiness probe returns true. The
  probe is a `Func<bool>` evaluated on every read, not a value captured at construction: an AddOn is
  constructed at `State.Active`, before `MasterInstrument.*` is safe to read, so a captured flag would
  stay false for the whole session and leave M4 permanently `INCONCLUSIVE`. The AddOn supplies
  `() => HostIsLive` (true once the state reaches `Active`/`DataLoaded`/`Historical`/`Transition`/`Realtime`);
  a throwing probe fails closed to not-ready. Before readiness the identity is `INCONCLUSIVE`, not FAIL.
  A selected-but-not-ready contract reports `SelectionStatus=Resolved` with `RootMetadataObservable=false`.
- The D-078/D-096 attestation target is the same **explicitly selected** instrument: with no selection
  it is `HOST_UNAVAILABLE`; the selected contract that is not loaded, or one that matches several
  loaded contracts, is also `HOST_UNAVAILABLE` (fail closed). When it resolves, the host attests that
  real loaded object (never one built from the configured string).
- The check is emitted **even when the D-078/D-096 attestation observation is null**, so M4 always
  reports the instrument identity.
- `instruments.json` carries an `instrumentIdentity` block (status, `selectionStatus`,
  `selectionReasonCode`, counts, reason codes, masked ref); `EvidencePackageBuilder` re-evaluates the
  verifier from `dataset.Snapshot.InstrumentIdentity`.
- Tests: `InstrumentIdentityTests` (verifier + probe), `HostInstrumentIdentityTests` (host adapter over
  the offline stub surface) and `InstrumentSelectionTests` (the selector rule + the host resolution +
  the AddOn readiness + the shared metadata/capture identity, incl. no-selection, unloaded, ambiguous,
  mismatch and not-ready cases); a green stub run is **not** real-host proof.

## M3 active-order counting

- M3 Portfolio access is **target-only**: `Positions`/`Orders` are read from the single account
  M2 verified (`Nt8HostDataProvider.TryGetVerifiedTargetAccount` → exactly one exact name match
  AND a passing `AccountIdentityVerifier` verdict), never enumerated across `Account.All`. With
  no verified target (missing/ambiguous/duplicated/unverified) no Portfolio collection is read
  at all; `OrderLifecycleObservation.TargetAccountVerified` records that, and `M3.ACCOUNT_SURFACE`
  / `M3.ACTIVE_ORDERS` report `TARGET_ACCOUNT_NOT_VERIFIED` (INCONCLUSIVE, never a misleading
  zero). `GetAccounts()` returns the target's single `AccountRecord` (or none). `BuildAccountAssociations`,
  `BuildAccountIdentityObservation` and `BuildTargetAccountConnectionObservation` still enumerate
  `Account.All` metadata only (no Portfolio collections) for the M2 verification.
- `M3.ACTIVE_ORDERS` reports the **exact** number of live orders, derived from each account's real
  `Account.Orders` collection. `Core/Engine/OrderStateClassifier.cs` is the host-free single source
  of truth: `Initialized`/`Submitted`/`Accepted`/`Working`/`PartFilled`/`ChangeSubmitted` are
  **active**; `Filled` = **completed**; `Cancelled` = **cancelled**; `Rejected` = **rejected**;
  anything else is **unclassified**. A terminal order is **never** counted as active.
- The previous bug: `Nt8HostDataProvider.GetAccounts()` hardcoded `WorkingOrderCount = 0` and
  `BuildOrderLifecycleObservation()` never distinguished a live from a finished order, so
  `accounts.json` always showed `workingOrderCount: 0`. Both now use the classifier, so the host
  snapshot's active count and the `accounts.json` `workingOrderCount` agree.
- Two paths, one source: the host adapter's point-in-time counts (`ActiveOrderCount`, terminal
  counts, `OrderReadFailureCount`, `UnclassifiedOrderCount`) are folded into the snapshot by
  `SnapshotAssembler` together with the `M3OrderLifecycleConsumer`'s counters from the event drain
  stream (`RuntimeCounterKeys.OrderActive` / `OrderCompleted` / `OrderCancelled` / `OrderRejected`
  / `OrderUnclassified`). Setting it only on one path would leave the other blind.
- Fail-closed: no readable `Account.Orders` collection ⇒ `INCONCLUSIVE`
  (`ORDER_COLLECTION_UNOBSERVABLE`); a positive read-failure count or an unclassified state ⇒
  `INCONCLUSIVE` (`ACTIVE_ORDER_COUNT_UNRELIABLE`). Only a fully observed, fully classified
  collection yields a trustworthy count — and a readable-but-empty collection is a genuine `PASS`
  with `ACTIVE=0`, not `INCONCLUSIVE`. A terminal count is reported as corroborating evidence but
  never folded into the active total.
- Read-only: the adapter only enumerates `Account.Orders`; it never submits, changes or cancels an
  order. Tests: `M3ActiveOrderTests` (classifier, probe over the in-memory provider, consumer +
  assembler, and the host adapter over the offline stub surface — no orders / one / multiple /
  completed / cancelled / rejected / mixed / unreadable). A green stub run is **not** real-host
  proof.

## M4 field-read status (P0 fix)

- `SafeString`/`SafeDouble` swallow getter exceptions and return `""`/`null`, which hid a real
  read failure. `BuildAttestationObservation` read every metadata field through them and only
  recorded a coarse `"MasterInstrument"` failure, so `M4.FIELD_METADATA` could PASS with an
  unread field (the false-PASS risk).
- Fix: the attestation metadata path now uses `Nt8HostDataProvider.TryReadMetadataString` /
  `TryReadMetadataDouble`, which record the **exact** field name in `ReadFailures` when the getter
  throws and leave a per-field `*Observable` flag false. One failed field never erases another
  field's read status. `SafeString`/`SafeDouble` are unchanged for the other (non-attestation)
  call sites.
- `InstrumentMetadataObservation` carries per-field observability (`FullNameObservable`,
  `ExpiryObservable`, `ExchangeObservable`, `RootNameObservable`, `InstrumentTypeObservable`,
  `CurrencyObservable`) plus `HasReadFailures`. `M4.FIELD_METADATA` decides: any recorded read
  failure ⇒ `FAIL` (`HOST_READ_FAILED`, outranks an unobserved field); else any required field not
  observed ⇒ `INCONCLUSIVE` (`HOST_METADATA_INCOMPLETE`); only a fully read, fully validated set ⇒
  `PASS` (`ALL_FIELDS_READ`).
- `M4.HOST_METADATA` now proves only that the host instrument object/metadata surface was
  reachable; it never claims every required field was read (that is `M4.FIELD_METADATA`'s job).
- `BuildInstrumentIdentityObservation` and `BuildEsProductSpecificationObservation` (also named in
  the P0 prompt) already fail closed: they read through `SafeString`/`SafeDouble` but set their own
  `*Observable` flags from the read result (`HasValue`/`IsNullOrEmpty`), so a throwing getter leaves
  the field unobservable and `InstrumentIdentityVerifier`/`EsProductSpecificationVerifier` stay
  INCONCLUSIVE. `M4FieldReadStatusTests` now proves this with a throwing getter on each path, so a
  future refactor cannot silently regress them to PASS.
- The exported `instruments.json` attestation block carries a `metadata` object with
  `fieldReadFailures` and the per-field `*Observable` flags, so the read outcome is preserved.
- Offline stub `MasterInstrument` gained `ThrowOnNameRead`/`ThrowOnInstrumentTypeRead`/
  `ThrowOnCurrencyRead` fault injection (mirrors `ThrowOnTickSizeRead`/`ThrowOnPointValueRead`) to
  exercise a throwing getter over the stub surface. Tests: `M4FieldReadStatusTests`. A green stub
  run is **not** real-host proof.

## Zero-vs-unknown at the data-reading and serialization boundaries (P0 fix)

- A value the host **read** and that is genuinely zero stays a number; a missing, null, unavailable
  or unreadable value stays **unknown**. Never default an unavailable value to `0`.
- `AccountRecord.PositionCount` / `AccountRecord.WorkingOrderCount` are now `int?` and
  `InstrumentRecord.TickSize` / `InstrumentRecord.PointValue` are now `double?`. `null` means
  unobserved/unreadable; `0` means the host genuinely reported zero (a readable, confirmed-empty
  collection for the counts).
- `Nt8HostDataProvider.ReadPositionCount`/`CountActiveOrders` return `null` (not `0`) when the
  Portfolio collection is missing/unreadable, when an order is null, when an order state cannot be
  classified, or when enumeration throws mid-read. `GetInstrument` wraps every field in
  `SafeString`/`SafeDouble` so an unreadable economics field stays `null` instead of a fabricated
  `0`/empty. A genuinely readable-but-empty collection still yields a real `0`.
- `EvidencePackageBuilder.WriteObservableCount`/`WriteObservableDouble` emit the number for an
  observed value and the string `"UNOBSERVABLE"` for an unknown one, so `accounts.json` /
  `instruments.json` preserve genuine-zero vs unknown vs confirmed-empty. `WriteCollectionCount`
  emits `"UNOBSERVABLE"` for a null/empty `Accounts`/`Instruments` collection (never `"count":0`);
  `BuildAccountsJson`/`BuildInstrumentsJson`/`BuildManifest`/`CountRecords`/`ProviderLabel`/
  `InstrumentLabel` are null-collection safe.
- The M4 probes keep their own `*Observable` flags on `InstrumentAttestationObservation` /
  `EsProductSpecificationObservation`; the `InstrumentRecord` nulls feed only the evidence export,
  never a diagnostic verdict. No downstream check treats an unknown as valid evidence.
- Tests: `ZeroDefaultTests` (model, JSON genuine-zero/unreadable, null/empty collection, and the
  host adapter over the offline stub surface with read-fault injection). A green stub run is
  **not** real-host proof.

## M5 DOM & market data (P4 — data reliability)

- M5 now ingests **real** host data: `Host/MarketDataCapture.cs` subscribes (read-only) to the
  loaded ES instrument's `Instrument.MarketData` (Level I) and `Instrument.MarketDepth` (Level II)
  `Update` events and feeds normalized `MarketDataRecord`/`DomRecord`s into `EventIngestPipeline`.
  The AddOn (re)subscribes before each pass via `EnsureMarketDataCapture` (wired through
  `RuntimeDiagnosticRunner`'s optional `Func<bool>`), and detaches on dispose.
- The event types live in `NinjaTrader.Data` (`MarketDataEventArgs`, `MarketDepthEventArgs`,
  `MarketDataType`, `Operation`); `MarketData`/`MarketDepth` live in `NinjaTrader.Cbi`. The real
  `Operation` enum has **only** `Add/Update/Remove` (no `Insert`) — the stub mirrors this so the
  `UseStubs=false` build compiles. `MarketDepthEventArgs.Position` is an `int` (0-based ladder).
- Subscribe on `instrument.Dispatcher` with `InvokeAsync` (documented pattern); guard with
  `HasShutdownStarted`/`HasShutdownFinished`. The stub `Instrument.Dispatcher` runs inline.
- `SnapshotAssembler.Assemble` now takes the whole `SessionContext` (not just counters) so
  `MergeDomMarketData` can derive freshness from `SessionContext.LastMarketDataEventUtc` (newest
  event instant, recorded by `M5DomMarketDataConsumer`). `M5DomMarketDataConsumer` also fixed a
  double-count: `IsReset` on a non-`Reset` operation increments `DomHostReset` once, not twice.
- Fail-closed contract: `M5.DOM_SURFACE`, `M5.DEPTH`, `M5.LEVEL1`, `M5.DATA_INTEGRITY`,
  `M5.FRESHNESS` stay `INCONCLUSIVE` with no observed surface; any positive counter (sync failure,
  out-of-bounds, invalid, gap, drop) is itself evidence → `FAIL`. `M5.FRESHNESS` is always
  `INCONCLUSIVE` and only labels `FRESH`/`STALE` with the real age; a future-stamped event (negative
  age) stays unobservable. Depth (Level II) is never inferred from a Level I quote.
- `M5.VALIDITY` is derived from real ingested records, not a constant: `M5DomMarketDataConsumer`
  counts a `DomRecord` with a negative/non-finite `Price`, a negative `Volume`, a negative
  `Position`, or an unknown `Operation`/empty `InstrumentId` (the latter as `DomHostMalformed`, and
  it contributes no depth-operation evidence). `Reset` is never validated. Only values that can
  never be legitimate are flagged, so a legitimate host field (e.g. a zero price on a removal) is
  never a false FAIL. A malformed record also makes the host layer `Observable` so its loss is not
  hidden behind an "unobservable" surface.
- `market-data-summary.json` / `dom-summary.json` carry `dataSurfaceObservable`, `depthObserved`,
  `lastEventUtc` (never fabricated; `UNOBSERVABLE` when absent). Tests: `M5DomMarketDataTests`
  (valid/missing/stale/invalid + host capture over the offline stub surface).
- Like M9, the observed M5/M6/M7 evidence must reach **both** paths: the live probe (via
  `ProbeContext` → `SnapshotAssembler.Assemble(GetSnapshot(), Session)`) and the exported package
  (`DiagnosticRuntime.BuildEvidenceDataset`, which now also calls `SnapshotAssembler.Assemble` on
  the session). Setting it only on the live path leaves the exported `market-data-summary.json` /
  `dom-summary.json` reporting an unobserved surface even though real records were ingested.
  Regression test: `Phase4RuntimeIntegrationTests.EvidenceExport_MarketDataSummary_ReflectsRealObservedIngestData`.

## M6 TradeDelta / OrderFlow (P5 — data reliability)

- M6's surface is the **trade stream** (`MarketDataRecord` of `Kind=Trade`) plus the Level II depth
  feed (for resets). The host adapter (`MarketDataCapture`) records the Level I `Last` print with
  the best bid/ask carried on the same event; NT8's public Level I surface has **no aggressor
  side**, so the direction is *derived*, never provider-confirmed.
- `Core/Engine/TradeDirectionClassifier.cs` is the host-free, pure classifier: price ≥ ask ⇒ Buy,
  price ≤ bid ⇒ Sell, inside a valid uncrossed spread ⇒ unknown `PriceInsideSpread`, crossed book ⇒
  `BboInvalid`, one side only ⇒ `BboIncomplete`, neither ⇒ `NoBboAvailable`. `ToPositiveOrNull`
  treats a host `0`/non-finite quote as unavailable. A derived direction is always counted as
  `TradeDirectionSource.DerivedFromQuote`; a print that already carries a classification or an
  explicit unknown reason is counted exactly as reported (never re-derived).
- `M6TradeDeltaConsumer` classifies from the event BBO, falling back to the **last observed quote**
  (`MarketDataRecord` of `Kind=Quote`). It counts buy/sell/unknown (with the exact cause, plus an
  `unclassified` bucket so the unknown total is fully attributed), the per-stream sequence
  observation, the weakest observed `SourceTimeBasis` (counter `td.sourceTimeBasis.worst`, encoded
  basis+1 so `Unknown=0` stays distinct from "no observation"), and **per-feed resets**
  (`td.reset.marketData`, `td.reset.depth`) — a Level II depth reset is what invalidates the
  derived order-flow/DOM mirror, so it is tracked separately from a Level I reset.
- `SnapshotAssembler.MergeTradeDelta` derives `TradeStreamObservable`, `SequenceObservable`,
  `AllDirectionsQuoteDerived`, the per-feed reset counts, the source-time basis and a
  `TradeDeltaCompleteness` when the host did not supply one (`COMPLETE`/`INCOMPLETE`/`UNOBSERVABLE`).
- Fail-closed contract: `M6.TRADE_STREAM`, `M6.SEQUENCE_CONTINUITY` and `M6.TRADEDELTA_COMPLETENESS`
  never `PASS` without an observed trade stream. `M6.SEQUENCE_CONTINUITY` additionally requires an
  **observed source sequence** (NT8 Level I exposes none), so a zero gap count is `INCONCLUSIVE`
  unless continuity was actually measurable; a positive gap count always `FAIL`s. A `COMPLETE` that
  the evidence contradicts (no observed stream / sequence gap / unclassified direction) `FAIL`s;
  an `INCOMPLETE` stays `INCONCLUSIVE`.
- `M6.ORDERFLOW_READINESS_VS_STRATEGY_SKIP` reports engine readiness vs a per-strategy decision as
  two scopes with `NOT_DECLARED_CONTRADICTION`; no public API exposes OrderFlow readiness, so an
  unsupplied value is reported `UNOBSERVABLE` with unobservable evidence, never assumed.
- `orderflow-summary.json` carries the derived evidence (`tradeStreamObservable`,
  `sequenceObservable`, `unknownUnclassified`, `sourceTimeBasis`, per-feed reset counts). Tests:
  `M6TradeDeltaTests` (classifier + consumer + assembler + probe + host capture over the offline
  stub surface + runtime integration); a green stub run is **not** real-host proof.

## M7 Strategies / Risk Gate (P6 — data reliability)

- M7 now derives its results from **real ingested strategy records**, not from the probe's own
  unclassified output (the M6 pattern, commit 785d13c). No public NinjaTrader API exposes strategy
  internals, so the only source is the read-only `IReadOnlyDiagnosticSource` contract a host or
  RiskPilot component may implement (`ReadStrategies()`); the AddOn exposes it via the
  `StrategyDiagnosticSource` property.
- Flow: `Host/StrategyDiagnosticCapture` (read-only, best-effort, never throws) pulls the records
  and feeds them through `DiagnosticRuntime.Ingest(StrategyRecord)`; the `M7StrategyConsumer` on the
  drain thread writes each into the session-scoped `Core/Engine/Consumers/StrategyEvidenceAccumulator`;
  `SnapshotAssembler.MergeStrategyRiskGate` reads the accumulator (counters fallback) so the live
  probe and the exported package see the **same** evidence. `RuntimeDiagnosticRunner` waits
  (bounded, `WaitForIngestDrain(500)`) for the drain thread after a capture that ingested anything.
- `Core/Engine/StrategyReasonCodes.cs` is the published stage catalog; attribution is delegated to
  it and a code it cannot place (unknown **or ambiguous across stages**, e.g. `FLAT_DIRECTION`) is
  reported **unattributed**, never guessed into a stage. Both the accumulator and the counters
  fallback use `StrategyReasonCodes.StageOf`, so the two paths agree.
- Correlation is **derived** from the records' own ids (never injected via a setter), so
  `M7.STAGE_CORRELATION` can only reflect what a real record reported. `RuntimeDiagnosticRunner`
  waits on the **wall clock** for the drain, so a deterministic/frozen injected clock can never
  make the host wait unbounded.
- Fail-closed contract: `M7.STRATEGY_SURFACE` is derived (`StrategySurfaceObservable` is read-only,
  true only with a real observation); `M7.STRATEGY_EVALUATION` reports each of the five registered
  strategies as observed/available/unavailable/missing-capability/`NOT_OBSERVED` — availability is
  only counted when the source reported it; `M7.CANDIDATE_REJECTION`/`STAGE3_SIZING`/`RISK_GATE`
  stay `INCONCLUSIVE` as raw distributions with no observed reason codes; `M7.STAGE_CORRELATION`
  needs `CorrelationObservable` (an incomplete observed surface is `INCONCLUSIVE` with the gap
  reason, a complete one is `PASS`); `M7.REASON_ATTRIBUTION` reports unattributed codes. Every
  INCONCLUSIVE carries a reason code that explains why.
- `strategies.json` carries the `riskGate` block (surface flag, observed record count, derived
  distributions, correlation, per-strategy rows with per-field observability); no raw account/
  connection identity enters it. Tests: `M7StrategyTests` (accumulator, assembler, host capture,
  runtime integration); a green stub run is **not** real-host proof.

## M8 Security Scan (P7 — security ownership)

- The read-only security scan is **owned and executed by M8** (`Core/Engine/Probes/M8SafetyAcceptanceProbe.cs`),
  never injected as a bare boolean from outside. M8 runs the host-free `Core/Security/SecurityScanner.cs`
  against the read-only source the data provider supplies. The previous out-of-band `StaticSafetyScan`
  property / `SafetyScanEvidence` class on `DiagnosticRuntime` were removed.
- The scan check id is `M8.SECURITY_SCAN` (was `M8.STATIC_SAFETY_SCAN`). It preserves every rule the
  offline `verification/addon-smoke` scan applied (order/account mutation, live-trading enable,
  reflection/unsafe/interop, identity-literal confinement, trading-disabled declaration), so there are
  no missing checks and no duplicate execution (M8 is the single executor).
- Source contract: `Core/Contract/ISecurityScanSource.cs` (`IsAvailable`, `IsComplete`, `GetSourceFiles()`,
  `ReadFile`, `FileExists`) — strictly read-only. The host adapter is `Host/FileSystemSecurityScanSource.cs`
  (reads production source under `src/RiskPilot.Diagnostics.Nt8`, excludes `/Stubs/`, `/obj/`, `/bin/`;
  never writes and never touches NT8 configuration). `IDiagnosticDataProvider.GetSecurityScanSource()`
  exposes it; the host provider returns a `FileSystemSecurityScanSource`.
- **Scan scope is confined to the BOOT installation directory.** The host adapter resolves only the
  standalone repo folder (`src/RiskPilot.Diagnostics.Nt8`) or the NinjaScript custom-compile folder
  `AddOns\RiskPilot.Diagnostics.Nt8` (the BOOT *child* of `AddOns`, never a bare `AddOns` parent), so an
  unrelated third-party/user AddOn can never be scanned or produce a BOOT finding. `ResolveDefaultRoot(base, documents)`
  is the testable resolution entry point. Every read passes one containment gate (`ResolveWithinRoot`):
  rooted and `..`-escaping relative paths are refused, and a reparse point (symbolic link/junction) on the
  root, a file, or any path component is never followed. The candidate-root presence probe and the tree
  walk are reparse-aware and bounded — never `SearchOption.AllDirectories`, which would follow a symlink
  out of the tree. The BOOT root itself must be a real (non-reparse) directory containing production source.
- **Incomplete coverage is never PASS.** `IsComplete` is false when any file/directory is skipped (an
  unreadable entry or a reparse point) or the tree was only partially enumerated; the scanner folds that
  into `SourceComplete` + `SECURITY_SCAN_INCOMPLETE`, and M8 reports the check `INCONCLUSIVE` (an empty
  or partially covered set is never a clean PASS). A skipped file is recorded, never silently ignored.
- Single result, two paths: M8 publishes the scan to `SessionContext.SecurityScan`; `SnapshotAssembler.MergeSecurityScan`
  copies it into the export snapshot, so the live probe and the exported `safety-audit.json` report the
  **same** scan without a second run.
- Fail-closed: a null/unavailable/unreadable source, or no readable file, ⇒ `INCONCLUSIVE` (never PASS);
  an unreadable file in the set is recorded as `SECURITY_SCAN_EXCEPTION` and blocks a clean PASS; a
  positive forbidden hit or a leaked identity literal or a missing trading-disabled declaration ⇒ `FAIL`.
  Exceptions and unavailable security info never silently produce PASS.
- Identity literals are never hardcoded in the scanner: `SecurityScan.ContainsIdentityLiteral` draws them
  from `ExpectedAccountIdentity`, so the scanner is never itself a leak site. `safety-audit.json` carries
  a `securityScan` block (executed/counts/reasonCodes/hits by rule+file+line only — no code fragment).
- Tests: `SecurityScanTests` (scanner rules + M8 ownership/reporting over the in-memory read-only source),
  `SecurityScanScopeTests` (the scope/traversal/symlink/incomplete-coverage regression set below), and the
  M8 cases in `Phase3ModuleTests` / `Phase5ScenarioTests`. A green stub run is **not** real-host proof.
- `SecurityScanScopeTests` covers: the correct BOOT directory; an unrelated sibling AddOn (never scanned);
  the custom-compile layout resolving the BOOT child of `AddOns` and never the bare `AddOns` parent;
  parent-directory and `..` traversal (refused) versus an in-root `..` (allowed); a BOOT root that is a
  symlink (unavailable); a symlinked directory/file inside BOOT (never traversed/read, marks incomplete);
  a missing/empty BOOT directory (unavailable, never PASS); an unreadable directory/file (incomplete,
  never PASS); and M8 reporting `INCONCLUSIVE` for an incomplete source set.

## P4 host-readiness evidence (nine facts, epochs, invalidation)

- `M1.HOST_READINESS` is not a Boolean. `Core/Run/HostReadiness.cs` defines the independent facts
  (`HostReadinessFacts`), the host-free derivation (`HostReadinessDerivation.AccountVerified` /
  `.ConnectionVerified`), the evaluator (`HostReadinessEvaluator`) and the thread-safe
  `HostReadinessTracker`. `Core/Models/Observations/HostReadinessObservation.cs` carries the
  three-valued `ReadinessFactState` (`Unknown`/`Satisfied`/`Unsatisfied`) and the `HostReadinessState`
  labels. Nine facts: `hostOperational`, `hostAvailable`, `metadataReadAllowed`, `instrumentResolved`,
  `metadataObserved`, `accountVerified`, `connectionVerified`, `marketDataSubscribed`,
  `marketDataObserved`. **No fact is derived from another without evidence** — the old model recorded
  `metadataReady` from the lifecycle gate, so Active alone looked metadata-ready. `FullyReady` is true
  only when every fact is `Satisfied`.
- Who records what: the AddOn records `hostOperational` (via `AddOnState`) and `hostAvailable`
  (`ApplyLifecycleState`); `MarketDataCapture.TrySubscribe` records `metadataReadAllowed`
  (the lifecycle gate), `instrumentResolved` and `metadataObserved` (via `ReadMetadataObserved`,
  which returns Unsatisfied on a throwing getter) and `marketDataSubscribed`; `OnMarketData`/
  `OnMarketDepth` record `marketDataObserved`; `Detach` clears the subscription+observed facts.
  `Nt8HostDataProvider.BuildHostReadinessObservation` overlays `accountVerified`/`connectionVerified`
  from the M2 `AccountIdentityObservation` / `TargetAccountConnectionObservation` (so an account in
  `Account.All` is never verification and a connected provider is never a verified association) and
  re-derives the overall state.
- **Epoch + invalidation.** `BeginEpoch` (driven from `SetDefaults` and from `Active` after a
  `Terminated`) clears every fact, so a restart never inherits readiness. `InvalidateStreamFacts`
  (driven by `DiagnosticAddOn.InvalidateReadinessForReconnect`) clears `connectionVerified` /
  `marketDataSubscribed` / `marketDataObserved`. The exported `hostReadiness` block carries `epoch`.
- `DiagnosticAddOn.Readiness` delegates to `Nt8HostDataProvider.ReadHostReadiness()` once the
  provider exists (single source of truth with the snapshot/evidence); before Active it reports the
  raw tracker observation. `OwnerAuthorizedProviderSlots` is a new AddOn input, **default empty**
  (fail-closed), threaded into the provider so the AddOn is never a slot-mapping leak.
- `environment.json` `hostReadiness` writes each fact as its `SATISFIED`/`UNSATISFIED`/`UNKNOWN`
  wire string (`ReadinessFactStates.ToWire`), never a Boolean.
- M7 ships no producer: `DiagnosticAddOn.StrategyDiagnosticSource` is null by default, so M7 stays
  `INCONCLUSIVE` (never a fabricated PASS). The read-only boundary and producer responsibilities are
  in `docs/M7_STRATEGY_INTEGRATION.md`. The real-host acceptance procedure is
  `docs/NT8_ACCEPTANCE_RUNBOOK.md` (procedure only — not executed from the offline build env).

## M1 lifecycle diagnostics (P9 — lifecycle ownership)

- M1 is the **single owner** of the AddOn lifecycle surface. The host records every `State` it sees
  in `OnStateChange` (`Host/DiagnosticAddOn.cs` `_lifecycleStates` / `LifecycleStates`), and the host
  provider reduces those transitions into `Core/Models/Observations/LifecycleObservation.cs`
  (`LifecycleSurfaceObservable`, `ObservedStates`, `IsStubOrMockValue`). The decision is host-free:
  `Core/Identity/LifecycleVerifier.cs` classifies each state into a phase
  (`LifecyclePhases`: INITIALIZATION / STARTUP / RUNTIME / SHUTDOWN / UNKNOWN) and validates the
  sequence. No verdict logic lives in the host layer.
- The lifecycle is supplied to the provider as a **live probe** (`new Nt8HostDataProvider(() =>
  HostIsLive, () => LifecycleStates, () => _readiness.Observe())`), not a captured list: the provider
  is built at `State.Active` and the host reaches its operational state (and loads its instrument)
  while the provider lives, so a captured list would freeze at the states seen up to `Active`. The
  previous untyped `RuntimeObservation.LifecycleStates` list was never populated by the host, so M1
  could not see the lifecycle at all; it was removed in favour of the dedicated observation +
  `DiagnosticSnapshot.Lifecycle`.
- Three M1 checks: `M1.LIFECYCLE` (the observed sequence + verdict), `M1.LIFECYCLE_PHASES` (which
  phases were confirmed) and `M1.HOST_READINESS` (the five separated readiness facts). The
  **connection phase is deferred to M2** (`connectionPhase=DEFERRED_TO_M2`) because no `State` member
  carries it — M1 never infers it and never reports it PASS, so there is no duplicate
  lifecycle/connection reporting across modules.
- Fail-closed contract (`LifecycleVerifier`): no/unobservable surface, a stub/mock value, an
  unrecognised `State` member, or a missing INITIALIZATION/STARTUP ⇒ `INCONCLUSIVE`; a backwards
  transition (rank decrease) or any state after `Terminated` ⇒ `FAIL`; only an ordered sequence that
  reached startup is `PASS`. `SetDefaults` may legitimately repeat (equal rank is allowed). Shutdown
  is optional (the tool normally runs while the AddOn is live) but if observed must be last.
- `environment.json` carries a `lifecycle` block (status, transition count, current state, the four
  phase flags, `connectionPhase`, observed/unclassified states, reason codes) or `"lifecycle":
  "UNOBSERVABLE"`; `EvidencePackageBuilder.WriteLifecycleBlock` re-evaluates the verifier from
  `dataset.Snapshot.Lifecycle`, so the exported evidence matches the live M1 probe. Tests:
  `LifecycleDiagnosticsTests` (verifier + M1 probe + host adapter over the offline stub surface) and
  the M1 cases in `Phase3ModuleTests` / `Phase4EvidencePackageTests`; a green stub run is **not**
  real-host proof.

## AddOn lifecycle / host readiness (root-cause fix)

- **The root cause:** a NinjaTrader 8 AddOn uses the **"Active state"** management system —
  `SetDefaults → Configure → Active → Terminated` — and **never** reaches `DataLoaded`/`Historical`/
  `Transition`/`Realtime` (those belong to Indicators/Strategies). The old `HostIsLive` accepted only
  the data-processing states, so it was permanently `false`, and host metadata, the selected
  instrument and market data were never observed. Never gate AddOn readiness on a data-processing
  state again.
- **One source of truth for the lifecycle predicate:** `Core/Identity/AddOnLifecycle.cs`
  (`IsOperational` accepts `Active` plus the data-processing states defensively; an unknown/null
  state is not operational). `DiagnosticAddOn.HostIsLive` reads the state recorded on the tracker, so
  the probe and the readiness model can never disagree, and the lifecycle is drivable offline via
  `DiagnosticAddOn.ApplyLifecycleState(string)` (which `OnStateChange` delegates to — the offline stub
  surface cannot raise the host callback).
- **Five separate readiness facts, never one Boolean:** `Core/Run/HostReadiness.cs`
  (`HostReadinessFacts`, `HostReadinessEvaluator`, `HostReadinessTracker`, `HostReadinessRetryPolicy`)
  + `Core/Models/Observations/HostReadinessObservation.cs`. States: `NOT_OBSERVED`,
  `ADDON_NOT_OPERATIONAL`, `ADDON_SHUTTING_DOWN`, `AWAITING_HOST`, `AWAITING_INSTRUMENT`,
  `AWAITING_METADATA`, `AWAITING_SUBSCRIPTION`, `READY`. Only all five hold ⇒ `READY`. Reaching
  `Active` alone is **never** ready (fail-closed). The tracker takes one private lock; the observation
  is derived on each read.
- **Delayed host readiness is handled by a bounded, idempotent retry loop.** The AddOn runs the first
  probe synchronously on the `Active` transition and schedules retries with geometric backoff
  (`HostReadinessRetryPolicy`, ~2 min budget) on a `System.Threading.Timer`; the loop stops once
  fully ready, on shutdown, or when the budget is exhausted. `MarketDataCapture.TrySubscribe` is
  idempotent, so a repeated probe never creates a duplicate subscription. `ReadinessRetryEnabled`
  (default true) is the deterministic test seam — tests set it false so no background thread races
  them; the first synchronous probe still runs.
- **The readiness observation reaches both paths:** the provider exposes it as a live probe
  (`() => _readiness.Observe()`), so `GetSnapshot().HostReadiness` feeds the live `M1.HOST_READINESS`
  probe and the exported `environment.json` `hostReadiness` block from one derivation.
- **Detach is exact:** `MarketDataCapture.Detach()` (driven by `SafeDispose` on `Terminated`) removes
  the host handlers and records the subscription fact false; `SafeDispose` stops the retry loop
  first so no timer callback re-subscribes after teardown. Tests: `HostReadinessTests` (evaluator /
  tracker / retry policy, incl. concurrency), `AddOnLifecycleReadinessTests` (AddOn lifecycle over
  the offline stub surface: Active, late instrument, no instrument, duplicate-subscription guard,
  terminate/detach, idempotent Active, concurrency, disposal), and the M1/evidence cases in
  `LifecycleDiagnosticsTests` / `Phase4EvidencePackageTests`. A green stub run is **not** real-host
  proof: the real `OnStateChange` timing and host object model are only provable inside NinjaTrader 8.

## NinjaTrader version compatibility (Prompt 10)

- The declared host versions live in **one** place: `Core/Identity/NinjaTraderCompatibility.cs` —
  `TargetVersion` (8.1.9.0), `MinimumSupportedVersion` (8.1.8.4), `TestedVersions` (empty) and
  `CompatibilityVerifiedAtRuntime` (false while `TestedVersions` is empty). Documents reference this
  declaration; never hardcode a version elsewhere.
- Minimum, target and tested are deliberately distinct. The **target is an intention, not a runtime
  fact**: no public NinjaTrader API exposes the installed host build, so `M1.HOST_COMPATIBILITY` is
  always `INCONCLUSIVE` (never `PASS`) until a real host build is actually run and recorded in
  `TestedVersions`. Editing a document must never make compatibility PASS.
- `environment.json` carries a `ninjaTraderCompatibility` block (target/minimum/verifiedAtRuntime/
  testedVersions/verifiedBuild/statement); `summary.html` shows the target with its verification status.
- **Redaction nuance:** a version string like `8.1.9.0` matches the IPv4 mask rule. `SecretRedactor`
  now applies the IPv4 rule separately and preserves only the exact declared version constants
  (`KnownNonSecretVersions` = target + minimum + tool version + assembly version); every other
  dotted-quad (a genuine IP) is still masked. If you add a new published version constant, add it to
  `KnownNonSecretVersions` too, or it will be masked in the evidence and can trip `IsSecretFree`.
- Tests: `NinjaTraderCompatibilityTests` (declaration invariants + M1 check + environment.json block +
  redaction round-trip). A green stub run is **not** real-host proof.
- **Static-scan guard:** `verification/addon-smoke/test_diagnostic_safety.py` fails if a NinjaTrader
  version literal (`"8.1.<n>..."`) appears in production source outside `NinjaTraderCompatibility.cs`,
  and fails if the `.csproj` header no longer contains the declared target/minimum. This is the
  machine check that stops a future doc/probe edit from silently re-introducing version drift.
- The SAMPLE generator derives each finding's severity from the real `DiagnosticResult` (matching
  `DiagnosticEngine`), so a sample `INCONCLUSIVE` is `MEDIUM`, not a hardcoded `INFO`.

## Stage 3 / Stage 4 coverage audit (Prompt 11)

- The Stage 3/4 traceability matrix lives in `Core/Run/Stage34Coverage.cs`: `Stage34CoverageCatalog.All()`
  is the single source of truth (requirement id, stage, BOT ref, BOOT module(s) + exact check id(s),
  host data, method, evidence class, scope, `requiresRealHost`/`requiresRithmicSimAccount`, test
  coverage, limitation). `Stage34CoverageEvaluator.Evaluate(result)` derives the per-run status from
  the results the run actually produced; the readable form is `docs/STAGE34_COVERAGE.md`.
- The catalog was reconciled against the authoritative RiskPilot.BOT Stage 3/4 records on `main`
  (`G3_STAGE3_STAGE4_ARCHITECTURE_CONTRACT.md`, `DECISION_LOG.md`, the D-031-2/R1, D-036/D-073,
  D-078/D-080, D-085, D-095 and D-096 owner decisions). Every runtime requirement names the BOOT
  test(s) that exercise its backing check(s) (`TestCoverage`); a source-only requirement has none.
  `Stage34CoverageTests` fails if a runtime requirement loses its test coverage or if one of the
  traced requirements (S3-D022-VERSION-CORRELATION, S3-NO-INVENTED-DATA, S4-ACCOUNT-STATE-UNBYPASSABLE,
  S4-DRAWDOWN-CONTINUITY, S34-THREAD-SAFETY, ID-CONTRACT-EXPIRY, ID-PLATFORM) is dropped.
- Coverage status is **derived, never asserted**: `VERIFIED` only when **every** backing check ran and
  PASSed; a missing backing check ⇒ `INCONCLUSIVE` (never PASS); a requirement with no runtime host API
  ⇒ `SOURCE_ONLY`. The artifact always carries a `statement` that BOOT does **not** fully validate
  Stage 3 or Stage 4.
- The matrix is exported as the **18th artifact** `stage34-coverage.json` (was 17). `EvidencePackageCatalog`
  is still the single source of truth for the artifact list; the manifest lists 17 files (all but
  `manifest.json`). `summary.html` gets a short coverage table, `full-report.html` the full matrix.
- **Never put a raw identity literal in `Stage34Coverage.cs`** (or any production source outside
  `Core/Identity/AccountIdentityVerifier.cs`): the static safety scan fails on a leaked `SimRP.BOT`.
  Describe the identity generically instead.
- `M4.ES_PRODUCT_SPEC` is the new check. It proves the loaded ES instrument's **economics**
  (point value 50 / tick size 0.25 / derived tick value 12.50 / USD) against
  `ExpectedEsProductSpecification` (published CME contract facts with explicit provenance).
  `M4.INSTRUMENT_IDENTITY` proves *which* instrument; this proves *its economics* (D-095 §4). Wrong
  ⇒ `FAIL`; missing ⇒ `INCONCLUSIVE`; the host adapter reads `MasterInstrument.*` after the live
  readiness probe and emits only a synthetic `instrument#<hash>`. The tick value is derived from
  point value × tick size; a host that exposes no tick value is not failed for it.
- Tests: `EsProductSpecificationTests`, the M4 ES-economics cases in `HostInstrumentIdentityTests`, and
  `Stage34CoverageTests`. A green stub run is **not** real-host proof.

## Menu-item ownership & lifecycle (P17 fix)

- `MenuIntegration` no longer uses a global `bool _added`. Ownership lives in the host-free,
  unit-tested `Core/Run/MenuOwnershipRegistry.cs` (plus `MenuOwnerClassifier` and the
  `DiagnosticMenu.ItemId` constant). The WPF adapter in `Host/MenuIntegration.cs` is a thin wrapper
  over it: it finds the Control Center's main `Menu`, wraps it in an `IMenuSurface`, and delegates
  the ownership decision. This follows the repo rule that all testable UI-adjacent logic lives in
  `Core/Run` (the WPF files are not compiled offline).
- **The intended owner is the Control Center only.** `MenuOwnerClassifier` accepts
  `NinjaTrader.Gui.ControlCenter` / `ControlCenter` (matched by walking the window's type hierarchy,
  string-based like the provider-identity path); every other NinjaTrader window is `Other` and is
  refused without touching the menu. Never add the item to an arbitrary window.
- Each supported owner window has its **own** item on its **own** menu (so multiple Control Center
  windows across a reload each integrate correctly). A repeat Attach by the same owner is an
  idempotent `AlreadyAttached` no-op; a **second window presenting the same menu** is refused
  (`SurfaceAlreadyOwned`), so the item is never added twice to one menu. A stale surface item under
  our id is cleared first. An unalive owner / non-ControlCenter owner / failed add is refused
  (`OwnerNotAlive` / `UnsupportedOwner` / `AddFailed`), never a false success.
- Removal (`Remove`/`Clear`, also driven by the owner window's `Closed` handler) removes the item,
  detaches the click handler, and drops the owner/surface/callback references, so a destroyed window
  and its menu are never retained through a static reference. `Reset()` clears the registry and
  drains the `Closed` subscriptions, so a host restart / AddOn reload can re-register cleanly.
- Thread safety: every registry entry point takes one private lock. Surface mutations (add/remove)
  run **under** the lock so the single-item invariant holds under concurrency; only the owner's
  removal callback runs **after** the lock is released (no deadlock, no re-entrancy). In the host
  adapter the registry entry and the window-close subscription are created/removed under the same
  `Gate`, so a concurrent `Reset` cannot leave a dangling subscription or an unsubscribed entry.
  Tests: `MenuIntegrationLifecycleTests` (first/repeat/multi-owner registration, owner destruction,
  removal, handler cleanup, reset-then-reattach, no-duplicate, concurrency, classifier). A green
  stub run is **not** real-host proof — the WPF adapter itself is only compiled in the
  `UseStubs=false` build.


## UI dispatcher lifecycle (P12 — Control Center ownership)

- NinjaTrader raises `OnWindowCreated`/`OnWindowDestroyed` for **every** NTWindow (Control Center,
  charts, DOM, SuperDOM, workspaces), in that window's own thread. The old `DiagnosticAddOn.Ui.cs`
  attached on any window and detached on any window, so closing an unrelated chart cleared the
  dispatcher the Diagnostic Center was using. The fix gates both hooks on the **Control Center**:
  `OnWindowCreated` returns unless `window is NinjaTrader.Gui.ControlCenter` (the documented cast),
  and `OnWindowDestroyed` detaches only when the window owns the active dispatcher.
- Ownership lives in the host-free `Core/Run/SwitchableUiDispatcher`, not in the host hook:
  `Attach(object owner, IUiDispatcher dispatcher)` records the owner and refuses (returns false,
  leaving the target in place) a different owner while a **live** target is held; a shut-down
  target is replaced regardless of owner so a re-created Control Center can take over. `Detach(object owner)`
  clears the target only for the current owner (an unrelated window's destroy is a no-op); the
  parameterless `Detach()` clears both target and owner unconditionally and is what AddOn termination
  (`ShutdownUiLayer`) uses, so no stale dispatcher reference survives an AddOn reload.
- `SwitchableUiDispatcher.Attach` also refuses a null or already-shut-down dispatcher (a dead
  dispatcher is never adopted, so notifications keep running inline instead of being dropped).
  `Post` still runs inline with no target; `HasShutdownStarted` stays false with no target so
  pre-window notifications are never dropped.
- `WpfUiDispatcher.HasShutdownStarted` covers **both** `HasShutdownStarted` and `HasShutdownFinished`;
  `Post` contains an `InvalidOperationException` from `InvokeAsync` (the dispatcher can shut down
  between the flag check and the post) and reports the post as not queued, so a shutdown race never
  faults the run worker.
- The offline `System.Windows.Threading.Dispatcher` stub gained settable `HasShutdownStarted` /
  `HasShutdownFinished` / `OnDispatcherThread` / `ThrowOnInvokeAsync` switches (the existing
  fault-injection convention) so the shutdown paths are exercised over the stub surface.
- Tests: `UiDispatcherLifecycleTests` (unrelated window create/destroy, owning window destroy,
  repeated attach/detach, dispatcher shutdown, AddOn terminate + reinitialise, concurrency). They
  drive the real `SwitchableUiDispatcher` and the real `WpfUiDispatcher`; the thin `is ControlCenter`
  host guard is mirrored as test scaffolding. A green stub run is **not** real-host proof: the real
  `NinjaTrader.Gui.ControlCenter` cast and the real WPF dispatcher shutdown behaviour are only
  provable inside NinjaTrader 8.


## Combined lifecycle integration audit (dispatcher + menu + window)

- The three lifecycle PRs (dispatcher ownership, menu ownership, window manager) were audited
  together; the combined host sequence is exercised by `LifecycleIntegrationTests.cs` over the real
  host-free primitives (`SwitchableUiDispatcher`, `MenuOwnershipRegistry`, `DiagnosticWindowController`)
  and the real `WpfUiDispatcher`/stub `Dispatcher`. The controller is process-wide/single-window;
  it is not keyed by the Control Center owner object. The WPF adapters (`DiagnosticAddOn.Ui.cs`,
  `MenuIntegration`, `DiagnosticCenterWindowManager`, `DiagnosticCenterWindow`) are only compiled for
  `UseStubs=false`, so they are not covered offline.
- No integration defect was found, but the three layers do not share one ownership key: the
  dispatcher and menu registry are keyed by the Control Center window object, while the
  Diagnostic Center controller tracks one process-wide window and receives the Control Center
  dispatcher when Show is requested. The combined test exercises one host-like sequence; it does
  not prove a common owner key across all three layers. No production source was changed by the audit.
- **Post-termination stale-destroy behavior (documented, not a defect):** `DiagnosticAddOn.Ui.cs`
  is a long-lived AddOn instance in a single NT8 process. If the Control Center is destroyed *after*
  `SafeDispose` ran, `_uiDispatcher` is the same instance that already ran the unconditional
  `Detach()`; `Detach(window)` returns false (owner already null), so the guard does not run and no
  stale `_windowManager`/menu can be cleared incorrectly. In normal operation `OnWindowDestroyed`
  fires before `SafeDispose`, which is what clears `_windowManager` and resets the menu.
- `MenuIntegration.Detach(Window)` is production-dead code: the AddOn drives menu cleanup through
  `MenuIntegration.Reset()` only. It is correct and harmless; do not wire it without re-auditing.
- Reopening the Diagnostic Center relies on `DiagnosticWindowController.CloseIfOpen` clearing the
  tracked reference **before** queuing the close, so a reopen is never blocked by a half-closed
  window (`WindowLifecycleTests` + `LifecycleIntegrationTests`).
- A run started before the Control Center window exists is applied inline (no dispatcher target);
  once a real dispatcher attaches, later notifications are marshalled (`InvokeAsync`) and only
  rendered when the UI thread drains the queue. The live probe and the exported package both read the
  thread-safe `DiagnosticViewState`, so a deferred render is a display-timing property, not a lost
  result.
- Real-host behavior (the `NinjaTrader.Gui.ControlCenter` cast, per-window `OnWindowCreated`/
  `OnWindowDestroyed` threading, and real `Dispatcher.HasShutdownStarted`/`HasShutdownFinished`
  timing) is **not** provable offline and still needs the manual NT8 acceptance tests in
  `docs/LIFECYCLE_INTEGRATION_AUDIT.md`.

## Operator target-instrument selection & verified environment class (targeted remediation)

- **The target instrument is chosen explicitly by the operator, never auto-discovered.** The
  host-free model is `Core/Instruments/TargetInstrumentSelection.cs` (observable selected id +
  available list + `Changed`), the decision rule is the existing
  `Core/Instruments/InstrumentSelector.cs`, and `Core/Run/IInstrumentSelectionController.cs` is the
  minimal surface the Diagnostic Center drives. `DiagnosticAddOn` implements it over the
  read-only `Instrument.All` discovery surface (`Nt8HostDataProvider.TryEnumerateLoadedInstrumentNames`).
  No component ever picks the first/nearest-expiry contract.
- **One selection source of truth, read live.** The AddOn owns one `TargetInstrumentSelection` and
  threads `() => _instrumentSelection.SelectedInstrumentId` (a **live probe**, never a captured
  string) into both `Nt8HostDataProvider` and `MarketDataCapture`, so a selection the operator makes
  *after* the AddOn is constructed (at `State.Active`) is honoured by the very next resolution.
  `RequestedInstrumentId` on the AddOn is now a thin proxy over the same model.
- **Fail-closed selection statuses** (`InstrumentSelectionStatus`): no selection ⇒ `NOT_CONFIGURED`;
  a selected contract that is not loaded ⇒ `NOT_LOADED`; an insufficiently specific selection
  matching >1 loaded contract ⇒ `MULTIPLE_CANDIDATES`; a surface that cannot be enumerated ⇒
  `SURFACE_UNAVAILABLE`; a stub/mock value ⇒ `STUB_OR_MOCK`. Only an exact, unique match ⇒ `RESOLVED`.
  A null or **throwing** selection probe is treated as "no selection" (never an implicit instrument).
- **Re-targeting is idempotent and never duplicates a subscription.** `MarketDataCapture.TrySubscribe`
  is a no-op while already subscribed to the resolved instrument; on a selection change it calls
  `DetachInstrumentLocked` before subscribing to the new contract, so two contracts are never
  subscribed at once. Clearing the selection (or a selection that no longer resolves) detaches and
  reports the instrument fact unresolved. `Detach()` and the locked helper are the only detach paths.
- **The picker lives in `Core/Run`.** The WPF `DiagnosticCenterWindow` only renders the shared model
  (a ComboBox of exact FullNames + a Refresh button + a status line) and writes the operator's
  choice through the controller; a programmatic repopulation runs under a guard so it is never
  mistaken for an operator action, and a refresh never changes the current selection. The WPF files
  are `UseStubs != true`-only, so this is verified by review, not by the offline stub build.
- **The environment class is derived from the full verified chain
  `Target Account → Verified Connection → Provider Identity → Environment Classification`.** It is
  never taken from the first `Account.All` entry (the previous "Sim*" name heuristic is gone) and
  never from a verified account whose *connection association* is unproven. The links:
  - `Core/Identity/EnvironmentClassifier.cs` (`Classify` and `ClassifyVerifiedChain`) is the
    host-free rule: any unresolved link ⇒ `EnvironmentClass.Unknown`, never `LIVE`/`SIM`.
  - `Core/Identity/EnvironmentVerificationChain.cs` evaluates the four links from the M2
    `AccountIdentityObservation`, the `TargetAccountConnectionObservation` and the derived class,
    and reports per-link booleans + reason codes (`ENVIRONMENT_CHAIN_ACCOUNT_UNVERIFIED` /
    `_CONNECTION_UNVERIFIED` / `_PROVIDER_UNVERIFIED` / `_CLASS_UNKNOWN`; `ENVIRONMENT_CHAIN_VERIFIED`
    when every link holds). A link with a null/unobservable observation fails closed.
  - `Nt8HostDataProvider.DetectEnvironment` / `ClassifyTargetAccountEnvironment` resolve the verified
    target (`TryGetVerifiedTargetAccount`) **and** require its own association verdict
    (`TargetAccountConnectionVerifier.Evaluate(BuildTargetAccountConnectionObservation()) == PASS`)
    before reading `target.Connection.Options.Mode`. A connected market-data connection is never
    mistaken for a verified account connection; a disconnected/ambiguous/different-connection target
    stays `Unknown`. The same helper backs `AccountRecord.AccountClass`.
  - `M1.ENVIRONMENT_CLASS` PASSes only with a fully verified chain; otherwise `INCONCLUSIVE` with the
    chain reason codes (`CHAIN_*_VERIFIED=...` booleans in the reason codes). `environment.json`
    gains an `environmentVerificationChain` block (`verified`, `verifiedEnvironmentClass`, the four
    `links` booleans, `reasonCodes`); an unresolved link always writes `verified:false` and
    `verifiedEnvironmentClass:UNKNOWN`, so a verified environment is never reported off a partial
    chain. The block is re-evaluated from `dataset.Snapshot` (the same derivation as the live probe).
  - Fail-closed: no verified target, an unproven/ambiguous/disconnected association, an unresolved
    provider, or an unreadable mode ⇒ `Unknown` (M1 `INCONCLUSIVE`), never a guess.
- Tests: `EnvironmentClassificationTests` (classifier + `ClassifyVerifiedChain` + host adapter over
  the offline stub surface: verified Simulation ⇒ Simulation; verified Live ⇒ Live at the classifier
  level; no verified target / unverified Live target / no connection / no accounts / disconnected
  target connection / ambiguous accounts / ambiguous connections / different connection / connection
  not enumerated ⇒ Unknown; first-account-not-target never drives the class; enumeration order never
  changes the result; the M1 check PASSes only with a fully verified chain and stays INCONCLUSIVE
  otherwise; the exported `environment.json` chain block) and the new cases in
  `InstrumentSelectionTests` (selection model, AddOn refresh/select flow, live probe sees a
  post-construction selection, throwing probe fails closed, re-target detaches the previous
  instrument). A green stub run is **not** real-host proof.
- **Known pre-existing flake (not caused by this work):** `M7StrategyTests.RuntimeIntegration_
  IngestedStrategyRecord_DrivesM7ObservationAndEvidence` can observe `M7.STRATEGY_SURFACE=INCONCLUSIVE`
  because `DiagnosticRuntime.Process` increments `Session.RecordProcessed()` **before**
  `dispatcher.Dispatch(record)`, so a test waiting on `ProcessedCount` may read the accumulator
  before the M7 consumer wrote it. Reproduced on the untouched baseline (~1/25 runs); left as-is
  (out of scope).

## D-097 acceptance run (identity, target instrument, readiness wait, M7)

- The observed host facts are 19 accounts, exactly one exact-name match, and one connected
  `Rithmic` connection (`Provider14`, Live). M2 must PASS with exactly those inputs and must still
  refuse any other account, connection name, unresolved slot, mode or unreadable field.
  `D097IdentityAcceptanceTests` covers the positive path plus each negative boundary.
- The permanent identity is account `SimRP.BOT`, connection `Rithmic`, provider `Provider14` resolved
  to `Rithmic` by BOOT's compiled immutable exact-match mapping, mode `Simulation` **or** `Live` for
  that one SIM account, denomination `USD`. The mode acceptance is recorded as `ModeIsAccepted`;
  `ModeIsSimulation` remains a separate fact so evidence still shows which mode was observed.
- The explicit target instrument is `ES 12-26` (compiled default). It must be loaded and uniquely
  matched; the tool never auto-discovers a first/nearest contract.
- `RuntimeDiagnosticRunner` waits for `HostReadinessObservation.FullyReady` before a **Full** pass,
  bounded by `DiagnosticAddOn.ReadinessWaitTimeoutMs` (default 20 s). Quick and Post-Trade Audit do
  not wait, preserving their previous semantics. Cancellation stops the wait before any probe. The
  wait only delays the pass; it never changes a verdict, so a FAIL stays FAIL. The wait is
  deterministic-testable through the injectable sleep delegate and a zero timeout.
- M7 executes the read-only strategy verification when `DiagnosticAddOn.StrategyDiagnosticSource`
  supplies records (`CaptureStrategyObservations` → ingest → drain → M7); with no source it stays
  `INCONCLUSIVE`. `D097M7IntegrationTests` covers both paths, including the exported snapshot.
- `docs/STAGE34_COVERAGE.md` and the runbook carry the D-097 identity text; keep them in sync with
  `ExpectedAccountIdentity` and `ProviderIdentity.OwnerAuthorizedSlots`.

## Reference repo

`Ai00120/RiskPilot.BOT` is **read-only reference** for study. Never modify it. Its stubs live
at `src/RiskPilot.BOT/Stubs/NinjaTraderStubs.cs`; its two-build-mode csproj is the pattern
mirrored here.

## Provider identity — the `Provider14` contract (P2 / D-097)

- The real `NinjaTrader.Cbi.Provider` enum has **no** dedicated `Rithmic` member. Named members are
  `Collective2=0 .. Yahoo=19`; an adapter without a dedicated member is assigned a **generic
  placeholder slot** (`Provider1..Provider40` = 20..59, `Custom0..Custom45` = 1000..1045,
  `Unknown` = 9999). The diagnostic environment reports `Provider14`, a placeholder slot that
  carries **no vendor identity** in the enum itself. Never map any other slot to Rithmic (or any
  vendor) by guesswork, display name, or an online reference.
- `Core/Identity/ProviderIdentity.cs` is the single host-free source of truth: `Classify`
  (Named / PlaceholderSlot / Unobservable), `IsPlaceholderSlot`, `Resolve`, `Compare` (three-valued
  `ProviderIdentityMatch`: `Matches` / `Differs` / `Unresolved`), `IsExpectedProvider`,
  `IsUnresolved` and `TryAgree`. D-097 supplies exactly one **compiled, immutable, exact-match**
  owner-authorized mapping — `Provider14` → `Rithmic` (`OwnerAuthorizedSlots`) — so the observed
  host slot resolves without any runtime input. `NoOwnerAuthorizedSlots` remains available for the
  fail-closed path. The lookup is ordinal (`provider14`, `Provider140` do not resolve).
- Fail-closed: a definite named member that differs ⇒ `FAIL`; an unreadable value or any other
  unresolved placeholder slot ⇒ `INCONCLUSIVE` (`PROVIDER_SLOT_UNRESOLVED` /
  `PROVIDER_ROUTE_UNRESOLVED` / `ACCOUNT_PROVIDER_UNRESOLVED` / `CONNECTION_PROVIDER_UNRESOLVED`),
  never a false mismatch.
- M2 (`M2ConnectionProbe`) classifies the provider and counts account/connection provider
  agreement three-valued: a placeholder on either side is counted as
  `PROVIDER_AGREEMENT_UNRESOLVED` (INCONCLUSIVE), not a mismatch. The instrument verifier treats
  an unresolved provider path as `PROVIDER_ROUTE_UNRESOLVED` so a Globex route is never confirmed
  from an unidentified provider. `M2.ACCOUNT_CONNECTION_ASSOCIATION` additionally requires the
  target account's own connection object to be among the host's enumerated connections
  (`TARGET_ACCOUNT_CONNECTION_NOT_ENUMERATED`): a name match alone is not proof of association.
- `Host/Nt8HostDataProvider` uses the compiled D-097 mapping by default; the full constructor still
  accepts an explicit mapping so the adapter logic can be exercised with an empty mapping offline.
  The offline stub `Provider` enum mirrors the real surface (no `Rithmic`); tests use
  `Provider.Provider14` and both the production mapping and a reviewed test mapping. A green stub
  run is **not** real-host proof. See `tests/RiskPilot.Diagnostics.Nt8.Tests/ProviderIdentityTests.cs`
  and `D097IdentityAcceptanceTests.cs`.
