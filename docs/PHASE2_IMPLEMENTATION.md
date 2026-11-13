# RP-DIAG-01 — PHASE 2: FOUNDATION & DIAGNOSTIC CORE

**Product:** RiskPilot NT8 Diagnostic Center
**Namespace:** `RiskPilot.Diagnostics.Nt8`
**Document type:** Phase 2 implementation record
**Prepared:** 2026-11-12
**Safety invariant:** `Trading=Disabled` — read-only, no order/execution/account mutation.
**Depends on:** `docs/PHASE1_ARCHITECTURE_AND_AUDIT.md` (approved).

> This document records what was actually built in Phase 2, how it was verified, and —
> explicitly — what was **not** verified. The offline build uses compile-time stubs, so
> **real NinjaTrader 8.1.9.0 host compatibility is NOT established** by this phase
> (declared target 8.1.9.0, minimum supported 8.1.8.4; see `docs/PHASE1_ARCHITECTURE_AND_AUDIT.md` §5.4).

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
| No blocking of NT callbacks | `Ingest` is non-blocking (no user-level lock, no wait on the drain thread); heavy work runs on the drain thread |
| Backpressure / overflow / dropped-event counting | `EventQueue.TryEnqueue` returns false when full; `SessionContext.RecordDropped` |
| Event-consumer contract (extension point for M3–M8) | `Core/Engine/INormalizedEventConsumer.cs`, `Core/Engine/NormalizedEventDispatcher.cs` |
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
- **Callbacks never block.** `Ingest` stamps and enqueues using only `Interlocked` operations
  and concurrent collections; all processing is deferred to the drain worker
  (`DiagnosticRuntime.DrainLoop`). No user-level lock is taken on the callback path and the
  callback never waits on the drain thread.
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
| A9 | "Sequence gaps" implied to be checked automatically | The detector existed but was not wired. Phase 2 wires per-stream gap detection (`NormalizedRecord.SourceSequence` + `StreamKey`) into `EventIngestPipeline`, and any gap surfaces as `M0.EVENT_INTEGRITY = FAIL`. | `EventIngestPipeline`, `SequenceGapDetector` |
| A10 | Timestamp handling limited to stamping | Implausible future source timestamps (beyond 5 min clock skew) are counted as `TIMESTAMP_INVALID` and surface as `M0.EVENT_INTEGRITY = FAIL`. | `EventIngestPipeline`, `DiagnosticEngine` |
| A11 | Duplicate identity was (record type + timestamp) | A collision-safe identity now combines stream key, source sequence, source timestamp, and a content fingerprint, so distinct simultaneous events are never discarded. | `NormalizedRecord.DuplicateIdentity`, `EventIngestPipeline` |
| A12 | "Lock-free" was claimed without basis | The duplicate and gap detectors were lock-based. They are now genuinely non-blocking (lock-free CAS / try-acquire), and the documentation states a *non-blocking* guarantee — not a formal wait-free one — verified by contention tests. | `DuplicateEventDetector`, `SequenceGapDetector`, `DiagnosticRuntime.Ingest` |
| A13 | Processed events were counted but not delivered | `DiagnosticRuntime.Process` now dispatches payloads to registered `INormalizedEventConsumer` instances; Phase 2 registers none (M3–M8 deferral), but the contract is defined and tested. | `INormalizedEventConsumer`, `NormalizedEventDispatcher` |

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
   stamp session/time/sequence (Interlocked)
   duplicate check (bounded, non-blocking)
   sequence-gap check (non-blocking CAS)
   queue.TryEnqueue (non-blocking) ──▶ EventQueue (bounded ring)
   update session counters (Interlocked)  │
   return immediately                     ▼
                                     Drain(buffer) → Process(record)
                                        → dispatcher.Dispatch(record)
                                     (heavy work, off the callback path)
```

**Concurrency guarantees (measured/verified, no stronger claim).** The callback path
(`Ingest`) performs only `Interlocked` operations, `ConcurrentQueue`/`ConcurrentDictionary`
calls, and a bounded lock-free retry loop. It **does not take a user-level lock** and does
**not** wait on the drain thread. The internal collections (`ConcurrentQueue`,
`ConcurrentDictionary`) use fine-grained internal synchronisation; they are lock-free from
the caller's perspective — no caller-supplied monitor is held and no caller blocks on another
thread's progress. Where two threads contend on the same key, the loser retries rather than
waiting. This is a *non-blocking* guarantee, not a formal wait-free guarantee, and it is
verified by the contention tests in `ThreadSafetyTests.cs`, not by benchmark measurements.

- The queue is bounded; when full, `TryEnqueue` returns `false`, the session records a drop,
  and the drop surfaces as `M0.EVENT_INTEGRITY = FAIL` (fail-closed — never a silent PASS).
- Duplicate identity is collision-safe: it combines the stream key, the source sequence (when
  present), the source timestamp, and a content fingerprint of the record's distinguishing
  fields. Two different instruments, two different streams (quote/trade/DOM), or two different
  payloads at the same timestamp are never collapsed into one another. See §3 (A11).
- Source-reported per-stream sequence numbers (`NormalizedRecord.SourceSequence`, keyed by
  `StreamKey`) are checked on ingest; a gap increments the session gap counter and surfaces
  as `M0.EVENT_INTEGRITY = FAIL`. Streams are isolated, so a gap in one never masks another.
- Implausible future source timestamps (> 5 min clock skew) are counted as `TIMESTAMP_INVALID`
  and also surface as `M0.EVENT_INTEGRITY = FAIL`.
- Processed payloads are handed to registered `INormalizedEventConsumer` instances on the
  drain thread (see §5.1). Phase 2 registers none, so this is a no-op extension point.
- `DiagnosticRuntime.Stop` and `Dispose` both perform a **final drain** so shutdown does not
  silently abandon queued records.
- `Stop`/`Dispose` join the worker with a 5-second bound so a stuck worker cannot hang the host.

### 5.1 Event-consumer contract (Phase 2 extension point)

`DiagnosticRuntime.Process` no longer only increments a counter: after counting the record it
dispatches the payload to every registered `INormalizedEventConsumer` via
`NormalizedEventDispatcher`. Phase 2 intentionally ships **no** consumers — the M3–M8 modules
are future work — so the delivered behaviour is unchanged, but the contract they will use is
defined and tested now.

| Aspect | Contract |
|---|---|
| Where it runs | The single drain thread — never a NinjaTrader callback thread |
| Thread safety | A consumer must be safe against concurrent reads of shared session state |
| Blocking | A consumer must not block indefinitely; heavy work belongs here, off the callback path |
| Errors | A consumer must not throw to signal a normal condition; the dispatcher contains any exception, counts it (`ConsumerErrorCount`), and continues delivering to other consumers |
| Safety | Strictly read-only: no order submit/change/cancel/close, no account/connection/position mutation |
| Registration | `DiagnosticRuntime.RegisterEventConsumer(...)`; safe before or after `Start` (copy-on-write) |

`NormalizedEventDispatcher` is copy-on-write for registration and contains per-consumer
exceptions, so one faulty module can never abort the drain loop or starve the others.

---

## 6. Verification

### 6.1 Offline build (executed)

```
dotnet build src/RiskPilot.Diagnostics.Nt8/RiskPilot.Diagnostics.Nt8.csproj -p:UseStubs=true
→ Build succeeded. 0 Warning(s), 0 Error(s)
```

A clean build produces no warnings. The stub-only event in `Stubs/NinjaTraderStubs.cs` carries a
`#pragma warning disable 67` so the offline stub surface stays warning-free. (An earlier
revision of this document claimed one warning; that count did not match the actual build
output and has been reconciled to 0.)

### 6.2 Unit tests (executed)

```
dotnet test tests/RiskPilot.Diagnostics.Nt8.Tests/RiskPilot.Diagnostics.Nt8.Tests.csproj
→ Passed! Failed: 0, Passed: 115, Skipped: 0, Total: 115
```

| Required test area | Test file |
|---|---|
| Thread safety (incl. lock-free detectors under contention) | `ThreadSafetyTests.cs` |
| Queue overflow | `EventQueueTests.cs` |
| Duplicate event (incl. cross-instrument/stream collision safety) | `DuplicateEventTests.cs` |
| Sequence gap (unit + end-to-end, per-stream isolation) | `SequenceGapTests.cs` |
| Timestamp handling (skew flagging + M0 propagation) | `TimestampTests.cs` |
| Session isolation | `SessionIsolationTests.cs` |
| Serialization | `SerializationTests.cs` |
| SHA-256 manifest | `ManifestTests.cs` |
| Secret redaction | `SecretRedactionTests.cs` |
| Shutdown / Dispose | `LifecycleTests.cs` |
| Error classification | `ErrorClassificationTests.cs` |
| Status propagation | `StatusPropagationTests.cs` |
| Probe behaviour (M1/M2) | `ProbeTests.cs` |
| Event-consumer contract + error containment | `EventConsumerTests.cs` |

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
| Real NT8 build + runtime smoke | Next | Requires a Windows host with NT8 8.1.9.0 (minimum 8.1.8.4) |
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
- Pull request: **`Ai00120/RiskPilot.BOOT#1`** (`main` ← `phase2/foundation-diagnostic-core`).
- Commit history (preserved as-is; nothing squashed or rewritten):
  1. `8ef688a4ea968a68654a986b9bc94b4b715ab931` — feat(phase2): standalone read-only NT8 diagnostic core (RP-DIAG-01)
  2. `75a7d7b3b965d48170da506ab74e3e6a0316add0` — docs(phase2): record commit SHA and branch status
  3. `0c606efe08674f0c903d3e41565966ca40ac4820` — fix(phase2): wire sequence-gap + timestamp validation into ingest path
  4. (this review) — fix(phase2): collision-safe duplicate identity, non-blocking ingest path, event-consumer contract
- The branch is pushed to `origin`; the PR is open. **No merge has been performed.**

---

## 10. Independent-review corrections (2026-11-12)

A focused technical review of PR #1 raised findings that were investigated and resolved on the
same branch. Each correction has a regression test; none of them change the read-only safety
posture or the real-host-verification status.

| # | Finding | Root cause | Correction |
|---|---|---|---|
| 1 | Duplicate detection could suppress legitimate simultaneous events | `BuildDuplicateKey` used only (record type + source timestamp), so two different instruments, or a quote and a trade, sharing a timestamp collided | `NormalizedRecord.DuplicateIdentity()` now composes stream key + source sequence + source timestamp + content fingerprint; payload records override `ContentFingerprint()` |
| 2 | Documentation claimed a lock-free ingest path while detectors/runtime used locks | `DuplicateEventDetector`/`SequenceGapDetector` used `lock`; `DiagnosticRuntime.Ingest` took `_gate` | Both detectors rewritten non-blocking (CAS / try-acquire); `Ingest` reads volatile fields without `_gate`; documentation now states a *non-blocking* guarantee and points to contention tests |
| 3 | `Process` counted events but delivered no payload | No consumer abstraction existed | Added `INormalizedEventConsumer` + `NormalizedEventDispatcher`; `Process` dispatches with per-consumer error containment; Phase 2 registers no consumers (documented deferral) |
| 4 | Documentation inconsistency | §9 stale; warning/test counts outdated | §9 updated to the real branch/PR/commits; warning count reconciled to 0; test count reconciled to 115 |
| 5 | Verification | — | Full offline build, all tests, and the static scan re-run; diff reviewed; BOT confirmed untouched |

**Remaining limitations after the corrections (unchanged in substance):**

- Real NinjaTrader 8.1.9.0 host build and runtime remain **unverified** (minimum supported 8.1.8.4).
- The lock-free guarantee is *non-blocking*, not formally wait-free, and is evidenced by
  contention tests rather than benchmark measurements.
- Duplicate detection remains a bounded-window approximation: an identity evicted from the
  window may be treated as new again. Genuine duplicates inside the window are always caught.
- Only M1/M2 probes exist; the event-consumer contract is delivered but no M3–M8 consumer is.
- No BOT integration; `IReadOnlyDiagnosticSource` is declared but unimplemented on the BOT side.
