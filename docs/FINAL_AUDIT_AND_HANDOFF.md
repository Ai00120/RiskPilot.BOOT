# Phase 5 — Final Independent Audit & Handoff

**Product:** RiskPilot NT8 Diagnostic Center (standalone read-only NinjaTrader 8 AddOn)
**Namespace / project:** `RiskPilot.Diagnostics.Nt8`
**Repository:** `Ai00120/RiskPilot.BOOT` (this repo)
**Reference (read-only, never modified):** `Ai00120/RiskPilot.BOT`
**Audit date:** 2026-11-12
**Version:** 1.0.0

> This audit was performed independently and did not assume the earlier phases were correct.
> It re-read the source, re-ran the suites, and probed for fail-open behaviour. Four real
> defects were found and fixed. The fixes are described below with the reasoning.

---

## 1. Executive summary

The AddOn is a standalone, read-only diagnostic tool. It has **no** execution dependency on
RiskPilot.BOT. Across Phases 1–5 it comprises ~14,000 lines of production C# in the `Core`
(host-free) and `Host` (NinjaTrader-only) layers, plus 238 unit/integration tests and a static
safety scan.

The Phase 5 audit found and fixed **seven genuine defects** — several of which could have let the
tool report a misleading result or corrupt its own evidence:

1. **Secret redactor corrupted real evidence.** A blanket 6+ digit mask rewrote legitimate
   diagnostic counters (e.g. `ALL_CANDIDATES_INVALID=311179`) to `***REDACTED***`.
2. **Two integrity checks were fail-open.** `M5.DATA_INTEGRITY` and `M6.SEQUENCE_CONTINUITY`
   read a field that was never populated, so they always returned `PASS`; and even after wiring
   the field, an *unobserved* stream still produced a `PASS` on zero data.
3. **A second fail-open pair.** `M9.MISSING_OR_DUPLICATE` returned `PASS` on a completely
   unobserved audit surface, and `M8.EVIDENCE_INTEGRITY` returned `PASS` on a session that
   ingested nothing.
4. **The evidence write path did not enforce the documented secret-free / complete-set
   invariants** — only the hash manifest was checked.
5. **Culture-sensitive formatting** in correlation evidence, instrument expiry and the sink
   timestamp could emit non-ISO or non-Gregorian values under some locales.

All 238 tests pass, the static safety scan passes over 103 production files and 34 forbidden
patterns, and the stub build succeeds. **A real NinjaTrader-host build was NOT verified** because
the NinjaTrader assemblies are not present in this environment (see §5).

### The three acceptance results (reported separately, never merged)

| # | Subject | Result |
| --- | --- | --- |
| 1 | **AddOn implementation / build quality** | Source compiles against stubs; 238 tests green; static scan green. Real-host build **NOT verified** (no NT8 DLLs). Overall: **INCONCLUSIVE** pending a real-host build. |
| 2 | **Runtime diagnostic coverage / evidence quality** | Strong, and improved by this audit (real per-domain gap/drop evidence; fail-closed integrity; provenance labelling). No real host run was possible here, so no runtime evidence is claimed. Overall: **INCONCLUSIVE** (no host). |
| 3 | **RiskPilot.BOT acceptance** | **NOT_APPLICABLE.** This tool is independent of RiskPilot.BOT and cannot certify it. A green tool build or diagnostic run is NOT a RiskPilot.BOT acceptance. |

---

## 2. What was audited

### 2.1 Architecture and code

| Area | Finding |
| --- | --- |
| AddOn independence | `RiskPilot.Diagnostics.Nt8` references only NinjaTrader assemblies and the BCL. No reference to `RiskPilot.BOT` in the solution, project or source. **PASS.** |
| Namespace / project correctness | Namespace root `RiskPilot.Diagnostics.Nt8` matches the assembly and folder layout. `EnableDefaultCompileItems=false`; every file is explicitly listed. **PASS.** |
| Layer separation | `Core/` contains **no** NinjaTrader types; `Host/` is the only layer that touches them; `Stubs/` compiles only under `USE_STUBS`. Enforced by the static scan. **PASS.** |
| Documented APIs only | No reflection, `unsafe`, P/Invoke, or undocumented members. The 34-pattern static scan and a manual review confirm it. Provider identity is resolved by `ConnectOptions.Provider.ToString()` (no `Provider.Rithmic` enum assumption). **PASS.** |
| Thread safety | `EventQueue` is lock-free (`Interlocked` + `ConcurrentQueue`); `SequenceGapDetector` uses a CAS loop; `DuplicateEventDetector` is single-threaded by contract on the callback thread. Shared counters use `Interlocked`. **PASS** (see §3.2 for the one subtlety noted). |
| Memory management | Bounded queue with a fixed capacity; the drain loop reuses a fixed buffer and nulls slots after processing; no unbounded collection grows with event count except the bounded duplicate window. **PASS.** |
| Lifecycle | `SetDefaults` → `Active` → `OnWindowCreated` wiring; single-run guarantee in `DiagnosticRunCoordinator`; UI notifications marshalled through `IUiDispatcher` (async post only). **PASS.** |
| Event queue / backpressure | Enqueue never blocks the host callback thread: at capacity the item is dropped and counted, and the drop is attributed to its data domain. **PASS.** |
| Shutdown | The drain loop performs a final drain so queued records are never silently abandoned; `Dispatcher` shutdown is checked via both `HasShutdownStarted` and `HasShutdownFinished`. **PASS.** |
| Exception handling | Host reads are wrapped in `Safe*` helpers; the evidence write path contains all exceptions and reports through the log. **PASS.** |

### 2.2 Safety (proved by code path + static scan)

The tool is read-only. By code-path inspection and the static safety scan:

- **No order is sent.** There is no call to any order-submission API anywhere in the source.
- **No order is modified or cancelled.** No change/cancel API is referenced.
- **No position is closed or altered.** Positions are read only.
- **No account or connection is mutated.** `Account.All` and `Connection.Connections` are read
  under a lock; nothing is written back.
- **No RiskPilot setting is changed.** No settings surface is touched.
- **Trading is never enabled.** There is no code path that enables it; `Trading=Disabled` is
  reported as runtime evidence and is asserted by tests.
- **No reflection or private API.** Confirmed by scan and review.

The static safety scan (`verification/addon-smoke/test_diagnostic_safety.py`) enforces these as
offline invariants over 103 production source files with 34 forbidden patterns. **PASS.**

### 2.3 Data validity

- No fabricated data: a member the host could not read stays null/empty and is reported
  `UNAVAILABLE`/`INCONCLUSIVE` with a reason.
- Order / execution / position attribution: order id is classified (Null / Empty / Whitespace /
  Valid / Unavailable) and never silently coerced; unmatched executions and unmatched terminal
  orders are counted, not assumed away.
- NULL/EMPTY/WHITESPACE order ids: classified and counted.
- Incomplete / duplicate events: `DuplicateEventDetector` suppresses duplicates by identity and
  counts them; incomplete lifecycles are reported, not completed.
- Sequence gaps: detected **per stream** (never merged), counted, and attributed to the data
  domain (market-data/DOM vs. order-flow).
- Timestamp provenance: source timestamps carry a `SourceTimeBasis`; an implausible future
  timestamp is counted, never trusted; the summary `sourceTimeBasis` is derived as the weakest
  observed basis and is `UNOBSERVABLE` with no records.
- Provider identity: resolved by string comparison; unknown provider → `INCONCLUSIVE`.
- Instrument mapping: `MasterInstrument.*` is read only after `DataLoaded`/`Realtime`.
- DOM snapshot correlation, OrderFlow/TradeDelta validity, status propagation: each has a
  dedicated probe and tests.

### 2.4 Reporting

- HTML/CSV/JSON are produced by dedicated writers; every string is passed through
  `SecretRedactor`; account references are synthetic `account#<hash>` tokens.
- Manifest: session, UTC times, versions, provider, instrument, masked accounts, record/dropped
  counts, truncation, **provenance**, every file + SHA-256, aggregate status.
- SHA-256: `VerifyIntegrity` checks every artifact's presence, hash and byte length.
- Missing data is never reported as `PASS`.
- **Runtime vs. mock/test evidence is now explicitly separated** by the `evidenceProvenance`
  field and a visible banner (see §4.4).

### 2.5 Real-host compatibility

See §5. Not verified in this environment; the stub build is not presented as a real build.

---

## 3. Defects found and fixed

### 3.1 Secret redactor corrupted legitimate diagnostic counters

**Severity: high (evidence integrity).** The "account number" rule masked *every* 6+ digit run,
including the tool's own counters and epochs. A real observation such as
`ALL_CANDIDATES_INVALID=311179` or `sync=265 failure=1` was rewritten to `***REDACTED***` in
findings, CSV and JSON — the evidence was destroyed by the very filter meant to protect it.

**Fix.** The rule is now context-aware. A digit run that is the value of a structured
`key=value` / `"key":value` field is preserved (it is evidence). A free-text run
(`acct 12345678`) is still masked, and any run whose key looks like an account field
(`account=12345678`, `"accountRef":12345678`) is masked regardless. Credential keywords were
extended (`passwd`, `passcode`, `pwd`, `pin`, `otp`, `credential`). Regression tests cover both
directions.

### 3.2 Integrity checks were fail-open (the most important finding)

**Severity: high (fail-closed violation).** `M5.DATA_INTEGRITY` and `M6.SEQUENCE_CONTINUITY`
read `SequenceGapCount` from observations whose field was **never populated** by the host, so the
checks could never fail: they always returned `PASS`, even when the ingest path had detected a
gap. A missing observation was being reported as a clean pass — exactly what the repository
forbids.

**Fix (two parts).**

1. **Wire real evidence.** Per-domain sequence-gap and drop counters were added
   (`md.sequenceGaps`, `td.sequenceGaps`, `md.droppedEvents`), populated on the ingest path by
   stream key, and surfaced into the DOM and TradeDelta observations by `SnapshotAssembler`. The
   two data domains are never merged.
2. **Fail closed on absent data.** A positive gap/drop count fails regardless (it is itself
   evidence of a problem). A **zero** count only passes once the relevant stream was actually
   observed; with no observed stream the check is `INCONCLUSIVE`, not `PASS`.

Tests assert all three behaviours for both modules (inconclusive-when-unobserved,
fail-on-gap-even-when-unobserved, pass-only-when-observed-and-clean).

### 3.3 A second fail-open pair (M8 and M9)

**Severity: high (fail-closed violation).** The same defect class appeared twice more:

- `M9.MISSING_OR_DUPLICATE` returned `PASS` when the recorded order/execution surface was never
  observed. With no events, all counts are zero, so the "clean" branch fired — directly
  contradicting the probe's own documented contract ("missing or unmatched events are reported as
  INCONCLUSIVE, never as PASS").
- `M8.EVIDENCE_INTEGRITY` returned `PASS` for a session that ingested nothing at all. With no
  records, the drop/gap/timestamp counters are all zero, so an empty session looked "clean".

**Fix.** Both checks now require positive evidence that the relevant surface was observed before
they may `PASS`: `M9.MISSING_OR_DUPLICATE` requires `AuditSurfaceObservable`, and
`M8.EVIDENCE_INTEGRITY` requires `EnqueuedCount > 0`. A positive count still fails regardless, so
a genuine problem detected on a partially-observed surface is never suppressed. Regression tests
cover all three behaviours for both checks.

### 3.4 Evidence write path did not enforce the documented invariants

**Severity: medium (defense in depth).** `WriteEvidencePackage` verified the hash manifest but did
not check the two other invariants the class documents: that the package is secret-free and that
the manifest carries no raw identity. A package that leaked a token, or was missing an artifact,
could still be written.

**Fix.** The write path now refuses to write a package unless it passes `VerifyIntegrity`,
`HasCompleteArtifactSet`, `IsSecretFree` and `ManifestIsIdentitySafe`. Failures are logged and no
partial package is written. The sample generator runs the same four guards.

### 3.5 Culture-sensitive formatting

**Severity: low (correctness under non-default locales).** `DateTime.ToString("O")` in the M3
correlation evidence, `DateTime.ToString("yyyy-MM-dd")` for instrument expiry, and the sink's
`yyyyMMdd_HHmmss` timestamp used the ambient culture. Under a non-Gregorian calendar the year
could differ; under a non-ISO culture the separator could change. All three now use
`CultureInfo.InvariantCulture`.

### 3.6 Flaky test (not a product defect)

Three runtime-integration tests used a fixed `Thread.Sleep(50)` to wait for the drain thread and
could race under load. They now wait deterministically on the session's processed count.

---

## 4. Additions

### 4.1 Phase-5 acceptance-scenario tests

`tests/RiskPilot.Diagnostics.Nt8.Tests/Phase5ScenarioTests.cs` covers the mandatory review
scenarios: provider Rithmic and unknown provider; connection lost/reconnected; account without
orders; invalid (whitespace) order id; incomplete order lifecycle; unmatched execution;
incomplete instrument metadata; missing D-078 approval; DOM reset; DOM out-of-bounds; snapshot
mismatch; unknown trade direction; incomplete TradeDelta; OrderFlow unavailable; missing strategy
capability; queue overflow; shutdown mid-capture; report integrity; and trading disabled.

### 4.2 Sample evidence package

`tools/RiskPilot.Diagnostics.SampleReport` builds a complete 17-artifact package using the real
production `EvidencePackageBuilder`, so the package shape and SHA-256 manifest match a real run.
The committed sample is at `docs/samples/sample-evidence-package/` and is **labelled `SAMPLE`**.

### 4.3 Documentation

`docs/INSTALL.md`, `docs/USAGE.md`, this document, `CHANGELOG.md`, and a SHA-256 manifest
(`docs/samples/sample-evidence-package/SHA256SUMS.txt`).

### 4.4 Evidence provenance marker

Every package now carries `evidenceProvenance` (`RUNTIME` or `SAMPLE`) in `manifest.json`, a
`Provenance` row in `summary.html`, and a visible banner for `SAMPLE`. This makes it impossible to
mistake a mock/test artifact for a runtime result.

---

## 5. Real NinjaTrader build — exact, reproducible result

**Not verified in this environment.** The NinjaTrader 8 assemblies are not installed here, so the
`UseStubs=false` build cannot resolve them.

Command and result (reproducible on this machine):

```
$ dotnet build src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=false -c Release
Build FAILED.
error CS0246: The type or namespace name 'NinjaTrader' could not be found ...
error CS0246: The type or namespace name 'AddOnBase' could not be found ...
error CS0246: The type or namespace name 'NTWindow' could not be found ...
```

The failure is purely the absent assemblies. To complete the real build, the following files must
be present at the paths the project already references (adjust `HintPath` if NT8 is installed
elsewhere):

| Assembly | Expected path |
| --- | --- |
| `NinjaTrader.Core.dll` | `C:\Program Files\NinjaTrader 8\bin\` |
| `NinjaTrader.Cbi.dll` | `C:\Program Files\NinjaTrader 8\bin\` |
| `NinjaTrader.Data.dll` | `C:\Program Files\NinjaTrader 8\bin\` |
| `NinjaTrader.NinjaScript.dll` | `C:\Program Files\NinjaTrader 8\bin\` |
| `NinjaTrader.Gui.dll` | `C:\Program Files\NinjaTrader 8\bin\` |

Target: NinjaTrader **8.1.8.4 (64-bit)** on Windows with the .NET Framework 4.8 targeting pack.
The Cbi/Data/NinjaScript references were added in this audit so the `UseStubs=false` configuration
is complete; the stub build is unaffected.

**The stub build (`UseStubs=true`) is NOT a real build and is not presented as one.**

---

## 6. Test and verification results

| Check | Command | Result |
| --- | --- | --- |
| Stub build | `dotnet build ... -p:UseStubs=true -c Release` | **succeeded** |
| Solution build (incl. tool) | `dotnet build RiskPilot.Diagnostics.Nt8.sln -c Release` | **succeeded** |
| Unit + integration tests | `dotnet test ... -c Release` | **238 passed / 0 failed** |
| Static safety scan | `python3 verification/addon-smoke/test_diagnostic_safety.py` | **PASS** (103 files, 34 patterns) |
| Real-host build | `dotnet build ... -p:UseStubs=false -c Release` | **FAILED** — NT8 DLLs absent (see §5) |

Test files (20) and their focus include queue/backpressure (`EventQueueTests`,
`ThreadSafetyTests`), duplicate/gap detection (`DuplicateEventTests`, `SequenceGapTests`),
lifecycle and isolation (`LifecycleTests`, `SessionIsolationTests`), status propagation and
timestamp provenance (`StatusPropagationTests`, `TimestampTests`), redaction
(`SecretRedactionTests`), the eight probes (`Phase3ModuleTests`, `ProbeTests`), the evidence
package and post-trade audit (`Phase4EvidencePackageTests`, `Phase4PostTradeAuditTests`), the run
coordinator and runtime integration (`Phase4RunCoordinatorTests`,
`Phase4RuntimeIntegrationTests`), and the Phase-5 scenarios (`Phase5ScenarioTests`).

---

## 7. Historical baseline (kept as a historical sample — NOT a new tool run)

The baseline recorded on 11 November 2026 is preserved as a historical sample. It is **not** a
result of running this tool in this environment, and no definitive root cause is asserted for it.

- Provider: Rithmic
- Instrument: ES 12-26
- 270,979 market-data records, 0 dropped, 0 sequence gaps
- Source timestamps: `SERVER_TIME_UNVERIFIED`
- 265 DOM snapshot sync, 264 `UpdateOutOfBounds`, 1 `InsertOutOfBounds`, 1 snapshot sync failure
- Native DOM: 64 Bid / 64 Ask; Internal DOM: 10 Bid / 6 Ask
- Instrument Attestation Failed: `HOST_UNAVAILABLE` and `D078_FIELD_APPROVAL_NOT_APPROVED`
- `ALL_CANDIDATES_INVALID`: 311,179
- Trading = Disabled

---

## 8. API limitations (unobservable from the public surface)

- The exact NinjaTrader host version and the provider build are not exposed by any public API;
  they are reported `UNOBSERVABLE`.
- Trade aggressor side is not derivable from public Level I data; a quote-derived direction is
  never presented as provider-confirmed.
- `Instrument.MasterInstrument.*` is only valid after `DataLoaded`/`Realtime`.
- Provider identity is resolved by string comparison (no guaranteed `Provider.Rithmic` enum).
- The post-trade audit surface comes from the host's already-read `Account.Orders` snapshot plus
  the ingest collector; the tool subscribes to no host event feed.

---

## 9. Items that still require manual verification (on a Windows host)

1. Complete the real-host build (§5) and record the exact result.
2. Run Quick / Full / Post-Trade Audit against a live Rithmic connection and export a package.
3. Confirm the window opens from the Control Center menu and that Cancel behaves as documented.
4. Confirm the evidence directory is created outside the Git tree and contains 17 artifacts.
5. Confirm `Trading=Disabled` is reported and that no account/connection/position changes occur.

---

## 10. Handoff

- **Source:** `src/RiskPilot.Diagnostics.Nt8/` (AddOn), `tests/…` (tests),
  `tools/RiskPilot.Diagnostics.SampleReport/` (sample generator).
- **Solution / project:** `RiskPilot.Diagnostics.Nt8.sln` and the AddOn `.csproj`.
- **Install guide:** `docs/INSTALL.md`. **User guide:** `docs/USAGE.md`.
- **Sample report (SAMPLE-labelled):** `docs/samples/sample-evidence-package/`, with
  `SHA256SUMS.txt`.
- **Changelog:** `CHANGELOG.md`.
- **Version:** `1.0.0`; release tag `v1.0.0`.

### Installation (short form)

1. Build against the real host on Windows (`-p:UseStubs=false -c Release`).
2. Copy `RiskPilot.Diagnostics.Nt8.dll` into
   `%USERPROFILE%\Documents\NinjaTrader 8\bin\Custom\AddOns\` (or compile the sources in the
   NinjaScript Editor).
3. Restart NinjaTrader and open **RiskPilot NT8 Diagnostic Center** from the Control Center menu.
