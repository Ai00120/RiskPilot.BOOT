# Changelog

All notable changes to the RiskPilot NT8 Diagnostic Center (`RiskPilot.Diagnostics.Nt8`) are
recorded here. This repository is independent of `Ai00120/RiskPilot.BOT` and has no execution
dependency on it.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- **Deterministic NinjaScript source-bundle packager.** `tools/nt8-source-package/package_addon_sources.py`
  stages only the `Core/` and `Host/` `.cs` files explicitly compiled by the standalone project,
  fails closed on any `AssemblyInfo.cs`, `[assembly: ...]` attribute, `bin/`, `obj/` or build
  artifact, and is idempotent on re-run. It is now the only supported way to place BOOT sources
  under `Documents\NinjaTrader 8\bin\Custom\AddOns\`. `docs/INSTALL.md` and `README.md` document
  the CS0579 root cause and recovery. Regression coverage:
  `verification/addon-smoke/test_nt8_compilation_regression.py` (also wired into both CI
  workflows) plus `CompilationRegressionTests`.

- **Stage 3 / Stage 4 diagnostic coverage audit (Prompt 11).** A final integration audit traced the
  important Stage 3 (Position Sizing) and Stage 4 (Independent Risk Gate) requirements defined in
  RiskPilot.BOT to the BOOT modules and checks that can provide evidence for them. The matrix is
  defined once in `Core/Run/Stage34Coverage.cs` (`Stage34CoverageCatalog`) and evaluated per run by
  `Stage34CoverageEvaluator` from the results the run actually produced; it is exported with every
  run as the new **18th artifact** `stage34-coverage.json` and shown in `summary.html` (short table)
  and `full-report.html` (full matrix). The readable form is `docs/STAGE34_COVERAGE.md`.
  - Each requirement records its BOT reference, the BOOT module(s) and exact check id(s), the host
    data and verification method, an evidence class (`RUNTIME_HOST` / `SOURCE_LEVEL`), a verification
    scope (`FULL` / `PARTIAL` / `NONE`), whether it needs a real host / a connected Rithmic SIM
    account, and the BOOT test(s) that exercise its backing checks. The per-run status is **derived,
    never asserted**: `VERIFIED` only when every backing check ran and PASSed; a missing backing
    check leaves it `INCONCLUSIVE` (never `PASS`); a requirement with no runtime host API is
    `SOURCE_ONLY`. The artifact always carries an explicit `statement` that BOOT does **not** fully
    validate Stage 3 or Stage 4.
  - The catalog was reconciled against the authoritative RiskPilot.BOT Stage 3/4 records on `main`
    (`G3_STAGE3_STAGE4_ARCHITECTURE_CONTRACT.md`, `DECISION_LOG.md`, the D-031-2/R1, D-036/D-073,
    D-078/D-080, D-085, D-095 and D-096 owner decisions). That added seven requirements the first
    pass had overlooked: policy/configuration versioning and snapshot correlation (D-022 condition 6),
    the non-bypassable verified-account enforcement / setter-visibility trust boundary (OD-3 §6),
    drawdown / high-water-mark continuity (D-031-2 / R1), thread-safety and subscriber isolation
    (cross-stage invariant 8), the contract-expiry pin and manual-rollover governance (D-085 /
    D-095 §5), the NinjaTrader 8 platform identity, and the no-invented-data invariant (invariant 4).
    `Stage34CoverageTests` fails if a runtime requirement loses its test coverage or if one of these
    traced requirements is dropped.
  - New read-only check **`M4.ES_PRODUCT_SPEC`** (M4): proves the loaded ES instrument's *economics*
    — point value 50, tick size 0.25, derived tick value 12.50, currency USD — which Owner Decision
    D-095 §4 makes part of the one immutable canonical ES specification. `M4.INSTRUMENT_IDENTITY`
    proves *which* instrument is loaded; this proves *its economics*. The host reads the loaded
    instrument's `MasterInstrument.*` (after the live readiness probe) and reduces it to numbers plus
    a synthetic `instrument#<hash>`; a wrong value is `FAIL`, a missing value is `INCONCLUSIVE`
    (never `PASS`), a stub/mock value is `INCONCLUSIVE`. Tests: `EsProductSpecificationTests`, the M4
    ES-economics cases in `HostInstrumentIdentityTests` (host adapter over the offline stub surface)
    and `Stage34CoverageTests`.

### Changed

- The evidence package now contains **18 artifacts** (was 17): `stage34-coverage.json` was added.
  `EvidencePackageCatalog` remains the single source of truth; the manifest lists the other 17 files.
  The committed SAMPLE package and `SHA256SUMS.txt` were regenerated.

- **NinjaTrader version documentation aligned to the declared target 8.1.9.0.** The documented
  target host was 8.1.8.4; the reported target environment is 8.1.9.0. The minimum supported
  version (8.1.8.4), the declared target (8.1.9.0) and the tested version(s) are now kept distinct
  and declared in one reviewed place, `Core/Identity/NinjaTraderCompatibility.cs`, so no document
  can drift independently. Documentation alignment is **not** a compatibility claim: the tested
  version list is empty because the NinjaTrader assemblies are not available in the offline build
  environment, so `CompatibilityVerifiedAtRuntime` is `false` and M1 emits a new
  `M1.HOST_COMPATIBILITY` check that is always `INCONCLUSIVE` (never `PASS`) until a real-host run
  is recorded. `environment.json` gains a `ninjaTraderCompatibility` block (targetVersion,
  minimumSupportedVersion, verifiedAtRuntime, testedVersions, verifiedBuild, statement) and
  `summary.html` shows the target version with its verification status. `SecretRedactor` now
  preserves the tool's own declared version strings (which share the IPv4 shape) while still
  masking genuine IP addresses, so the compatibility evidence is not corrupted. The offline static
  safety scan now also fails on version drift: a NinjaTrader version literal outside the single
  definition file, or a `.csproj` header that disagrees with the declared constants. Tests:
  `NinjaTraderCompatibilityTests`.

### Fixed

- **NinjaTrader 8.1.9.0 CS0579 duplicate assembly attributes.** Root cause: NT8 compiles every
  `.cs` file under `bin\Custom` into one `NinjaTrader.Custom.dll` that already has its own
  `bin\Custom\AssemblyInfo.cs`. Copying `Properties/AssemblyInfo.cs` (or VS-generated
  `obj/**/*.AssemblyInfo.cs`) into the Custom tree duplicated AssemblyTitle, AssemblyDescription,
  AssemblyCompany, AssemblyProduct, AssemblyCopyright, ComVisible and AssemblyVersion. The
  standalone project's own `AssemblyInfo.cs` and `GenerateAssemblyInfo=false` are correct and are
  **preserved**; the deployment procedure is now a deterministic, fail-closed source bundle that
  never stages assembly metadata. No assembly metadata was removed from the repository.

- **NinjaTrader 8.1.9.0 CS0117 `Currency` does not contain a definition for `Yen`.** Root cause:
  the real `NinjaTrader.Cbi.Currency` enum has no `Yen` member (JPY is `JapaneseYen`, GBP is
  `BritishPound`); the offline stub and the generic USD/EUR/JPY/GBP mapping were wrong. The stub
  now mirrors the reflected NT8 enum names/values, and the identity-validation path uses a single
  USD-only reduction helper (`Currency.UsDollar` → `USD`) with no generic currency table. An
  observed non-USD member stays a definite `FAIL` (`CURRENCY_MISMATCH`), an unreadable
  denomination/product currency stays `INCONCLUSIVE`, and no foreign currency is converted into
  USD. The fixed ES/USD identity and read-only architecture are unchanged. Tests:
  `CompilationRegressionTests`, `test_nt8_compilation_regression.py`.

- **SAMPLE findings reported the wrong severity.** The sample generator hardcoded every non-`FAIL`
  finding to `INFO`, so an `INCONCLUSIVE` finding (which production reports as `MEDIUM`) appeared as
  `INFO` in the committed sample. The generator now copies the real `DiagnosticResult.Severity`,
  matching `DiagnosticEngine`. The sample package and `SHA256SUMS.txt` were regenerated.

- **The AddOn lifecycle was not actually observable by M1 (organization/correctness).** M1 carried a
  `M1.LIFECYCLE` check that read an untyped `RuntimeObservation.LifecycleStates` list which the host
  layer never populated, so the check could only ever report `LIFECYCLE_NOT_OBSERVED` — the lifecycle
  surface was effectively missing from M1. The lifecycle is now a first-class, host-free diagnostic
  owned by M1: the host records every `State` it sees in `OnStateChange` and exposes it through a live
  probe (`new Nt8HostDataProvider(() => HostIsLive, () => LifecycleStates)`), the provider reduces the
  transitions into a dedicated `LifecycleObservation`, and the new host-free
  `Core/Identity/LifecycleVerifier` classifies them into phases (INITIALIZATION / STARTUP / RUNTIME /
  SHUTDOWN) and validates the sequence. M1 now emits `M1.LIFECYCLE` (observed sequence + verdict) and
  `M1.LIFECYCLE_PHASES` (which phases were confirmed). The connection phase is not an AddOn `State`
  member, so it is explicitly deferred to M2 (`connectionPhase=DEFERRED_TO_M2`) rather than duplicated.
  The untyped `RuntimeObservation.LifecycleStates` list was removed so there is a single lifecycle
  surface. Fail-closed: a missing/unobservable surface, a stub/mock value, an unrecognised `State`
  member, or a missing initialization/startup is `INCONCLUSIVE`; a backwards transition or a state
  after `Terminated` is `FAIL`; only an ordered sequence that reached startup is `PASS`. `environment.json`
  now carries a `lifecycle` block (or `UNOBSERVABLE`). Tests: `LifecycleDiagnosticsTests` (verifier,
  M1 probe, host adapter) and the new lifecycle cases in `Phase3ModuleTests` / `Phase4EvidencePackageTests`.
- **The SAMPLE report generator no longer compiled.** `tools/RiskPilot.Diagnostics.SampleReport`
  referenced removed members (`StrategyRiskGateObservation.StrategySurfaceObservable` and
  `AllCandidatesInvalidCount`), so the documentation sample could not be regenerated. It now builds the
  sample rejection distribution from `CandidateRejectionReasons` (the derived surface flag and count
  are computed, not assigned). The sample package was regenerated, including the new M1 lifecycle block.
- **Active orders were reported as zero (misleading diagnostic).** `accounts.json` always carried
  `workingOrderCount: 0`, and the M3 lifecycle observation never distinguished a live order from a
  finished one, so an account with working orders could be reported as having none. The host
  adapter now reads each account's real `Account.Orders` and classifies every `Order.OrderState`
  through the new host-free `Core/Engine/OrderStateClassifier.cs`: `Initialized`/`Submitted`/
  `Accepted`/`Working`/`PartFilled`/`ChangeSubmitted` are **active**; `Filled` is **completed**;
  `Cancelled` is **cancelled**; `Rejected` is **rejected**. A terminal order is never counted as
  active, and `workingOrderCount` is now the real active count. The new `M3.ACTIVE_ORDERS` check
  reports the exact active count with the terminal breakdown as corroborating evidence; with no
  readable `Account.Orders` collection, an unreadable order, or an unclassifiable state it stays
  `INCONCLUSIVE` (fail-closed) rather than publishing a misleading zero. The tool only ever reads
  the order collection — it never submits, changes or cancels an order.
- **M6 reported every real trade print as an unclassified trade (misleading diagnostic).** The
  host adapter copied the Level I `Last` event into a `MarketDataRecord` but never classified its
  direction, so on a real Rithmic/ES feed every observed print was counted `Unknown` and
  `AllDirectionsQuoteDerived` was never true — the M6 direction evidence described the tool's own
  gap, not the host. The new host-free `TradeDirectionClassifier` derives the aggressor side from
  the print price against the best bid/ask carried on the same event (falling back to the last
  observed quote), and the `M6TradeDeltaConsumer` applies it. A derived direction is always
  counted as `DerivedFromQuote`, never provider-confirmed; a print that already carries a
  classification or an explicit unknown reason is counted exactly as reported (never
  re-derived); and a print that cannot be classified keeps its precise cause
  (`NO_BBO`/`BBO_INCOMPLETE`/`BBO_INVALID`/`INSIDE_SPREAD`/`PROVIDER_REPORTED`), with an
  `unclassified` bucket so the unknown total is always fully attributed. A zero or non-finite
  print price is never compared to the BBO (which would make `0 <= bid` a confident, wrong
  `Sell`); it stays unknown and is reported, never silently mis-classified.
- **`M6.SEQUENCE_CONTINUITY` could still pass on a stream whose continuity was never measured
  (fail-open).** NT8's public Level I surface exposes no source sequence, so a zero gap count
  proves nothing. The check now requires an *observed* trade stream **and** an observed source
  sequence before it can `PASS`; with no measured sequence it stays `INCONCLUSIVE`, and a positive
  gap count still `FAIL`s regardless.
- **`M6.TRADEDELTA_COMPLETENESS` passed on a bare caller-supplied string.** No public NinjaTrader
  API exposes TradeDelta completeness, so a `COMPLETE` value that the observed evidence
  contradicts (no observed trade stream, a sequence gap, or an unclassified direction) now `FAIL`s
  instead of passing; when the runtime does not supply a completeness the assembler derives one
  from the observed stream (`COMPLETE`/`INCOMPLETE`) or leaves it `UNOBSERVABLE`. An `INCOMPLETE`
  stays `INCONCLUSIVE`.
- **M6 reset evidence missed the Level II reset that actually invalidates order-flow state.** The
  consumer counted a reset only from the Level I feed; it now counts a Level II depth reset (the
  event that invalidates the derived order-flow/DOM mirror) separately, and `M6.RESET_HANDLING`
  reports both. The M6 observation also carries the weakest source-time basis actually observed on
  the trade stream (never hardcoded) and a `M6.TRADE_STREAM` surface check, so an unobserved
  order-flow surface is explicit rather than implicit.
- **M6 declared the OrderFlow engine readiness without a host source.** No public API exposes the
  OrderFlow readiness, so when it is not supplied the readiness is reported `UNOBSERVABLE` with
  unobservable evidence instead of being presented as an observed value.
- **`M2.ACCOUNT_CONNECTION_ASSOCIATION` did not prove the target account's actual connection.**
  The check compared each account's provider with the provider of *its* reported connection, but
  it never proved that the *target* account (D-095/D-096: `SimRP.BOT`) is bound to the required
  connection (`My Rithmic`) and provider (`Rithmic`); a set of accounts whose providers agreed
  could pass even when the target account was absent or bound to a different connection. The
  check now resolves the target account's **own** `Account.Connection` and compares its name and
  provider (and the account provider) against the required identity, via the new fail-closed
  `TargetAccountConnectionVerifier`. It returns `INCONCLUSIVE` when the account surface is
  unobservable, no account is observed, an account reports no connection, a required field is
  unreadable, or the association is ambiguous; it returns `FAIL` when an observed field
  contradicts the required association (a wrong or differently-cased account/connection name or a
  wrong provider). The generic per-account comparison is retained as
  `M2.ACCOUNT_PROVIDER_AGREEMENT`.
- **The provider probe could inspect the first available connection instead of the target
  account's connection (fail-open).** `Nt8HostDataProvider.GetProvider()`/`BuildProviderEvidence()`
  returned the provider of the first enumerated `Connection.Connections` entry, so an unrelated
  Rithmic connection could be reported as the evidence provider. The provider is now attributed
  to the target account's own connection, or, only when there is no target account, to a single
  unambiguous connection; with multiple connections and no target account it is not attributed at
  all and `M2.PROVIDER_IDENTITY` stays `INCONCLUSIVE` (`PROVIDER_NOT_ATTRIBUTED`). The resolution
  basis is recorded (`TARGET_ACCOUNT_CONNECTION` / `SINGLE_CONNECTION` / `UNAVAILABLE`).
- **Multiple and ambiguous associations were not detected.** The host now enumerates the
  connections and counts how many match the required connection identity; more than one match
  (ambiguous association) or more than one exact account match keeps the association
  `INCONCLUSIVE` rather than silently accepting the first one.
- **The raw connection name was still emitted, contradicting the documented privacy invariant
  (fail-open for identity privacy).** Although the exact-identity check reduced its own result to
  booleans/counts, the configured connection name still reached a finding and two artifacts:
  `M2.ACCOUNT_PROVIDER_AGREEMENT` wrote `connection=<raw name>` into its evidence, `connections.json`
  wrote the name as `name`, and `accounts.json` wrote it as `associations[].connectionName`. The
  name is now reduced to a non-reversible `SyntheticRef` token (`connection#<hash>`): the finding
  emits `connection=connection#<hash>`, `connections.json` carries `connectionRef`, and
  `accounts.json` carries `associations[].connectionRef`. No raw account or connection name now
  reaches a finding, log, report or artifact, matching the documented invariant.

### Added

- **M8 now owns and executes the read-only security scan** (`M8.SECURITY_SCAN`, replacing the
  out-of-band `M8.STATIC_SAFETY_SCAN`). The scan logic lives host-free in
  `Core/Security/SecurityScanner.cs` + `Core/Security/SecurityScanRules.cs` and runs inside M8
  against a strictly read-only source (`Core/Contract/ISecurityScanSource.cs`, host adapter
  `Host/FileSystemSecurityScanSource.cs`). It preserves every rule the offline
  `verification/addon-smoke` scan applied, so no check is lost and no module executes it twice.
  The previous injected-boolean API (`DiagnosticRuntime.StaticSafetyScan` / `SafetyScanEvidence`)
  was removed; the single scan result is published to `SessionContext.SecurityScan` and merged by
  `SnapshotAssembler` so the live probe and the exported `safety-audit.json` agree without a
  second run. Fail-closed: an unavailable/unreadable source or an unreadable file ⇒ `INCONCLUSIVE`
  (never `PASS`); a forbidden hit / leaked identity literal / missing trading-disabled declaration
  ⇒ `FAIL`. Identity literals are drawn from `ExpectedAccountIdentity`, never hardcoded in the
  scanner. `safety-audit.json` now carries a `securityScan` block.
- `TargetAccountConnectionObservation` (host-free; booleans/counts + synthetic account ref only)
  and `TargetAccountConnectionVerifier`, which decide the target account→connection→provider
  association with no raw identity in the result.
- **M6 data-reliability test suite** (`tests/.../M6TradeDeltaTests.cs`) covering the direction
  classifier (buy/sell/inside-spread/crossed-book/one-sided/no-quote/sentinel prices), the
  consumer (event-BBO and last-quote classification, host-supplied preservation, sequence
  observation, weakest source-time basis, per-feed resets), the assembler derivations, the probe's
  fail-closed semantics (unobserved stream, unmeasured sequence, corroborated vs contradicted
  completeness), the host capture adapter over the offline stub surface, and runtime integration
  through to the exported `TradeDelta` evidence.
- New M6 fields in `orderflow-summary.json`: `tradeStreamObservable`, `sequenceObservable`,
  `unknownUnclassified`, `sourceTimeBasis`, and the per-feed reset counts
  (`marketDataResetCount`, `depthResetCount`).
- A privacy-preserving `connectionAssociation` block in `accounts.json` (status, reason codes,
  field-level detail, counts and a synthetic account ref; no raw account or connection name).
- Regression tests: `TargetAccountConnectionTests` (verifier + M2 probe: exact match, wrong
  account, missing account, wrong/unrelated connection, wrong provider, multiple connections,
  ambiguous association, missing surface, no raw-identity leak) and
  `HostTargetAccountConnectionTests` (host adapter over the offline stub surface, including the
  "provider is not the first connection" and "multiple connections without a target account"
  cases).

### Changed

- **M1 owns the lifecycle surface.** The M1 module title is now "Environment, API & Lifecycle
  Compatibility" (catalog, probe and SAMPLE report agree), the AddOn lifecycle observation moved from
  the untyped `RuntimeObservation.LifecycleStates` list to the dedicated
  `DiagnosticSnapshot.Lifecycle` / `LifecycleObservation`, and the new `M1.LIFECYCLE_PHASES` check
  reports the confirmed phases. The connection phase is explicitly deferred to M2.

## [1.0.0] — Phase 5 final audit & release

Phase 5 is an independent end-to-end audit of everything built in Phases 1–4. It found and fixed
several real defects, added a Phase-5 acceptance-scenario test suite, added a clearly-labelled
sample evidence package, and produced the release documentation.

### Fixed

- **Secret redactor corrupted legitimate diagnostic counters (data integrity).** The
  "account number" rule masked *every* 6+ digit run, including the tool's own counters, so a real
  observation such as `ALL_CANDIDATES_INVALID=311179` or an epoch was rewritten to
  `***REDACTED***` in findings, CSV and JSON. The rule is now context-aware: a digit run that is
  the value of a structured `key=value` / `key:value` field is preserved (it is evidence), while a
  free-text run (`acct 12345678`) and any run whose key looks like an account field
  (`account=12345678`) are still masked.
- **`M5.DATA_INTEGRITY` and `M6.SEQUENCE_CONTINUITY` could never fail (fail-open).** Both checks
  read `SequenceGapCount` from observations whose field was never populated, so they always
  returned `PASS` even when the ingest path had detected a gap. Per-domain sequence-gap and drop
  counters are now attributed by stream on the ingest path
  (`md.sequenceGaps`, `td.sequenceGaps`, `md.droppedEvents`) and merged into the DOM and TradeDelta
  observations, so a real gap fails the affected domain and the streams are never merged. In
  addition, an *unobserved* stream no longer yields a `PASS`: with no observed stream the check
  stays `INCONCLUSIVE`, while a positive count still fails regardless.
- **`M9.MISSING_OR_DUPLICATE` passed on an unobserved audit surface (fail-open).** With no recorded
  order/execution events all counts are zero and the check returned `PASS`, contradicting its own
  contract ("missing or unmatched events are reported as INCONCLUSIVE, never as PASS"). It now
  stays `INCONCLUSIVE` until the audit surface is observable; a positive count still fails.
- **`M8.EVIDENCE_INTEGRITY` passed on an empty session (fail-open).** A session that ingested no
  records at all has all-zero drop/gap counters and returned `PASS`. It now stays `INCONCLUSIVE`
  until at least one record has been ingested; a positive counter still fails.
- **`M2.ACCOUNT_CONNECTION_ASSOCIATION` passed with no observable association (fail-open).** When
  accounts were present but none reported a connection (`withConnection == 0`) the check returned
  `PASS`. It now stays `INCONCLUSIVE` until at least one account has an observed connection; a
  provider mismatch still fails.
- **The evidence write path did not enforce the documented invariants.** `WriteEvidencePackage`
  verified the hash manifest only; it now also refuses to write unless `HasCompleteArtifactSet`,
  `IsSecretFree` and `ManifestIsIdentitySafe` all pass, so a package that leaks a credential-like
  token, carries a raw identity, or is missing an artifact is never written.
- **`M3.CORRELATION` evidence timestamps used the ambient culture.** `DateTime.ToString("O")`
  without `CultureInfo.InvariantCulture` can emit a non-ISO form under some locales; it is now
  invariant, matching every other timestamp in the package. Instrument expiry and the
  evidence-directory timestamp are now invariant too.
- **Flaky runtime integration tests.** Three tests used a fixed `Thread.Sleep(50)` to wait for the
  drain thread; they now wait deterministically on the session's processed count.

### Added

- **Phase-5 acceptance-scenario test suite** (`tests/.../Phase5ScenarioTests.cs`) covering the
  mandatory review scenarios: provider Rithmic vs. unknown provider, connection lost/reconnected,
  account without orders, whitespace order id, incomplete order lifecycle, unmatched execution,
  incomplete instrument metadata, missing D-078 approval, DOM reset / out-of-bounds / snapshot
  mismatch, unknown trade direction, incomplete TradeDelta, OrderFlow unavailable, missing strategy
  capability, queue overflow, shutdown mid-capture, report integrity, and trading disabled.
- **Evidence provenance marker.** Every evidence package now carries an `evidenceProvenance` field
  (`RUNTIME` or `SAMPLE`) in `manifest.json`, a `Provenance` row in `summary.html`, and a visible
  banner when the data is a sample. This separates real runtime evidence from a mock/test artifact.
- **Reproducible sample report generator** (`tools/RiskPilot.Diagnostics.SampleReport`) that builds
  a clearly-labelled sample package with the real production `EvidencePackageBuilder`.
- **A committed sample evidence package** at `docs/samples/sample-evidence-package/` (synthetic;
  labelled `SAMPLE`). Its values are deliberately distinct from the historical 11 Nov 2026 baseline
  so the sample can never be mistaken for that recorded run.
- Release documentation: `docs/INSTALL.md`, `docs/USAGE.md`, `docs/FINAL_AUDIT_AND_HANDOFF.md`,
  and a SHA-256 manifest (`docs/samples/sample-evidence-package/SHA256SUMS.txt`).

### Changed

- Product version is now `1.0.0` (was `1.0.0-phase2`).
- Added a "tools" solution folder and the sample-report project to the solution.

### Safety

- No change to the read-only posture. Trading remains disabled; there is still no code path that
  sends, modifies or cancels an order, mutates an account/connection/position, or changes a
  RiskPilot setting. The static safety scan passes over 103 production source files and 34
  forbidden patterns.

## [0.4.0] — Phase 4 UI, evidence reporting & tests

- Added the three diagnostic modes (Quick / Full / Post-Trade Audit) and the host-free run
  coordinator (single-run guarantee, cancel, `IUiDispatcher` marshalling).
- Added the 17-artifact evidence package builder with SHA-256 manifest and integrity verification.
- Added M9 post-trade audit and the WPF Control Center window.
- See `docs/PHASE4_UI_AND_EVIDENCE.md`.

## [0.3.0] — Phase 3 diagnostic modules

- Added the eight read-only probes M1–M8 and their observation consumers.
- See `docs/PHASE3_DIAGNOSTIC_MODULES.md`.

## [0.2.0] — Phase 2 foundation & diagnostic core

- Added the standalone solution/project, normalized data model, bounded event queue,
  session/logging/manifest, and M1/M2 probes.
- See `docs/PHASE2_IMPLEMENTATION.md`.

## [0.1.0] — Phase 1 architecture & audit

- Repository audit and architecture record.
- See `docs/PHASE1_ARCHITECTURE_AND_AUDIT.md`.
