using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Probes
{
    /// <summary>
    /// M6 — TradeDelta &amp; OrderFlow. Reports the trade-direction source (provider-supplied
    /// versus quote-derived versus unknown), the unknown-direction causes (including
    /// inside-spread cases), TradeDelta completeness, reset handling and sequence continuity.
    /// <para>
    /// A direction derived by comparing the print to a quote is always labelled *derived* and
    /// is never presented as provider-confirmed. The apparent tension between "OrderFlow
    /// Available in the heartbeat" and "strategy skipped on RESET_OPERATION_RECEIVED" is
    /// reported as two separate observations with their scope and time; the module does not
    /// declare a contradiction or a resolution without evidence.
    /// </para>
    /// </summary>
    public sealed class M6TradeDeltaProbe : IDiagnosticProbe
    {
        public string ModuleId { get { return "M6"; } }

        public string Title { get { return "TradeDelta / OrderFlow"; } }

        public IReadOnlyList<DiagnosticResult> Evaluate(ProbeContext context)
        {
            var results = new List<DiagnosticResult>();
            var now = context.Clock.UtcNow;
            var correlation = context.CorrelationId;
            var snapshot = context.Snapshot;
            var observation = snapshot != null ? snapshot.TradeDelta : null;

            if (observation == null)
            {
                results.Add(FindingFactory.Unobservable(ModuleId, "M6.DIRECTION_SOURCE", correlation, now,
                    "No TradeDelta/OrderFlow observation was supplied by the host layer.",
                    "Run the pass while the trade stream and order-flow engine are active."));
                return results;
            }

            results.Add(BuildDirectionSourceCheck(observation, correlation, now));
            results.Add(BuildUnknownDirectionCheck(observation, correlation, now));
            results.Add(BuildTradeDeltaCompletenessCheck(observation, correlation, now));
            results.Add(BuildResetCheck(observation, correlation, now));
            results.Add(BuildSequenceContinuityCheck(observation, correlation, now));
            results.Add(BuildApparentTensionCheck(observation, correlation, now));

            return results;
        }

        private DiagnosticResult BuildDirectionSourceCheck(TradeDeltaObservation observation, string correlation, DateTime now)
        {
            var total = observation.BuyDerivedCount + observation.SellDerivedCount + observation.UnknownCount;
            if (total == 0)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M6.DIRECTION_SOURCE", correlation, now)
                    .WithFindingId("M6.DIRECTION_SOURCE")
                    .WithReasonCode("NO_TRADES_OBSERVED")
                    .WithObservation("No trade print was observed.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Counted trade-direction provenance across the observed trade stream.")
                    .WithLimitation("No trade print means direction provenance cannot be characterised.")
                    .WithNextAction("Run the pass while trades are printing.");
            }

            // The provenance is what matters: quote-derived is an inference, not a fact.
            var provenance = observation.AnyProviderSuppliedDirection
                ? "MIXED_OR_PROVIDER_SUPPLIED"
                : (observation.AllDirectionsQuoteDerived ? "ALL_QUOTE_DERIVED" : "UNKNOWN");

            return DiagnosticResult
                .Inconclusive(ModuleId, "M6.DIRECTION_SOURCE", correlation, now)
                .WithFindingId("M6.DIRECTION_SOURCE")
                .WithReasonCode(
                    "PROVENANCE=" + provenance,
                    "BUY_DERIVED=" + observation.BuyDerivedCount,
                    "SELL_DERIVED=" + observation.SellDerivedCount,
                    "UNKNOWN=" + observation.UnknownCount)
                .WithObservation("provenance=" + provenance + " buyDerived=" + observation.BuyDerivedCount
                    + " sellDerived=" + observation.SellDerivedCount + " unknown=" + observation.UnknownCount)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Recorded whether each direction was provider-supplied or derived from a quote comparison.")
                .WithLimitation("A quote-derived direction is an inference, not provider-confirmed evidence; it must never be reported as confirmed.")
                .WithNextAction("None; the provenance label is the evidence.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "provenance=" + provenance, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "buyDerived=" + observation.BuyDerivedCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "sellDerived=" + observation.SellDerivedCount, now));
        }

        private DiagnosticResult BuildUnknownDirectionCheck(TradeDeltaObservation observation, string correlation, DateTime now)
        {
            var total = observation.BuyDerivedCount + observation.SellDerivedCount + observation.UnknownCount;
            if (total == 0)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M6.UNKNOWN_DIRECTION", correlation, now)
                    .WithFindingId("M6.UNKNOWN_DIRECTION")
                    .WithReasonCode("NO_TRADES_OBSERVED")
                    .WithObservation("No trade print was observed.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("Counted unknown-direction trades and their causes.")
                    .WithLimitation("No trade print means the unknown-direction rate is undefined.")
                    .WithNextAction("Run the pass while trades are printing.");
            }

            var rate = (double)observation.UnknownCount / total * 100.0;
            return DiagnosticResult
                .Inconclusive(ModuleId, "M6.UNKNOWN_DIRECTION", correlation, now)
                .WithFindingId("M6.UNKNOWN_DIRECTION")
                .WithReasonCode(
                    "UNKNOWN=" + observation.UnknownCount,
                    "UNKNOWN_RATE_PCT=" + rate.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    "INSIDE_SPREAD=" + observation.UnknownInsideSpreadCount,
                    "NO_BBO=" + observation.UnknownNoBboCount,
                    "BBO_INCOMPLETE=" + observation.UnknownBboIncompleteCount,
                    "BBO_INVALID=" + observation.UnknownBboInvalidCount,
                    "PROVIDER_REPORTED_UNKNOWN=" + observation.UnknownProviderReportedCount)
                .WithObservation("unknown=" + observation.UnknownCount
                    + " ratePct=" + rate.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + " insideSpread=" + observation.UnknownInsideSpreadCount
                    + " noBbo=" + observation.UnknownNoBboCount
                    + " bboIncomplete=" + observation.UnknownBboIncompleteCount
                    + " bboInvalid=" + observation.UnknownBboInvalidCount)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Attributed each unknown direction to a specific cause (inside spread, no BBO, incomplete BBO, invalid BBO, provider-unknown).")
                .WithLimitation("The unknown-direction rate is reported, never eliminated; an unknown direction is never filled in with a guess.")
                .WithNextAction(observation.UnknownCount > 0
                    ? "Correlate the unknown trades against the BBO availability window reported by M5."
                    : "None.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "unknownRatePct=" + rate.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "insideSpread=" + observation.UnknownInsideSpreadCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "noBbo=" + observation.UnknownNoBboCount, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "bboIncomplete=" + observation.UnknownBboIncompleteCount, now));
        }

        private DiagnosticResult BuildTradeDeltaCompletenessCheck(TradeDeltaObservation observation, string correlation, DateTime now)
        {
            var completeness = observation.TradeDeltaCompleteness ?? "UNOBSERVABLE";
            var status = completeness == "COMPLETE" ? DiagnosticStatus.PASS
                : (completeness == "UNOBSERVABLE" ? DiagnosticStatus.INCONCLUSIVE : DiagnosticStatus.INCONCLUSIVE);

            return (status == DiagnosticStatus.PASS
                    ? DiagnosticResult.Pass(ModuleId, "M6.TRADEDELTA_COMPLETENESS", correlation, now)
                    : DiagnosticResult.Inconclusive(ModuleId, "M6.TRADEDELTA_COMPLETENESS", correlation, now))
                .WithFindingId("M6.TRADEDELTA_COMPLETENESS")
                .WithReasonCode("COMPLETENESS=" + completeness,
                    string.IsNullOrEmpty(observation.TradeDeltaUnavailableReason) ? "NO_REASON_REPORTED" : observation.TradeDeltaUnavailableReason)
                .WithObservation("completeness=" + completeness
                    + (string.IsNullOrEmpty(observation.TradeDeltaUnavailableReason) ? string.Empty : " reason=" + observation.TradeDeltaUnavailableReason))
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Read the TradeDelta completeness state and its reported reason.")
                .WithLimitation("An incomplete TradeDelta cannot be used as if it were complete; the tool never repairs it.")
                .WithNextAction(completeness == "COMPLETE"
                    ? "None."
                    : "Investigate the reported incompleteness reason before relying on TradeDelta.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "completeness=" + completeness, now));
        }

        private DiagnosticResult BuildResetCheck(TradeDeltaObservation observation, string correlation, DateTime now)
        {
            var reset = observation.ResetObserved;
            var count = observation.ResetCount;

            return DiagnosticResult
                .Inconclusive(ModuleId, "M6.RESET_HANDLING", correlation, now)
                .WithFindingId("M6.RESET_HANDLING")
                .WithReasonCode(reset ? "RESET_OPERATION_RECEIVED" : "NO_RESET_OBSERVED", "RESET_COUNT=" + count)
                .WithObservation("resetObserved=" + reset + " resetCount=" + count)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Observed DOM/stream RESET operations and their effect on order-flow state.")
                .WithLimitation("A reset invalidates the derived order-flow state; the tool reports the reset, it never re-derives a state that assumed continuity.")
                .WithNextAction(reset
                    ? "Treat the order-flow state after the reset as re-based; correlate with the strategy-skip observation below."
                    : "None.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "resetObserved=" + reset, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "resetCount=" + count, now));
        }

        private DiagnosticResult BuildSequenceContinuityCheck(TradeDeltaObservation observation, string correlation, DateTime now)
        {
            var gaps = observation.SequenceGapCount;
            return (gaps == 0
                    ? DiagnosticResult.Pass(ModuleId, "M6.SEQUENCE_CONTINUITY", correlation, now)
                    : DiagnosticResult.Fail(ModuleId, "M6.SEQUENCE_CONTINUITY", ErrorClass.SequenceGap, correlation, now))
                .WithFindingId("M6.SEQUENCE_CONTINUITY")
                .WithReasonCode("SEQUENCE_GAPS=" + gaps, "SOURCE_TIME_BASIS=" + observation.SourceTimeBasis.ToWire())
                .WithObservation("sequenceGaps=" + gaps + " sourceTimeBasis=" + observation.SourceTimeBasis.ToWire())
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Per-stream sequence-gap detection on the order-flow stream.")
                .WithLimitation("Source timestamps are server-time-unverified, so sequence continuity is the stronger continuity signal.")
                .WithNextAction(gaps == 0
                    ? "None."
                    : "Treat the affected order-flow window as incomplete.");
        }

        private DiagnosticResult BuildApparentTensionCheck(TradeDeltaObservation observation, string correlation, DateTime now)
        {
            // The recorded baseline shows "OrderFlow Available" in a heartbeat while the
            // OrderFlow strategy was skipped on RESET_OPERATION_RECEIVED. These are two
            // different scopes (engine readiness vs. a per-strategy decision at a specific
            // time). The tool reports both raw observations with their scope and does NOT
            // declare a contradiction or a resolution without evidence.
            var readiness = observation.OrderFlowReadiness ?? "UNOBSERVABLE";
            var resetObserved = observation.ResetObserved;

            return DiagnosticResult
                .Inconclusive(ModuleId, "M6.ORDERFLOW_READINESS_VS_STRATEGY_SKIP", correlation, now)
                .WithFindingId("M6.ORDERFLOW_READINESS_VS_STRATEGY_SKIP")
                .WithReasonCode(
                    "ORDERFLOW_READINESS=" + readiness,
                    resetObserved ? "RESET_OPERATION_RECEIVED" : "NO_RESET_OBSERVED",
                    "SCOPE=ENGINE_READINESS_VS_STRATEGY_DECISION",
                    "NOT_DECLARED_CONTRADICTION")
                .WithObservation("orderFlowReadiness=" + readiness
                    + " resetObserved=" + resetObserved
                    + " note=two different scopes (engine readiness vs per-strategy decision at a time); no contradiction is declared without evidence.")
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Reported the engine readiness state and the reset/strategy-skip evidence as separate observations with their scope.")
                .WithLimitation("The two observations come from different scopes and different times; the tool does not declare a contradiction or a resolution without evidence.")
                .WithNextAction("Correlate the readiness heartbeat timestamp with the strategy-skip timestamp to see whether the reset preceded the skip.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "orderFlowReadiness=" + readiness, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "resetObserved=" + resetObserved, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "scope=ENGINE_READINESS_VS_STRATEGY_DECISION", now));
        }
    }
}
