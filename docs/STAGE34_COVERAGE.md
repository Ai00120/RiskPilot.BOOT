# Stage 3 / Stage 4 diagnostic coverage — traceability matrix

This document is the readable form of the Stage 3 (Position Sizing) / Stage 4 (Independent Risk
Gate) coverage audit. It maps each RiskPilot.BOT Stage 3/4 requirement to the BOOT module(s) and
check(s) that can provide evidence for it, the host data the evidence depends on, the verification
method, and the current limitation. The machine-readable form is exported with every run as
`stage34-coverage.json`; the single source of truth is `Core/Run/Stage34Coverage.cs`
(`Stage34CoverageCatalog`), and the per-run status is derived by `Stage34CoverageEvaluator` from the
results the run actually produced.

**Reference repo:** `Ai00120/RiskPilot.BOT` (read-only). BOOT has no execution dependency on it.

## How to read this matrix

Three axes are kept deliberately separate, because they are not the same claim:

- **Evidence class** — where the evidence can come from.
  - `RUNTIME_HOST`: observable from the live NinjaTrader public APIs.
  - `SOURCE_LEVEL`: verifiable only by reading production source, not from the running host.
  - `NOT_VERIFIABLE`: neither a host observation nor a source fact.
- **Verification scope** — how completely BOOT can verify the requirement at all.
  - `FULL`: fully observable from the host surface when the data is present.
  - `PARTIAL`: BOOT can observe only part of it.
  - `NONE`: not verifiable at runtime.
- **Coverage status** — the outcome **for one run**, derived from the checks that run produced.
  - `VERIFIED`: every backing check ran and PASSed in this run.
  - `FAILED`: a backing check FAILed in this run.
  - `INCONCLUSIVE`: the backing checks ran but the required host evidence was not available, or a
    backing check was not present in this run.
  - `NOT_EXECUTED`: the backing module did not run in this mode.
  - `SOURCE_ONLY`: the requirement has no runtime host API and is not verifiable at runtime.

Two distinctions this audit is required to make explicit:

- **Source-level vs. runtime verification.** A requirement that BOOT can only check by reading
  source (or that lives entirely inside RiskPilot.BOT) is never `VERIFIED`. It is `SOURCE_ONLY`.
  The existence of a BOT feature in source is *not* evidence that the feature works at runtime.
- **Fail-closed reporting.** A requirement whose required host evidence is unavailable is
  `INCONCLUSIVE`, never `PASS`. The headline `statement` field of every `stage34-coverage.json`
  states in plain terms that BOOT does **not** fully validate Stage 3 or Stage 4.

## The permanent identity

The permanent identity is represented by the following BOOT checks. The raw literal account and
connection names live **only** in `Core/Identity/AccountIdentityVerifier.cs`; the static safety scan
fails if the account literal appears in any other production source file, and no raw identity is
ever written to a finding, a log or an artifact (only a synthetic `account#<hash>` /
`connection#<hash>` / `instrument#<hash>` token).

| Identity element | Value | BOOT check |
| --- | --- | --- |
| Account | permanent single account (`SimRP.BOT`, defined only in the identity definition file) | `M2.ACCOUNT_IDENTITY` |
| Connection | permanent bound connection (`My Rithmic`, defined only in the identity definition file) | `M2.ACCOUNT_CONNECTION_ASSOCIATION`, `M2.PROVIDER_IDENTITY` |
| Provider | `Rithmic` | `M2.PROVIDER_IDENTITY`, `M2.ACCOUNT_PROVIDER_AGREEMENT` |
| Mode | `Simulation` | `M2.ACCOUNT_IDENTITY` |
| Instrument | `ES` (root) | `M4.INSTRUMENT_IDENTITY` |
| Exchange | `CME` (confirmed via the D-096 Globex/Rithmic configured route, never inferred from text) | `M4.INSTRUMENT_IDENTITY`, `M4.D096_ROUTE_CANDIDATE` |
| Product | E-mini S&P 500 Futures | `M4.ES_PRODUCT_SPEC` |
| Platform | NinjaTrader 8 (installed build is unobservable — never reported as PASS) | `M1.HOST_COMPATIBILITY`, `M1.HOST_VERSION` |
| Currency | `USD` | `M2.ACCOUNT_IDENTITY`, `M4.ES_PRODUCT_SPEC` |

The **Product** row is the new coverage added by this audit. `M4.INSTRUMENT_IDENTITY` proves *which*
instrument is loaded (root / venue / type / currency); `M4.ES_PRODUCT_SPEC` proves the loaded
instrument's *economics* (point value 50, tick size 0.25, derived tick value 12.50, currency USD),
which Owner Decision D-095 §4 makes part of the one immutable canonical ES specification. The
economics are compared against published CME contract facts used as reference expectations with
explicit provenance — not an independent exchange-issued attestation — and the raw symbol is never
reported. A field that cannot be read is `INCONCLUSIVE`; a field that contradicts is `FAIL`.

## The matrix

| ID | Stage | Requirement | BOT ref | BOOT module(s) | BOOT check(s) | Evidence class | Scope | Needs real host | Needs Rithmic SIM | Current limitation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `S3-D022-DUAL-POLICY` | Stage 3 | Dual-policy sizing: `PERCENTAGE_OF_RISK` default, `FIXED_QUANTITY` only by explicit config, no automatic fallback | D-022 | M7 | `M7.STAGE3_SIZING`, `M7.REASON_ATTRIBUTION` | RUNTIME_HOST | PARTIAL | no | no | Sizing reason codes are observable only when RiskPilot.BOT exposes its read-only diagnostics; the policy-selection logic is not re-derived. |
| `S3-D022-FAILCLOSED` | Stage 3 | Missing/invalid/stale inputs fail closed with quantity 0 and an explicit reason | D-022 | M7 | `M7.STAGE3_SIZING` | RUNTIME_HOST | PARTIAL | yes | no | Reported as a raw outcome distribution; BOOT does not re-evaluate the sizing math. |
| `S3-D022-QUANTITY-LATTICE` | Stage 3 | Quantity rounded down to `QuantityStep`; below `QuantityMin` fails closed; `QuantityMax` enforced | D-022 | M7 | `M7.STAGE3_SIZING` | RUNTIME_HOST | PARTIAL | yes | no | Reports the lattice reason codes; does not independently recompute the lattice. |
| `S3-ANALYTICAL-NONEXECUTABLE` | Stage 3 | Stage 3 output is analytical only; no order submit/modify/cancel; trading stays disabled | D-022 / invariants 1–2 | M8 | `M8.SECURITY_SCAN`, `M8.READ_ONLY_POSTURE`, `M8.TRADING_DISABLED` | RUNTIME_HOST | FULL | yes | no | The scan proves the absence of forbidden patterns in BOOT's scanned source; it is a source-level guarantee about BOOT, not about RiskPilot.BOT. |
| `S3-OD1-INVALIDATION` | Stage 3 | Structured `InvalidationLevel` provenance; prose parsing and fabricated levels prohibited | OD-1 / D-074 / D-075 | — | — | SOURCE_LEVEL | NONE | no | no | No public host API exposes the invalidation producer; reported `SOURCE_ONLY`. |
| `S4-MONOTONIC-QUANTITY` | Stage 4 | `0 <= ApprovedQuantity <= RequestedQuantity`; a veto means 0; never originates/increases quantity | D-022 / G3-c | M7 | `M7.RISK_GATE` | RUNTIME_HOST | PARTIAL | yes | no | Observes the gate outcome distribution, not the internal arithmetic. |
| `S4-FAILCLOSED` | Stage 4 | Missing/invalid policy, account state, instrument spec or portfolio evidence vetoes with quantity 0 | D-022 / D-032 / G3-c | M7 | `M7.RISK_GATE`, `M7.REASON_ATTRIBUTION` | RUNTIME_HOST | PARTIAL | yes | no | Reports the veto reasons it observes; does not reproduce the decision logic. |
| `S4-ACCOUNT-STATE-GATE` | Stage 4 | Non-bypassable verified account state; a missing/stale/invalid snapshot vetoes; setters non-public | D-032 / OD-3 | M2, M7 | `M2.ACCOUNT_IDENTITY`, `M7.RISK_GATE` | RUNTIME_HOST | PARTIAL | yes | yes | Proves the account identity and observes account-state vetoes; cannot read RiskPilot.BOT's in-memory snapshot or prove setter visibility (source fact). |
| `S4-RISK-POLICY` | Stage 4 | Compiled immutable `RiskPolicyLimits` with a recomputed content hash; integrity failure fails closed | D-036 / D-073 / OD-6 | — | — | SOURCE_LEVEL | NONE | no | no | The compiled profile and its hash are RiskPilot.BOT source facts; reported `SOURCE_ONLY`. |
| `S4-PORTFOLIO-STATE` | Stage 4 | Unknown dynamic portfolio metrics must not silently default to zero | OD-2 / D-031 | — | — | SOURCE_LEVEL | NONE | no | no | Portfolio state is RiskPilot.BOT-internal; reported `SOURCE_ONLY`. |
| `S34-CORRELATION` | Stage 4 | Decision → proposal → risk-evaluation ids preserved and correlated end to end | D-022 inv 7 / G3-b / G3-c | M7 | `M7.STAGE_CORRELATION` | RUNTIME_HOST | FULL | yes | no | PASS only when the read-only source supplies the three ids; otherwise `INCONCLUSIVE`. |
| `ID-ACCOUNT` | Identity | Exact permanent single-account identity (name / provider / connection / mode / currency) | D-095 / D-096 | M2 | `M2.ACCOUNT_IDENTITY`, `M2.ACCOUNT_CONNECTION_ASSOCIATION` | RUNTIME_HOST | FULL | yes | yes | Needs the real Rithmic SIM account; with no account surface it is `INCONCLUSIVE`, never PASS. |
| `ID-CONNECTION-PROVIDER` | Identity | Exact connection/provider identity, connected, the account's own bound connection | D-096 | M2 | `M2.PROVIDER_IDENTITY`, `M2.ACCOUNT_CONNECTION_ASSOCIATION`, `M2.CONNECTIONS` | RUNTIME_HOST | FULL | yes | yes | Provider resolved by string comparison; the provider build is unobservable and never PASS. |
| `ID-INSTRUMENT` | Identity | Canonical ES/CME instrument identity (root / venue / type / currency) | D-078 / D-096 / D-095 §4 | M4 | `M4.INSTRUMENT_IDENTITY`, `M4.D096_ROUTE_CANDIDATE`, `M4.REGISTRY_MATCH` | RUNTIME_HOST | FULL | yes | yes | The venue is a configured-route mapping (Owner-authorized D-096), not an independent exchange-issued listing-venue attestation. |
| `ID-ES-ECONOMICS` | Identity | Canonical ES economics (point 50 / tick 0.25 / tick value 12.50 / USD) | D-095 §4 / D-096 §3 | M4 | `M4.ES_PRODUCT_SPEC` | RUNTIME_HOST | FULL | yes | yes | Reference CME contract facts, not an independent exchange attestation; the raw symbol is never reported. |
| `DEP-DOM-MARKETDATA` | Data | Live Level I / Level II market-data surface with observable integrity and freshness | D-073 / data contract | M5 | `M5.DOM_SURFACE`, `M5.DEPTH`, `M5.LEVEL1`, `M5.DATA_INTEGRITY`, `M5.FRESHNESS`, `M5.VALIDITY` | RUNTIME_HOST | FULL | yes | yes | Freshness is always `INCONCLUSIVE` and only labelled FRESH/STALE; depth is never inferred from a Level I quote. |
| `DEP-TRADEDELTA` | Data | Live trade stream with observable sequence and direction source | PHASE4_ORDER_FLOW_SPEC | M6 | `M6.TRADE_STREAM`, `M6.SEQUENCE_CONTINUITY`, `M6.DIRECTION_SOURCE`, `M6.TRADEDELTA_COMPLETENESS` | RUNTIME_HOST | PARTIAL | yes | yes | NT8's public Level I surface exposes no aggressor side, so direction is always derived; continuity is `INCONCLUSIVE` without an observed source sequence. |
| `DEP-POSTTRADE-AUDIT` | Data | Order / execution / position lifecycle audit with correlation, timing, missing/duplicate detection | PHASE3_RECORDING_SPECIFICATION | M9 | `M9.ORDER_LIFECYCLE`, `M9.EXECUTION_LIFECYCLE`, `M9.CORRELATION_TIMING`, `M9.MISSING_OR_DUPLICATE`, `M9.AUDIT_SURFACE` | RUNTIME_HOST | PARTIAL | yes | yes | Covers only events recorded while the tool ran; execution coverage is derived (the host reads only `Account.Orders`), never assumed. |

## Important requirements BOOT cannot currently verify

These are reported honestly as uncovered or source-only. They are **not** bugs to be papered over;
they are the boundary of what a read-only NT8 AddOn can observe.

| Requirement | Why BOOT cannot verify it | Reported as |
| --- | --- | --- |
| Structured `InvalidationLevel` provenance (OD-1) | No public NinjaTrader API exposes RiskPilot.BOT's per-candidate invalidation level. | `SOURCE_ONLY` |
| Compiled immutable `RiskPolicyLimits` + hash (D-036 / D-073) | The profile and its SHA-256 live inside RiskPilot.BOT; no host API exposes them. | `SOURCE_ONLY` |
| Dynamic portfolio state not zero-defaulted (OD-2) | Portfolio state is RiskPilot.BOT-internal. | `SOURCE_ONLY` |
| Exact internal sizing arithmetic (rounding / min / cap math) | Only the resulting reason codes are observable, not the intermediate values. | `PARTIAL` / `INCONCLUSIVE` |
| Installed NinjaTrader build / provider build | No public API exposes them. | `M8.UNOBSERVABLE` |
| Trade aggressor side | No public Level I API exposes it; direction is derived. | `INCONCLUSIVE` |

## Checks that require a real host or a connected Rithmic SIM account

`stage34-coverage.json` carries two per-requirement flags so this can be answered from the artifact:

- `requiresRealHost: true` — the requirement needs a real NinjaTrader installation to observe
  anything (identity, market data, order flow, post-trade audit, runtime posture).
- `requiresRithmicSimAccount: true` — the requirement additionally needs the actual connected
  Rithmic simulation account (all identity checks, the account-state gate, the market-data / order-
  flow / audit dependencies).

A run performed without the host, or without the connected Rithmic SIM account, leaves these
requirements `INCONCLUSIVE` or `NOT_EXECUTED`; it never reports them `VERIFIED`.

## Acceptance summary

Every `stage34-coverage.json` reports, per run:

- what was **verified** (`verifiedCount`, per-requirement `status=VERIFIED`),
- what **failed** (`failedCount`, `status=FAILED`),
- what remains **inconclusive** (`inconclusiveCount`, `status=INCONCLUSIVE` / `NOT_EXECUTED`),
- which requirements are **covered** (all rows) and which are **not covered** at runtime
  (`sourceOnlyCount` / `notCoveredCount`),
- which checks **require a real NinjaTrader installation** (`requiresRealHostCount`),
- which checks **require an actual connected Rithmic simulation account**
  (`requiresRithmicSimAccountCount`),
- an explicit `statement` that BOOT does **not** fully validate Stage 3 or Stage 4.

## Safety

BOOT remains strictly read-only. This audit added no order, account, connection, position or
configuration mutation. `M4.ES_PRODUCT_SPEC` only reads `MasterInstrument.*` of the already-loaded
instrument, after the live readiness probe, and reduces it to numbers plus a synthetic reference.
The static safety scan (`verification/addon-smoke/test_diagnostic_safety.py`) still passes, and no
raw identity literal is emitted by any new code.
