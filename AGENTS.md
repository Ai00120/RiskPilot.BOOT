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
- `src/RiskPilot.Diagnostics.Nt8/Core/Evidence/` — 17-artifact evidence package builder.
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
- `EvidencePackageBuilder.Build` is pure (no host/FS/clock). `VerifyIntegrity` checks all 17
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
- The `UseStubs=false` configuration references five NT8 assemblies (Core, Cbi, Data,
  NinjaScript, Gui). The real-host build is not verifiable offline; never claim it is.

## Exact account identity notes (P16 / D-095 / D-096)

- M2 verifies the **exact** permanent RiskPilot.BOT identity: account `SimRP.BOT`, connection
  `My Rithmic`, provider `Rithmic`, mode `Simulation`, currency `USD`.
- The only place the raw account/connection name literals live is
  `Core/Identity/AccountIdentityVerifier.cs` (`ExpectedAccountIdentity`). The static safety scan
  fails if `SimRP.BOT` appears in any other production source file, so an accidental identity
  leak into a diagnostic/log/report is caught. Tests and docs are not scanned.
- The Host adapter (`Nt8HostDataProvider.BuildAccountIdentityObservation()`) does the exact
  ordinal name comparison and reduces everything to booleans/counts + `SyntheticRef`; the raw
  names never enter `AccountIdentityObservation`, a finding, or an artifact.
- Fail-closed: no account surface / stub value / zero accounts / ambiguous multi-match /
  any unobserved required field ⇒ `INCONCLUSIVE`; a similar or differently-cased name, wrong
  provider/connection/mode/currency ⇒ `FAIL`. Only a fully observed, fully matching identity is
  `PASS`. A mismatch outranks an unobservable field (proven wrong beats unproven).
- `Account.Denomination` is mapped to an ISO code (`Currency.UsDollar` → `USD`) in the host; an
  unmapped member is unobservable, never guessed from the member name.
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

- `M4.INSTRUMENT_IDENTITY` **proves** the instrument NinjaTrader actually has selected/loaded is
  the permanent ES/CME identity. The host reads the loaded discovery surface `Instrument.All`
  (never `Instrument.GetInstrument(<configured string>)`) and reduces every field to
  booleans/counts + a `SyntheticRef` `instrument#<hash>` in `InstrumentIdentityObservation`; the
  decision is `Core/Identity/InstrumentIdentityVerifier` (fail-closed). The raw symbol never enters
  the observation, a finding or an artifact.
- The venue is confirmed only by the D-096 configured-route metadata: selected route `Globex`,
  configured exchange set exactly `{Globex}` and the sole **connected** provider path `Rithmic`.
  CME is **never** inferred from the `ES` symbol text. An unconfirmed exchange ⇒
  `INCONCLUSIVE` (`EXCHANGE_METADATA_UNOBSERVABLE`), never PASS; an observed route that contradicts
  ⇒ `FAIL` (`EXCHANGE_MISMATCH`).
- Fail-closed mapping: no surface ⇒ `INCONCLUSIVE` (`INSTRUMENT_IDENTITY_UNOBSERVABLE`); no loaded
  instrument ⇒ `INCONCLUSIVE` (`..._NO_INSTRUMENT_OBSERVED`); no readable root at all ⇒
  `INCONCLUSIVE` (`INSTRUMENT_ROOT_UNOBSERVABLE`, never a false FAIL); a root that was read and does
  not match exactly ⇒ `FAIL` (`INSTRUMENT_ROOT_MISMATCH`); more than one matching root ⇒
  `INCONCLUSIVE` (`..._AMBIGUOUS`); a stub/mock value ⇒ `INCONCLUSIVE`. A mismatch outranks an
  unobservable field. The expiry is **not** pinned: only the canonical root/venue form
  (`ES_FUTURES_CME_*`) is required, so a manual contract roll keeps the identity valid.
- `MasterInstrument.*` is only read when the provider's **live** readiness probe returns true. The
  probe is a `Func<bool>` evaluated on every read, not a value captured at construction: an AddOn is
  constructed at `State.Active`, before `MasterInstrument.*` is safe to read, so a captured flag would
  stay false for the whole session and leave M4 permanently `INCONCLUSIVE`. The AddOn supplies
  `() => HostIsLive` (true once the state reaches `DataLoaded`/`Historical`/`Transition`/`Realtime`);
  a throwing probe fails closed to not-ready. Before readiness the identity is `INCONCLUSIVE`, not FAIL.
- The D-078/D-096 attestation target is the **actual** loaded instrument: with no configured id the
  host consults `Instrument.All` and attests the instrument whose `MasterInstrument.Name` matches the
  permanent ES root (`ExpectedInstrumentIdentity.RootMatches`), reading that real object's metadata
  (never one built from a configured string). Zero matching roots or more than one (ambiguous) ⇒
  `HOST_UNAVAILABLE` (fail closed); a configured id, when present, still wins.
- The check is emitted **even when the D-078/D-096 attestation observation is null**, so M4 always
  reports the instrument identity.
- `instruments.json` carries an `instrumentIdentity` block (status, counts, reason codes, masked
  ref); `EvidencePackageBuilder` re-evaluates the verifier from `dataset.Snapshot.InstrumentIdentity`.
- Tests: `InstrumentIdentityTests` (verifier + probe) and `HostInstrumentIdentityTests` (host
  adapter over the offline stub surface); a green stub run is **not** real-host proof.

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
- `market-data-summary.json` / `dom-summary.json` carry `dataSurfaceObservable`, `depthObserved`,
  `lastEventUtc` (never fabricated; `UNOBSERVABLE` when absent). Tests: `M5DomMarketDataTests`
  (valid/missing/stale/invalid + host capture over the offline stub surface).

## Reference repo

`Ai00120/RiskPilot.BOT` is **read-only reference** for study. Never modify it. Its stubs live
at `src/RiskPilot.BOT/Stubs/NinjaTraderStubs.cs`; its two-build-mode csproj is the pattern
mirrored here.
