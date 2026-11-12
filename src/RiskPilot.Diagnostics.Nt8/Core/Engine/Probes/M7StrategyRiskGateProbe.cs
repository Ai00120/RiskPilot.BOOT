using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Probes
{
    /// <summary>
    /// M7 — Strategies &amp; risk gate. Reports each registered strategy's evaluation state
    /// (valid / invalid / unavailable, missing capability), the Stage 1 candidate rejection
    /// distribution, Stage 3 zero-quantity and sizing reasons, Stage 4 risk-gate vetoes and
    /// their reasons, and the Stage 2→3→4 correlation.
    /// <para>
    /// Every figure is a raw observation. The module does not treat an observed baseline
    /// figure as explained: it shows the raw evidence and the event linkage so a reader can
    /// attribute it.
    /// </para>
    /// </summary>
    public sealed class M7StrategyRiskGateProbe : IDiagnosticProbe
    {
        /// <summary>The five registered strategy ids, as observed in RiskPilot.BOT source.</summary>
        public static readonly string[] RegisteredStrategies =
        {
            "STRAT_SCALPING_CANDIDATE_01",
            "STRAT_TREND_PULLBACK_01",
            "STRAT_BREAKOUT_CONTINUATION_01",
            "STRAT_LEVEL_AWARE_REVERSAL_01",
            "STRAT_ORDER_FLOW_INTRADAY_01"
        };

        public string ModuleId { get { return "M7"; } }

        public string Title { get { return "Strategies / Risk Gate"; } }

        public IReadOnlyList<DiagnosticResult> Evaluate(ProbeContext context)
        {
            var results = new List<DiagnosticResult>();
            var now = context.Clock.UtcNow;
            var correlation = context.CorrelationId;
            var snapshot = context.Snapshot;
            var observation = snapshot != null ? snapshot.StrategyRiskGate : null;

            if (observation == null)
            {
                results.Add(FindingFactory.Unobservable(ModuleId, "M7.STRATEGY_SURFACE", correlation, now,
                    "No strategy/risk-gate observation was supplied by the host layer.",
                    "Run the pass while the strategy pipeline is evaluating."));
                return results;
            }

            results.Add(BuildStrategySurfaceCheck(observation, correlation, now));
            results.Add(BuildPerStrategyCheck(observation, correlation, now));
            results.Add(BuildCandidateRejectionCheck(observation, correlation, now));
            results.Add(BuildStage3Check(observation, correlation, now));
            results.Add(BuildStage4Check(observation, correlation, now));
            results.Add(BuildCorrelationCheck(observation, correlation, now));

            return results;
        }

        private DiagnosticResult BuildStrategySurfaceCheck(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            if (!observation.StrategySurfaceObservable)
            {
                return FindingFactory.Unobservable(ModuleId, "M7.STRATEGY_SURFACE", correlation, now,
                    "The strategy/risk surface was not observable.",
                    "Run the pass while the strategy pipeline is active.");
            }

            return DiagnosticResult
                .Pass(ModuleId, "M7.STRATEGY_SURFACE", correlation, now)
                .WithFindingId("M7.STRATEGY_SURFACE")
                .WithReasonCode("STRATEGY_SURFACE_OBSERVED", "STRATEGIES=" + observation.Strategies.Count)
                .WithObservation("strategies=" + observation.Strategies.Count)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Read the strategy evaluation observations and risk-gate reason counters.")
                .WithLimitation("Only strategies observed while the tool ran are reported.")
                .WithNextAction("None.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "strategies=" + observation.Strategies.Count, now));
        }

        private DiagnosticResult BuildPerStrategyCheck(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            var evidence = new List<DiagnosticEvidence>();
            var available = 0;
            var unavailable = 0;
            var missingCapability = 0;

            // Report every registered strategy, whether or not it was observed, so a missing
            // one is visible rather than silently absent.
            var observed = new Dictionary<string, StrategyEvaluationObservation>(StringComparer.OrdinalIgnoreCase);
            foreach (var strategy in observation.Strategies)
            {
                if (strategy != null && !string.IsNullOrEmpty(strategy.StrategyId))
                {
                    observed[strategy.StrategyId] = strategy;
                }
            }

            foreach (var id in RegisteredStrategies)
            {
                if (!observed.TryGetValue(id, out var strategy))
                {
                    evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "strategy=" + id + " state=NOT_OBSERVED", now));
                    continue;
                }

                if (strategy.Available)
                {
                    available++;
                }
                else
                {
                    unavailable++;
                }

                if (strategy.MissingCapability)
                {
                    missingCapability++;
                }

                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters,
                    "strategy=" + id
                    + " activity=" + strategy.Activity.ToWire()
                    + " available=" + strategy.Available
                    + " missingCapability=" + strategy.MissingCapability
                    + " candidates=" + strategy.CandidateCount
                    + " valid=" + strategy.ValidCandidateCount
                    + " invalid=" + strategy.InvalidCandidateCount
                    + (string.IsNullOrEmpty(strategy.UnavailableReason) ? string.Empty : " reason=" + strategy.UnavailableReason), now));
            }

            return DiagnosticResult
                .Inconclusive(ModuleId, "M7.STRATEGY_EVALUATION", correlation, now)
                .WithFindingId("M7.STRATEGY_EVALUATION")
                .WithReasonCode(
                    "AVAILABLE=" + available,
                    "UNAVAILABLE=" + unavailable,
                    "MISSING_CAPABILITY=" + missingCapability,
                    "REGISTERED=" + RegisteredStrategies.Length)
                .WithObservation("registered=" + RegisteredStrategies.Length + " available=" + available
                    + " unavailable=" + unavailable + " missingCapability=" + missingCapability)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Reported each registered strategy's evaluation state and reason codes.")
                .WithLimitation("A strategy that was not observed is reported as NOT_OBSERVED, never assumed active.")
                .WithNextAction(unavailable > 0
                    ? "Correlate each unavailable strategy's reason with the capability it requires."
                    : "None.")
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildCandidateRejectionCheck(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            var evidence = new List<DiagnosticEvidence>();
            foreach (var pair in observation.CandidateRejectionReasons)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "rejectionReason=" + pair.Key + "=" + pair.Value, now));
            }

            evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "allCandidatesInvalid=" + observation.AllCandidatesInvalidCount, now));

            return DiagnosticResult
                .Inconclusive(ModuleId, "M7.CANDIDATE_REJECTION", correlation, now)
                .WithFindingId("M7.CANDIDATE_REJECTION")
                .WithReasonCode("ALL_CANDIDATES_INVALID=" + observation.AllCandidatesInvalidCount, "DISTINCT_REASONS=" + observation.CandidateRejectionReasons.Count)
                .WithObservation("allCandidatesInvalid=" + observation.AllCandidatesInvalidCount + " distinctReasons=" + observation.CandidateRejectionReasons.Count)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Aggregated the Stage 1 candidate rejection reason distribution.")
                .WithLimitation("Raw counts only; a large count is not treated as explained until each reason is attributed.")
                .WithNextAction("Review the top rejection reasons and correlate them with the capability/market-data state at the same time.")
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildStage3Check(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            var evidence = new List<DiagnosticEvidence>();
            foreach (var pair in observation.Stage3ReasonCodes)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "stage3Reason=" + pair.Key + "=" + pair.Value, now));
            }

            evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "stage3ZeroQuantity=" + observation.Stage3ZeroQuantityCount, now));

            return DiagnosticResult
                .Inconclusive(ModuleId, "M7.STAGE3_SIZING", correlation, now)
                .WithFindingId("M7.STAGE3_SIZING")
                .WithReasonCode("STAGE3_ZERO_QUANTITY=" + observation.Stage3ZeroQuantityCount, "DISTINCT_REASONS=" + observation.Stage3ReasonCodes.Count)
                .WithObservation("zeroQuantity=" + observation.Stage3ZeroQuantityCount + " distinctReasons=" + observation.Stage3ReasonCodes.Count)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Aggregated the Stage 3 sizing reason distribution and the zero-quantity count.")
                .WithLimitation("A zero quantity is a designed fail-closed outcome; it is reported raw and not treated as an error by itself.")
                .WithNextAction("Correlate the zero-quantity reasons with the instrument-specification and account-state evidence.")
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildStage4Check(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            var evidence = new List<DiagnosticEvidence>();
            foreach (var pair in observation.RiskGateVetoReasons)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "vetoReason=" + pair.Key + "=" + pair.Value, now));
            }

            evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "riskGateVeto=" + observation.RiskGateVetoCount, now));

            return DiagnosticResult
                .Inconclusive(ModuleId, "M7.RISK_GATE", correlation, now)
                .WithFindingId("M7.RISK_GATE")
                .WithReasonCode("VETOES=" + observation.RiskGateVetoCount, "DISTINCT_REASONS=" + observation.RiskGateVetoReasons.Count)
                .WithObservation("vetoes=" + observation.RiskGateVetoCount + " distinctReasons=" + observation.RiskGateVetoReasons.Count)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Aggregated the Stage 4 risk-gate veto reason distribution.")
                .WithLimitation("A veto is a designed fail-closed outcome; the raw reason distribution is the evidence.")
                .WithNextAction("Attribute the top veto reasons (for example ANALYTICAL_NO_TRADE, MISSING_INSTRUMENT_SPECIFICATION) to their causes.")
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildCorrelationCheck(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            var stage = observation.Correlation ?? new StageCorrelationObservation();
            var complete = stage.CorrelationComplete;

            return (complete
                    ? DiagnosticResult.Pass(ModuleId, "M7.STAGE_CORRELATION", correlation, now)
                    : DiagnosticResult.Inconclusive(ModuleId, "M7.STAGE_CORRELATION", correlation, now))
                .WithFindingId("M7.STAGE_CORRELATION")
                .WithReasonCode(complete ? "CORRELATION_COMPLETE" : "CORRELATION_INCOMPLETE",
                    string.IsNullOrEmpty(stage.CorrelationGapReason) ? "NO_GAP_REASON" : stage.CorrelationGapReason)
                .WithObservation("decision=" + Presence(stage.DecisionId)
                    + " proposal=" + Presence(stage.ProposalId)
                    + " riskEvaluation=" + Presence(stage.RiskEvaluationId))
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Correlated the Stage 2 decision id, Stage 3 proposal id and Stage 4 risk-evaluation id.")
                .WithLimitation("Correlation is only PASS when all three ids are present and consistent; otherwise it stays INCONCLUSIVE.")
                .WithNextAction(complete
                    ? "None."
                    : "Capture the missing correlation id to complete the decision→proposal→risk linkage.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "decision=" + Presence(stage.DecisionId), now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "proposal=" + Presence(stage.ProposalId), now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "riskEvaluation=" + Presence(stage.RiskEvaluationId), now));
        }

        private static string Presence(string value)
        {
            return string.IsNullOrEmpty(value) ? "ABSENT" : "PRESENT";
        }
    }
}
