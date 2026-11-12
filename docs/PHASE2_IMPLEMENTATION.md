# RP-DIAG-01 — PHASE 2: FOUNDATION & DIAGNOSTIC CORE

**Product:** RiskPilot NT8 Diagnostic Center
**Namespace:** `RiskPilot.Diagnostics.Nt8`
**Document type:** Phase 2 implementation record
**Prepared:** 2026-11-12
**Safety invariant:** `Trading=Disabled` — read-only, no order/execution/account mutation.
**Depends on:** `docs/PHASE1_ARCHITECTURE_AND_AUDIT.md` (approved).

> This document records what was actually built in Phase 2, how it was verified, and —
> explicitly — what was **not** verified. The offline build uses compile-time stubs, so
> **real NinjaTrader 8.1.8.4 host compatibility is NOT established** by this phase.

---

## 1. Scope delivered

Phase 2 delivers the standalone solution and the diagnostic core, without any execution
dependency on `Ai00120/RiskPilot.BOT`:

| Requirement | Where |
|---|---|
| Standalone solution + project | `RiskPilot.Diagnostics.Nt8.sln`, `src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj` |
| Real NinjaTrader references (no fake build passed off as real) | `Host/` layer + `UseStubs=false` reference mode |
| AddOn lifecycle (Initialize/Start/Stop/Dispose) | `Host/DiagnosticAddOn.cs`, `Host/DiagnosticRuntime.cs` |
| Thread-safe bounded event queue | `Core/Queue/EventQueue.cs`, `Core/Queue/EventIngestPipeline.cs` |
| No blocking of NT callbacks | `EventIngestPipeline.Ingest` is lock-free on the hot path; heavy work runs on the drain thread |
| Backpressure / overflow / dropped-event counting | `EventQueue.TryEnqueue` returns false when full; `SessionContext.RecordDropped` |
| Session id + standard timestamp | `Core/Session/SessionId.cs`, `SessionContext`, `NormalizedRecord` |
| Normalized models (Environment, Connection, Provider, Account, Instrument, Order, Execution, Position, MarketData, DOM, Strategy, Finding) | `Core/Models/` |
| Status PASS/FAIL/INCONCLUSIVE/NOT_APPLICABLE | `Core/Status/DiagnosticStatus.cs` |
| Logging + correlation id | `Core/Logging/DiagnosticLog.cs`, `Core/Identity/CorrelationId.cs` |
| Extensible JSON output | `Core/Report/JsonWriter.cs`, `ReportSerializer.cs` |
| SHA-256 for outputs + manifest | `Core/Report/Hashing.cs`, `ReportManifest.cs`, `ManifestSerializer.cs` |
| Secret redaction / masking | `Core/Security/SecretRedactor.cs`, `SyntheticRef.cs` |
| Error handling + clean shutdown | `Core/Errors/ErrorClassifier.cs`, `DiagnosticRuntime.Dispose` (final drain) |
| Unit tests | `tests/RiskPilot.Diagnostics.Nt8.Tests/` |
| Static safety scan | `verification/addon-smoke/test_diagnostic_safety.py` |

---

## 2. Safety boundaries (as implemented)

- **Read-only.** No `SubmitOrder`, `CancelOrder`, `ChangeOrder`, `EnterLong/Short`,
  `ExitLong/Short`, `SetStopLoss`, `SetProfitTarget`, `Account.Flatten`, `Connection.Connect`
  or any equivalent appears in production source. Enforced by the static scan.
- **No reflection / unsafe / native interop.** No `System.Reflection`, `Activator`, `unsafe`,
  `stackalloc` or `DllImport` in production source (AssemblyInfo attribute usage excepted).
- **No mutation.** The `Host` layer reads only; it never writes account/connection/position
  or RiskPilot settings.
- **Callbacks never block.** `Ingest` stamps and enqueues; all processing is deferred to the
  drain worker (`DiagnosticRuntime.DrainLoop`).
- **Trading stays disabled.** `DiagnosticAddOn.TradingDisabled = true`; there is no enable
  path anywhere in the codebase.
- **Secret-free output.** Every string that reaches a report passes through `SecretRedactor`;
  account identifiers are replaced with `SyntheticRef` hashes.

---

## 3. API reconciliation with the real NinjaTrader surface

Phase 1 documented the intended API surface from official NinjaScript docs and from the
RiskPilot.BOT source. During Phase 2 implementation the following deltas were found and
reconciled. None of these contradict Phase 1; they are refinements required to make the
host layer compile against the documented public surface.

| # | Phase 1 assumption | Reality / decision | Where |
|---|---|---|---|
| A1 | "connection provider detection" implies a `Provider` enum member for Rithmic | No `Provider.Rithmic` member is guaranteed; BOT PR #275 hit exactly this. Provider identity is resolved by **string comparison** on `ConnectOptions.Provider.ToString()`. | `Nt8HostDataProvider`, `M2ConnectionProbe` |
| A2 | Account/connection state enum names | The host exposes `ConnectionStatus { Connected, Connecting, ConnectionLost, Disconnecting, Disconnected }`. Mapped by a closed switch that fails closed to `Unknown`. | `Nt8HostDataProvider.MapConnectionState` |
| A3 | Connection has a single status | NT8 separates price feed and order channel status (`PriceStatus`, `Status`). Both are recorded separately as `PriceState` / `OrderChannelState`. | `ConnectionRecord` |
| A4 | `MasterInstrument.*` readable at any time | Must be read only after `DataLoaded`/`Realtime`; the provider guards with a `masterMetadataReady` flag and fails closed to "unavailable". | `Nt8HostDataProvider.GetInstrument` |
| A5 | Host version observable | No public API exposes the exact NT8 build. `M1.HOST_VERSION` is always `INCONCLUSIVE`. | `M1EnvironmentProbe` |
| A6 | Provider build/version observable | No public API exposes the provider build. Recorded as unobservable. | `ProviderRecord` |
| A7 | "OrderState" naming for the connection's order channel | Renamed to `OrderChannelState` to avoid collision with the NT8 order-lifecycle enum and to keep the static safety scan unambiguous. | `ConnectionRecord` |
| A8 | Environment class derivable from a dedicated API | No such API; derived from account name convention (`Sim*` ⇒ Simulation) and fails closed to `Unknown` when no account is present. | `Nt8HostDataProvider.DetectEnvironment` |

**Unobservable items are recorded as such** and never reported as `PASS`:
`NINJATRADER_RUNTIME_VERSION`, `PROVIDER_BUILD`, `TRADE_AGGRESSOR_SIDE`.

---

## 4. Data model (normalized)

Every record derives from `NormalizedRecord`, which carries the common envelope:
`SessionId`, `Sequence`, `SourceTimestampUtc` (nullable), `SourceTimeBasis`,
`IngestedAtUtc`, `CorrelationId`.

| Model | Key fields |
|---|---|
| `EnvironmentRecord` | Environment class, host version (+observable flag), tool build mode, target framework, UTC offset |
| `ConnectionRecord` | Connection name, price/order channel states, provider name (+string-detection flag), role, reconnect count |
| `ProviderRecord` | Name, resolution method, is-expected-evidence-provider, build-observable flag |
| `AccountRecord` | Synthetic account ref, class, denomination, provider, position/order counts |
| `InstrumentRecord` | Full name, expiry, exchange, root symbol, type, currency, tick size, point value, master-metadata flag |
| `OrderRecord` | Order id, instrument, lifecycle state, side, type, quantity, filled quantity |
| `ExecutionRecord` | Execution id, order id, instrument, price, quantity, time |
| `PositionRecord` | Instrument, side, quantity, average price |
| `MarketDataRecord` | Instrument, kind (quote/trade/bar), bid/ask, last, volume |
| `DomRecord` | Instrument, operation (insert/update/remove/reset), level, price, size, market maker |
| `StrategyRecord` | Strategy id, activity, instance name |
| `DiagnosticFinding` | Check id, module, status, severity, message, reason codes, error class |

**Historical baseline is never presented as runtime data.** The recorded 270,979-record
session (`rec_20261111_200059_4ba92f5f734741d39e1c2ef611a6cf26`, ES 12-26, Rithmic) is a
documented baseline only. It appears nowhere in the tool's runtime output.

---

## 5. Execution model

```
NT8 callback thread                 drain thread (DiagnosticRuntime.DrainLoop)
─────────────────────               ────────────────────────────────────────────
Ingest(record)  ──▶ EventIngestPipeline.Ingest
   stamp session/time/sequence
   duplicate check (bounded window)
   queue.TryEnqueue (non-blocking) ──▶ EventQueue (bounded ring)
   update session counters               │
   return immediately                    ▼
                                     Drain(buffer) → Process(record)
                                     (heavy work, off the callback path)
```

- The queue is bounded; when full, `TryEnqueue` returns `false`, the session records a drop,
  and the drop surfaces as `M0.EVENT_INTEGRITY = FAIL` (fail-closed — never a silent PASS).
- `DiagnosticRuntime.Stop` and `Dispose` both perform a **final drain** so shutdown does not
  silently abandon queued records.
- `Stop`/`Dispose` join the worker with a 5-second bound so a stuck worker cannot hang the host.

---

## 6. Verification

### 6.1 Offline build (executed)

```
dotnet build src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=true
→ Build succeeded. (1 warning: unused stub event)
```

The single warning is in the offline stub file and does not affect the product assembly.

### 6.2 Unit tests (executed)

```
dotnet test tests/RiskPilot.Diagnostics.Nt8.Tests/RiskPilot.Diagnostics.Nt8.Tests.csproj
→ Passed! Failed: 0, Passed: 94, Skipped: 0, Total: 94
```

| Required test area | Test file |
|---|---|
| Thread safety | `ThreadSafetyTests.cs` |
| Queue overflow | `EventQueueTests.cs` |
| Duplicate event | `DuplicateEventTests.cs` |
| Sequence gap | `SequenceGapTests.cs` |
| Timestamp handling | `TimestampTests.cs` |
| Session isolation | `SessionIsolationTests.cs` |
| Serialization | `SerializationTests.cs` |
| SHA-256 manifest | `ManifestTests.cs` |
| Secret redaction | `SecretRedactionTests.cs` |
| Shutdown / Dispose | `LifecycleTests.cs` |
| Error classification | `ErrorClassificationTests.cs` |
| Status propagation | `StatusPropagationTests.cs` |
| Probe behaviour (M1/M2) | `ProbeTests.cs` |

The only test double is a deterministic clock and an in-memory data provider that replaces
the NinjaTrader host boundary. All diagnostic logic, the queue, the serializer, the manifest
and the redactor under test are the real production code.

### 6.3 Static safety scan (executed)

```
python3 verification/addon-smoke/test_diagnostic_safety.py
→ STATIC SAFETY CHECK: PASS  (49 production source files scanned, 34 forbidden patterns)
```

---

## 7. Explicit limitations (self-declared)

- **Real host build NOT verified.** No NinjaTrader installation was available in this
  environment. The `net48` / `UseStubs=false` path references `NinjaTrader.Core.dll` and
  `NinjaTrader.Gui.dll` from a Windows install and has **not** been compiled here.
- **Stub signatures may drift.** The stubs are derived from public documentation and the
  confirmed BOT surface. If a signature differs from the installed build, the real build
  fails — which is the correct, fail-closed outcome. A green stub build is **not** proof of
  host compatibility.
- **`verification/addon-smoke` is static only.** It proves the absence of forbidden APIs in
  source; it does not exercise runtime behaviour against a live host.
- **Only M1 and M2 probes exist.** M3–M8 are planned for later phases (Phase 1 §8).
- **No UI yet.** `NTWindow`/`NTTabPage` and menu integration are Phase 5.
- **No BOT integration.** The optional `IReadOnlyDiagnosticSource` contract is declared but
  no BOT side implements it; there is no runtime link to `RiskPilot.BOT`.

---

## 8. Remaining work

| Item | Phase | Notes |
|---|---|---|
| Real NT8 build + runtime smoke | Next | Requires a Windows host with NT8 8.1.8.4 |
| M3 Account/Order lifecycle | 3 | Needs Owner decision on `Executions` scope |
| M4 Instrument attestation (D-078/D-096) | 3 | Reproduce attestation failure reasons |
| M5 DOM/Market data quality | 4 | RESET frequency, snapshot-sync, gaps |
| M6 TradeDelta/OrderFlow | 4 | Direction-unknown rate |
| M7 Strategies/Risk gate | 5 | Reason-code distribution |
| M8 Safety/Acceptance + UI/Report | 5 | `NTWindow` + acceptance report |

---

## 9. Git

- Branch: `phase2/foundation-diagnostic-core` (off `main`).
- No change was made to `Ai00120/RiskPilot.BOT`.
- Commit SHA: `8ef688a4ea968a68654a986b9bc94b4b715ab931` (this record is amended into the
  same commit).
- The branch is local only; it has **not** been pushed and no PR was opened.
