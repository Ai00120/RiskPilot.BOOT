# User Guide — RiskPilot NT8 Diagnostic Center

This guide explains how to run the read-only diagnostics and how to read the report. See
`docs/INSTALL.md` for installation.

## What the tool does

The Diagnostic Center inspects the running NinjaTrader 8 host through its **documented public API
only** and reports what it can observe. It never changes anything: no order is sent, modified or
cancelled, no position is opened or closed, no account, connection or RiskPilot setting is
modified, and trading is never enabled.

A check that cannot be positively observed is reported as `INCONCLUSIVE` or `NOT_APPLICABLE` — never
as `PASS`. Missing data is always reported with a reason.

## Opening the window

NinjaTrader **Control Center → main menu → RiskPilot NT8 Diagnostic Center**.

## The three modes

| Mode | What it runs | Modules |
| --- | --- | --- |
| **Quick Diagnostic** | Fast environment, connection, account, instrument, market-data and safety checks | M1, M2, M3, M4, M5, M8 |
| **Full Diagnostic** | Every module, including DOM, order-flow and strategies | M1–M8 |
| **Post-Trade Audit** | Read-only analysis of already-recorded order / execution / position events | M9 |

A run executes on a background thread, so the Control Center never freezes. Only one run can be
active at a time; starting a second run while one is active is refused. **Cancel** stops the
diagnostic processing only — it never touches a trade, and a cancelled run writes no evidence
package.

## Result states

| State | Meaning |
| --- | --- |
| `PASS` | The check was positively observed to be good. |
| `FAIL` | A real problem was observed (for example a data gap or a missing approval). |
| `INCONCLUSIVE` | The check could not be decided from what was observable. |
| `NOT_APPLICABLE` | The check does not apply to the current host state. |

`INCONCLUSIVE` is a normal, honest outcome. With no connection, for example, provider identity is
`INCONCLUSIVE`, because it genuinely cannot be proven.

## The modules

| Module | Focus |
| --- | --- |
| **M1** | Environment, API & lifecycle compatibility (host version is unobservable — no public API; the declared target is NinjaTrader 8.1.9.0, minimum supported 8.1.8.4, and it is not verified at runtime). Sole owner of the AddOn lifecycle surface; the connection phase is deferred to M2. |
| **M2** | Connection & provider identity (price feed and order channel are tracked separately). |
| **M3** | Account & order lifecycle (F-05): order-id classification, correlation, fill continuity. |
| **M4** | Instrument attestation / D-078 field approval. |
| **M5** | DOM & market data: snapshot sync, out-of-bounds updates, data integrity. |
| **M6** | TradeDelta / OrderFlow: trade-stream observability, direction provenance, completeness, readiness. |
| **M7** | Strategies / risk gate: strategy surface and candidate rejection. BOOT ships no strategy producer, so M7 reports `INCONCLUSIVE` until an external read-only source is supplied (see `docs/M7_STRATEGY_INTEGRATION.md`). |
| **M8** | Safety / acceptance: trading-disabled evidence, read-only posture, static safety scan. |
| **M9** | Post-trade audit: order/execution/position surface and correlation timing. |

## Host readiness semantics

`M1.HOST_READINESS` is not a single Boolean. It reports **nine independent facts**, each with its
own three-valued state (`SATISFIED` / `UNSATISFIED` / `UNKNOWN`), and is `PASS` only when every fact
holds. Reaching `Active` never implies the metadata was read; a resolved instrument never implies
market data arrived; a successful subscription never implies events; an account in `Account.All`
never implies verification; a connected provider never implies a verified target-account
association.

| Fact | Proven by |
| --- | --- |
| `hostOperational` | the AddOn reached its operational lifecycle state (`Active`) |
| `hostAvailable` | the NinjaTrader host object model is reachable |
| `metadataReadAllowed` | the lifecycle permits attempting the metadata read |
| `instrumentResolved` | the **explicitly selected** target instrument resolved uniquely |
| `metadataObserved` | the required metadata was actually read from the host adapter |
| `accountVerified` | the target account's identity evidence was verified (M2) |
| `connectionVerified` | the account→connection association and connection state were verified (M2) |
| `marketDataSubscribed` | the Level I/II subscriptions were established |
| `marketDataObserved` | actual market-data events were received |

The overall state is one of `NOT_OBSERVED`, `ADDON_NOT_OPERATIONAL`, `ADDON_SHUTTING_DOWN`,
`AWAITING_HOST`, `AWAITING_METADATA_READ`, `AWAITING_INSTRUMENT`, `AWAITING_METADATA`,
`AWAITING_ACCOUNT`, `AWAITING_CONNECTION`, `AWAITING_SUBSCRIPTION`, `AWAITING_MARKET_DATA` or
`READY`. A restart begins a new lifecycle **epoch** that discards every fact observed before
termination, and a reconnect invalidates the connection/stream facts, so readiness is never
`PASS` on stale or invalidated evidence. The same facts are exported in `environment.json` under
`hostReadiness`.

## Verifying against a real host

The step-by-step BOOT-only NinjaTrader acceptance procedure is in
`docs/NT8_ACCEPTANCE_RUNBOOK.md`. It covers AddOn load, lifecycle, explicit instrument selection,
identity/venue, multiple contracts, account and connection association, provider identity,
LIVE/SIM classification, Level I/II observation, reconnect, shutdown/restart, evidence export and
fail-closed behavior. It never asks the operator to submit an order or enable trading.

## Exporting the evidence package

**Export** writes the evidence package for the last completed run. Each run gets its own directory:

```
%USERPROFILE%\Documents\RiskPilot.Diagnostics.Evidence\<session-id>\
```

The package contains 18 artifacts:

```
summary.html  full-report.html  acceptance-matrix.csv
environment.json  connections.json  accounts.json  instruments.json
orders.csv  executions.csv  positions.csv
market-data-summary.json  dom-summary.json  orderflow-summary.json
strategies.json  safety-audit.json  stage34-coverage.json  findings.json  manifest.json
```

- `summary.html` — the human-readable one-page summary.
- `full-report.html` — the complete report with every check and its evidence.
- `stage34-coverage.json` — the Stage 3 / Stage 4 traceability matrix: each RiskPilot.BOT Stage 3/4
  requirement mapped to the BOOT module(s) and check(s) that provide evidence, the host data and
  verification method, the evidence class (runtime vs. source-level), the verification scope, and
  the coverage status **derived from this run** (`VERIFIED` / `FAILED` / `INCONCLUSIVE` /
  `NOT_EXECUTED` / `SOURCE_ONLY`). The requirement text is defined in
  `Core/Run/Stage34Coverage.cs`; the exported artifact re-evaluates it from the run's own results.
  See `docs/STAGE34_COVERAGE.md` for the readable matrix.
- `manifest.json` — session, start/end UTC, versions, provider, instrument, **masked** accounts,
  record and dropped counts, truncation status, `evidenceProvenance`, every file with its SHA-256,
  and the overall run status.
- `environment.json` — the observed environment plus a `ninjaTraderCompatibility` block that
  separates the declared **target** version, the **minimum supported** version and whether any host
  build was **verified at runtime**. The installed host build is not exposed by any public API, so
  the tool never presents the target as verified; the block reports `verifiedAtRuntime: false`
  until a real-host run is recorded.

Only data that was actually observable is exported. A section whose source could not be read is
written with `status=UNAVAILABLE` and a `reason`, never fabricated and never omitted silently. Every
artifact is scanned for residual secrets before the package is declared valid, and account
references are replaced with synthetic `account#<hash>` tokens.

### Evidence provenance: runtime vs. sample

Every package carries an `evidenceProvenance` field:

- `RUNTIME` — a real in-host diagnostic run.
- `SAMPLE` — synthetic documentation data. A `SAMPLE` package also shows a visible banner in
  `summary.html`. It must never be read as a runtime result.

The repository ships one sample package at
`docs/samples/sample-evidence-package/` for documentation. It is labelled `SAMPLE`.

## Reading a package

1. Open `summary.html` first: it shows the aggregate status, the module table, the three acceptance
   subjects and the top non-PASS findings.
2. For a failing or inconclusive check, open `full-report.html` or `findings.json`. Each finding
   carries its observation, source, collection method, limitation, recommended next action and an
   evidence reference.
3. Verify integrity: `manifest.json` lists a SHA-256 for every artifact. Re-hash the files and
   compare. The tool already verifies all 18 artifacts and their hashes before it writes anything.

## Required configuration

The tool is read-only, so "configuration" means the read-only inputs the operator supplies; nothing
here mutates the host.

| Input | Where | Default | Effect if unset |
| --- | --- | --- | --- |
| Target instrument | the Diagnostic Center picker (exact contract FullName) | `ES 12-26` (compiled D-097 default) | it still resolves only an exact loaded contract; a missing contract leaves M4 `INCONCLUSIVE` |
| Target account identity | fixed in `Core/Identity/AccountIdentityVerifier` (`ExpectedAccountIdentity`) | account `SimRP.BOT`, connection `Rithmic`, provider `Rithmic` (slot `Provider14` via BOOT's compiled mapping), mode `Simulation` or `Live`, currency `USD` | n/a (it is the compile-time expectation) |
| Owner-authorized provider slot mapping | `DiagnosticAddOn.OwnerAuthorizedProviderSlots` | BOOT's compiled immutable D-097 mapping (`Provider14` → `Rithmic`) | a caller may supply an empty mapping; any other slot then stays unresolved and provider checks are `INCONCLUSIVE` |
| Readiness retry loop | `DiagnosticAddOn.ReadinessRetryEnabled` | `true` (host); tests set `false` | with it off only the first synchronous probe runs |

Nothing else is configurable, and no input enables trading or mutates an account, connection,
position or RiskPilot setting.

## Known limitations

- **No real-host verification.** Every result in this repository was produced against offline
  **stubs**. A real NinjaTrader build and a real AddOn run have **not** been performed here, so
  host compatibility is not proven. See `docs/INSTALL.md` and `docs/NT8_ACCEPTANCE_RUNBOOK.md`.
- **Host build version is unobservable.** No public NinjaTrader API exposes the installed host
  build, so `M1.HOST_COMPATIBILITY` is always `INCONCLUSIVE` and the declared target
  (NinjaTrader 8.1.9.0, minimum supported 8.1.8.4) is an intention, not a verified fact.
- **M7 executes when a read-only source is available.** A stock installation ships no strategy
  diagnostic producer, so M7 stays `INCONCLUSIVE`. When a host or RiskPilot component supplies
  `IReadOnlyDiagnosticSource` records, M7 executes the Stage 3/4 strategy verification and exports
  the same evidence; the read-only boundary is in `docs/M7_STRATEGY_INTEGRATION.md`.
- **Provider identity resolves only the authorized slot.** The adapter occupies the generic slot
  `Provider14`, which carries no vendor identity in the enum. BOOT's own compiled immutable D-097
  mapping resolves exactly `Provider14` → `Rithmic`; every other slot stays `INCONCLUSIVE`. No
  vendor is inferred by name or guesswork.
- **The target instrument is explicit.** `ES 12-26` is the compiled D-097 default; the operator may
  re-target in the picker. The tool never auto-discovers an instrument. A cleared selection never
  becomes ready and M4 stays `INCONCLUSIVE`.
- **Depth (Level II) depends on the feed.** An instrument/connection without a Level II surface
  reports `M5.DEPTH` `INCONCLUSIVE` — that is a supported outcome, not a failure.
- **Read-only by design.** The tool cannot submit, modify or cancel an order and cannot enable
  trading; anything that would require a mutation is reported `INCONCLUSIVE` rather than attempted.

## The three acceptance subjects

Acceptance is reported as three independent subjects and is never collapsed into a single verdict:

1. **AddOn implementation / build** — does the tool build and load. A stub build stays
   `INCONCLUSIVE`, because compiling against stubs is not proof of host compatibility.
2. **Runtime diagnostic coverage / evidence quality** — what the run actually observed.
3. **RiskPilot.BOT acceptance** — always `NOT_APPLICABLE` from inside this tool. The tool is
   independent of RiskPilot.BOT and cannot certify it.

A green AddOn build or a green diagnostic run is **not** a RiskPilot.BOT acceptance.

## Keyboard and workflow tips

- Run **Quick Diagnostic** first; it is fast and covers the common causes.
- Run **Full Diagnostic** when you need DOM, order-flow and strategy coverage.
- Run **Post-Trade Audit** after a session to inspect the order/execution/position surface.
- Export after the run you want to keep. Each export is self-contained and hash-verified.
