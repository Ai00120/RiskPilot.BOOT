# Changelog

All notable changes to the RiskPilot NT8 Diagnostic Center (`RiskPilot.Diagnostics.Nt8`) are
recorded here. This repository is independent of `Ai00120/RiskPilot.BOT` and has no execution
dependency on it.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed

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

- `TargetAccountConnectionObservation` (host-free; booleans/counts + synthetic account ref only)
  and `TargetAccountConnectionVerifier`, which decide the target account→connection→provider
  association with no raw identity in the result.
- A privacy-preserving `connectionAssociation` block in `accounts.json` (status, reason codes,
  field-level detail, counts and a synthetic account ref; no raw account or connection name).
- Regression tests: `TargetAccountConnectionTests` (verifier + M2 probe: exact match, wrong
  account, missing account, wrong/unrelated connection, wrong provider, multiple connections,
  ambiguous association, missing surface, no raw-identity leak) and
  `HostTargetAccountConnectionTests` (host adapter over the offline stub surface, including the
  "provider is not the first connection" and "multiple connections without a target account"
  cases).

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
