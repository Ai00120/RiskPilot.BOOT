# Stage 3 / Stage 4 diagnostic coverage — traceability matrix

This document is the readable form of the Stage 3 (Position Sizing) / Stage 4 (Independent Risk
Gate) coverage audit. It maps each RiskPilot.BOT Stage 3/4 requirement to the BOOT module(s) and
check(s) that can provide evidence for it, the host data the evidence depends on, the verification
method, the test coverage, and the current limitation. The machine-readable form is exported with
every run as `stage34-coverage.json`; the single source of truth is `Core/Run/Stage34Coverage.cs`
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

Every runtime row also carries a **Test coverage** column naming the BOOT test(s) that exercise its
backing check(s), so a reader can confirm the evidence is actually exercised rather than merely
declared. A source-only row has no host test and the column is empty; a test asserts this.

This matrix was reconciled against the authoritative RiskPilot.BOT Stage 3/4 records on `main`
(`G3_STAGE3_STAGE4_ARCHITECTURE_CONTRACT.md`, `DECISION_LOG.md`, and the D-031-2 / R1, D-036 / D-073,
D-078 / D-080, D-085, D-095 and D-096 owner decisions). That reconciliation added seven requirements
the first pass had overlooked: policy/configuration versioning and snapshot correlation (D-022
condition 6), the "no invented data" cross-stage invariant, the non-bypassable verified-account
enforcement and setter-visibility trust boundary (OD-3 §6), drawdown / high-water-mark continuity
(D-031-2 / R1), thread-safety and subscriber isolation (cross-stage invariant 8), the contract-expiry
pin and manual-rollover governance (D-085 / D-095 §5), and the NinjaTrader 8 platform identity.

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

The single source of truth is `Core/Run/Stage34Coverage.cs` (`Stage34CoverageCatalog`). The table below is generated from the exported `stage34-coverage.json` of the committed SAMPLE package, so it cannot drift from the machine-readable artifact. Every column the audit requires is present: the BOT requirement, the BOOT module(s), the host data, the verification method (see the `verificationMethod` field in the JSON), the test coverage, and the current limitation.

| ID | Stage | Requirement | BOT ref | BOOT module(s) | BOOT check(s) | Evidence class | Scope | Needs real host | Needs Rithmic SIM | Test coverage | Current limitation |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `S3-D022-DUAL-POLICY` | Stage 3 | Dual-policy sizing architecture | D-022 (conditions 1-4) | M7 | `M7.STAGE3_SIZING`, `M7.REASON_ATTRIBUTION` | RUNTIME_HOST | PARTIAL | no | no | M7StrategyTests | BOOT can observe the sizing reason codes only when RiskPilot.BOT exposes its read-only diagnostics; the policy-selection logic itself is BOT-internal and is not re-derived here. |
| `S3-D022-FAILCLOSED` | Stage 3 | Fail-closed sizing on missing inputs | D-022 (condition 5) | M7 | `M7.STAGE3_SIZING` | RUNTIME_HOST | PARTIAL | yes | no | M7StrategyTests | A zero-quantity outcome is reported only as a raw distribution; BOOT does not re-evaluate the sizing math, so it cannot prove a specific input caused a specific reason. |
| `S3-D022-QUANTITY-LATTICE` | Stage 3 | Quantity rounding / min / cap rules | D-022 (condition 7) | M7 | `M7.STAGE3_SIZING` | RUNTIME_HOST | PARTIAL | yes | no | M7StrategyTests | BOOT reports the lattice reason codes as a distribution; it does not independently recompute the quantity lattice. |
| `S3-D022-VERSION-CORRELATION` | Stage 3 | Policy / configuration versioning and snapshot correlation | D-022 (condition 6) | M7 | `M7.STAGE_CORRELATION`, `M7.STAGE3_SIZING` | RUNTIME_HOST | PARTIAL | yes | no | M7StrategyTests | No public host API exposes the sizing policy version; BOOT can only report the correlation the source supplies, and stays INCONCLUSIVE without it. |
| `S3-ANALYTICAL-NONEXECUTABLE` | Stage 3 | Stage 3 output is analytical and non-executable | D-022 / cross-stage invariants 1-2 | M8 | `M8.SECURITY_SCAN`, `M8.READ_ONLY_POSTURE`, `M8.TRADING_DISABLED` | RUNTIME_HOST | FULL | yes | no | SecurityScanTests, Phase3ModuleTests | The scan proves the absence of forbidden patterns in the scanned source; it is a source-level guarantee, not a proof about RiskPilot.BOT's own source. |
| `S3-NO-INVENTED-DATA` | Stage 3 | No invented data (cross-stage invariant 4) | cross-stage invariant 4 | — | — | SOURCE_LEVEL | NONE | no | no | — | BOOT observes the fail-closed outcomes but cannot prove the absence of fabricated data; it reports this as SOURCE_ONLY. |
| `S3-OD1-INVALIDATION` | Stage 3 | Structured InvalidationLevel provenance (OD-1) | OD-1 / D-074 / D-075 | — | — | SOURCE_LEVEL | NONE | no | no | — | No public host API exposes the invalidation producer; BOOT cannot verify it at runtime and reports it as SOURCE_ONLY. |
| `S4-MONOTONIC-QUANTITY` | Stage 4 | Monotonic quantity invariant | D-022 / cross-stage invariant 6 / G3-c | M7 | `M7.RISK_GATE` | RUNTIME_HOST | PARTIAL | yes | no | M7StrategyTests | BOOT observes the risk-gate outcome distribution, not the internal quantity arithmetic; the invariant itself is BOT-internal. |
| `S4-FAILCLOSED` | Stage 4 | Fail-closed risk gate on missing inputs | D-022 / D-032 / G3-c | M7 | `M7.RISK_GATE`, `M7.REASON_ATTRIBUTION` | RUNTIME_HOST | PARTIAL | yes | no | M7StrategyTests | BOOT reports the veto reasons it observes; it does not reproduce the gate's decision logic. |
| `S4-ACCOUNT-STATE-GATE` | Stage 4 | Verified account state gates Stage 4 (D-032) | D-032 / OD-3 | M2, M7 | `M2.ACCOUNT_IDENTITY`, `M7.RISK_GATE` | RUNTIME_HOST | PARTIAL | yes | yes | M7StrategyTests, AccountIdentityTests, TargetAccountConnectionTests, HostTargetAccountConnectionTests | BOOT proves the account identity and observes account-state vetoes; it cannot read RiskPilot.BOT's in-memory AccountStateSnapshot. |
| `S4-ACCOUNT-STATE-UNBYPASSABLE` | Stage 4 | Non-bypassable verified-account enforcement (OD-3) | D-032 / OD-3 (contract §6) | M2 | `M2.ACCOUNT_IDENTITY`, `M2.ACCOUNT_CONNECTION_ASSOCIATION` | RUNTIME_HOST | PARTIAL | yes | yes | AccountIdentityTests, TargetAccountConnectionTests, HostTargetAccountConnectionTests | BOOT can confirm the account identity but cannot read RiskPilot.BOT's RequireVerifiedAccountState flag or prove setter visibility at runtime; those stay a source-level fact. |
| `S4-RISK-POLICY` | Stage 4 | Compiled immutable RiskPolicyLimits with verified hash (D-036 / D-073) | D-036 / D-073 / OD-6 | — | — | SOURCE_LEVEL | NONE | no | no | — | The compiled policy profile and its SHA-256 are RiskPilot.BOT source facts; BOOT cannot verify them at runtime and reports this as SOURCE_ONLY. |
| `S4-PORTFOLIO-STATE` | Stage 4 | Dynamic portfolio state is not zero-defaulted (OD-2) | OD-2 / D-031 | — | — | SOURCE_LEVEL | NONE | no | no | — | The zero-default hazard is a RiskPilot.BOT source property; BOOT cannot verify it at runtime and reports this as SOURCE_ONLY. |
| `S4-DRAWDOWN-CONTINUITY` | Stage 4 | Drawdown continuity is not falsely reset (D-031-2 / R1) | D-031-2 / Owner-approved R1 remediation 2026-11-11 | — | — | SOURCE_LEVEL | NONE | no | no | — | Drawdown continuity is a RiskPilot.BOT-internal lifecycle property; BOOT cannot verify it at runtime and reports this as SOURCE_ONLY. |
| `S34-CORRELATION` | Stage 4 | Decision→proposal→risk linkage preserved | D-022 invariant 7 / G3-b / G3-c | M7 | `M7.STAGE_CORRELATION` | RUNTIME_HOST | FULL | yes | no | M7StrategyTests | The correlation is only PASS when the read-only source actually supplies the three ids; without them it stays INCONCLUSIVE. |
| `S34-THREAD-SAFETY` | Stage 4 | Thread-safety / subscriber isolation (cross-stage invariant 8) | cross-stage invariant 8 | — | — | SOURCE_LEVEL | NONE | no | no | — | Thread-safety and subscriber isolation are RiskPilot.BOT-internal; BOOT cannot verify them at runtime and reports this as SOURCE_ONLY. |
| `ID-ACCOUNT` | Identity | Exact account identity (D-095 / D-096) | D-095 / D-096 | M2 | `M2.ACCOUNT_IDENTITY`, `M2.ACCOUNT_CONNECTION_ASSOCIATION` | RUNTIME_HOST | FULL | yes | yes | AccountIdentityTests, TargetAccountConnectionTests, HostTargetAccountConnectionTests | Requires the real Rithmic SIM account to be connected; with no account surface the check is INCONCLUSIVE, never PASS. |
| `ID-CONNECTION-PROVIDER` | Identity | Exact connection / provider identity (D-096) | D-096 | M2 | `M2.PROVIDER_IDENTITY`, `M2.ACCOUNT_CONNECTION_ASSOCIATION`, `M2.CONNECTIONS` | RUNTIME_HOST | FULL | yes | yes | TargetAccountConnectionTests, HostTargetAccountConnectionTests | Provider identity is resolved by string comparison (no guaranteed Rithmic enum member); the provider build is unobservable and never reported as PASS. |
| `ID-INSTRUMENT` | Identity | Canonical ES/CME instrument identity (D-078 / D-096) | D-078 / D-096 / D-095 §4 | M4 | `M4.INSTRUMENT_IDENTITY`, `M4.D096_ROUTE_CANDIDATE`, `M4.REGISTRY_MATCH` | RUNTIME_HOST | FULL | yes | yes | InstrumentIdentityTests, HostInstrumentIdentityTests | The venue is a configured-route mapping (Owner-authorized D-096), not an independent exchange-issued listing-venue attestation; the raw symbol is never reported. |
| `ID-ES-ECONOMICS` | Identity | Canonical ES economic specification (D-095 §4) | D-095 §4 / D-096 §3 | M4 | `M4.ES_PRODUCT_SPEC` | RUNTIME_HOST | FULL | yes | yes | EsProductSpecificationTests, HostInstrumentIdentityTests | The canonical economics are published CME contract facts used as reference expectations, not an independent exchange attestation; the raw symbol is never reported. |
| `ID-CONTRACT-EXPIRY` | Identity | Contract expiry pin and manual rollover (D-085 / D-095 §5) | D-085 / D-095 §5 | — | — | SOURCE_LEVEL | NONE | no | no | — | BOOT verifies the canonical ES root/venue form (any expiry) but not the pinned expiry or the governed manual-rollover process; it reports this as SOURCE_ONLY and never claims the pin was changed only through the approved process. |
| `ID-PLATFORM` | Identity | NinjaTrader 8 platform identity and compatibility | D-096 / NinjaTraderCompatibility declaration | M1 | `M1.HOST_COMPATIBILITY`, `M1.HOST_VERSION`, `M1.ENVIRONMENT_CLASS` | RUNTIME_HOST | PARTIAL | yes | no | NinjaTraderCompatibilityTests, LifecycleDiagnosticsTests | No public NinjaTrader API exposes the exact installed host build, so M1.HOST_COMPATIBILITY is always INCONCLUSIVE (never PASS) until a real-host run is recorded in TestedVersions. |
| `DEP-DOM-MARKETDATA` | Data | DOM / Level II and Level I market-data surface | D-073 / data contract | M5 | `M5.DOM_SURFACE`, `M5.DEPTH`, `M5.LEVEL1`, `M5.DATA_INTEGRITY`, `M5.FRESHNESS`, `M5.VALIDITY` | RUNTIME_HOST | FULL | yes | yes | M5DomMarketDataTests | Freshness is always INCONCLUSIVE and only labelled FRESH/STALE; depth is never inferred from a Level I quote. |
| `DEP-TRADEDELTA` | Data | TradeDelta / OrderFlow inputs | PHASE4_ORDER_FLOW_SPEC | M6 | `M6.TRADE_STREAM`, `M6.SEQUENCE_CONTINUITY`, `M6.DIRECTION_SOURCE`, `M6.TRADEDELTA_COMPLETENESS` | RUNTIME_HOST | PARTIAL | yes | yes | M6TradeDeltaTests | NT8's public Level I surface exposes no aggressor side, so direction is always derived; sequence continuity stays INCONCLUSIVE without an observed source sequence. |
| `DEP-POSTTRADE-AUDIT` | Data | Order / execution / position lifecycle audit | PHASE3_RECORDING_SPECIFICATION / M9 | M9 | `M9.ORDER_LIFECYCLE`, `M9.EXECUTION_LIFECYCLE`, `M9.CORRELATION_TIMING`, `M9.MISSING_OR_DUPLICATE`, `M9.AUDIT_SURFACE` | RUNTIME_HOST | PARTIAL | yes | yes | Phase4PostTradeAuditTests, Phase4RuntimeIntegrationTests | The audit only covers events recorded while the tool ran; execution coverage is derived (the host reads only Account.Orders), never assumed. |

## Important requirements BOOT cannot currently verify

These are reported honestly as uncovered or source-only. They are **not** bugs to be papered over;
they are the boundary of what a read-only NT8 AddOn can observe.

| Requirement | Why BOOT cannot verify it | Reported as |
| --- | --- | --- |
| No invented data (cross-stage invariant 4) | Whether a value was fabricated is RiskPilot.BOT-internal; no host API exposes it. | `SOURCE_ONLY` |
| Structured `InvalidationLevel` provenance (OD-1) | No public NinjaTrader API exposes RiskPilot.BOT's per-candidate invalidation level. | `SOURCE_ONLY` |
| Compiled immutable `RiskPolicyLimits` + hash (D-036 / D-073) | The profile and its SHA-256 live inside RiskPilot.BOT; no host API exposes them. | `SOURCE_ONLY` |
| Dynamic portfolio state not zero-defaulted (OD-2) | Portfolio state is RiskPilot.BOT-internal. | `SOURCE_ONLY` |
| Drawdown continuity / high-water mark (D-031-2 / R1) | The HWM and its continuity epoch live inside RiskPilot.BOT. | `SOURCE_ONLY` |
| Thread-safety / subscriber isolation (invariant 8) | The pipeline lock and subscriber dispatch are RiskPilot.BOT-internal. | `SOURCE_ONLY` |
| Non-bypassable verified-account enforcement (OD-3) | BOOT can prove the account identity but cannot read the `RequireVerifiedAccountState` flag or prove setter visibility at runtime. | `PARTIAL` / `INCONCLUSIVE` |
| Exact internal sizing arithmetic (rounding / min / cap math) | Only the resulting reason codes are observable, not the intermediate values. | `PARTIAL` / `INCONCLUSIVE` |
| Contract expiry pin / manual-rollover governance (D-085 / D-095 §5) | BOOT deliberately does not pin the expiry and cannot observe how a pin change was governed. | `SOURCE_ONLY` |
| Installed NinjaTrader build / provider build | No public API exposes them. | `M1.HOST_COMPATIBILITY` `INCONCLUSIVE` / `M8.UNOBSERVABLE` |
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
