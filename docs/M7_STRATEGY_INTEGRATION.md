# M7 Strategy Diagnostics — External Integration Contract

BOOT's **M7 (Strategies / Risk Gate)** module can only report what a real strategy diagnostic
producer observes. **BOOT ships no such producer**: no public NinjaTrader 8 API exposes strategy
internals, and BOOT must not implement BOT's strategy or risk logic, must not depend on BOT
binaries, and must not fabricate a strategy observation. This document records the neutral,
read-only boundary and the exact responsibility of a future external producer.

## What BOOT does today

- The only source of strategy evidence is the read-only contract
  `Core/Contract/IReadOnlyDiagnosticSource.ReadStrategies()`.
- `DiagnosticAddOn.StrategyDiagnosticSource` is an optional property. **It is null by default**, so
  in a stock BOOT installation no strategy records are ever ingested.
- With no records, `StrategyRiskGateObservation.StrategySurfaceObservable` is false and every M7
  check reports `INCONCLUSIVE` with a reason code that explains why:
  `M7.STRATEGY_SURFACE` (`STRATEGY_SURFACE_UNOBSERVABLE`), `M7.STRATEGY_EVALUATION`,
  `M7.CANDIDATE_REJECTION`, `M7.STAGE3_SIZING`, `M7.RISK_GATE`, `M7.STAGE_CORRELATION`,
  `M7.REASON_ATTRIBUTION`.
- `M7.STRATEGY_EVALUATION` lists each registered strategy as `NOT_OBSERVED`; a strategy that was not
  observed is never assumed inactive or available.

BOOT is independently usable with or without a producer: M7 simply stays INCONCLUSIVE.

## The read-only boundary

A producer must implement **only** this contract. It is strictly read-only: it returns data and
cannot mutate host or RiskPilot state.

```csharp
public interface IReadOnlyDiagnosticSource
{
    string SourceId { get; }                               // e.g. "RiskPilot.BOT"
    IReadOnlyList<DiagnosticResult> ReadResults();         // normalized results, may be empty
    IReadOnlyList<StrategyRecord> ReadStrategies();        // strategy observations, may be empty
}
```

Each `StrategyRecord` carries only `StrategyId` and `ReasonCodes` as guaranteed fields. Every other
field is optional and paired with its own `*Observable` flag, so a producer that does not report a
value leaves the flag false and BOOT reports it `UNOBSERVABLE` rather than presenting the default:

| Field | Observable flag | Meaning |
| --- | --- | --- |
| `Activity` | `ActivityObservable` | the strategy's state this pass |
| `CandidateCount` / `ValidCandidateCount` / `InvalidCandidateCount` | `CandidateCountsObservable` | candidate counts |
| `MissingCapability` | `MissingCapabilityObservable` | skipped for a missing required capability |
| `Available` | `AvailableObservable` | evaluated and available |
| `UnavailableReason` | (reported with `Available`) | why it was unavailable |
| `DecisionId` / `ProposalId` / `RiskEvaluationId` | `CorrelationObservable` | the Stage 2→3→4 correlation ids |

`ReasonCodes` must use the published stage catalog (`Core/Engine/StrategyReasonCodes`). A code the
catalog cannot place — unknown **or** ambiguous across stages (for example `FLAT_DIRECTION`) — is
reported **unattributed**, never guessed into a stage.

## Producer responsibilities

1. Implement `IReadOnlyDiagnosticSource` **outside** this repository (for example in RiskPilot.BOT)
   and assign it to `DiagnosticAddOn.StrategyDiagnosticSource` before the first run.
2. Report only values actually observed; leave the matching `*Observable` flag false otherwise.
3. Report the correlation ids only when the Stage 2→3→4 chain is genuinely observable.
4. Never claim a strategy is available or unavailable unless the source observed it.
5. Never throw: a throwing `ReadStrategies()` is contained (BOOT ingests nothing), but it degrades
   M7 to INCONCLUSIVE.

BOOT does **not** implement a BOT-side producer, and does not reference BOT binaries. This boundary
is the only supported integration point.
