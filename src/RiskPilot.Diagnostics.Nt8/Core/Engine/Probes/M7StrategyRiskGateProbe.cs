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
    /// (available / unavailable / missing capability / not observed), the Stage 1/2 candidate
    /// rejection distribution, Stage 3 sizing outcomes, Stage 4 risk-gate vetoes and their
    /// reasons, the Stage 2→3→4 correlation, and any reason code the catalog could not attribute
    /// to a stage.
    /// <para>
    /// Every check is fail-closed: a check is only PASS when the underlying surface was
    /// positively observed, otherwise it is INCONCLUSIVE with a reason code that explains why
    /// the result is inconclusive. A strategy that was not observed is reported NOT_OBSERVED,
    /// never assumed active or inactive. A reason code that is not in the published catalog is
    /// reported as unattributed, never guessed into a stage.
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
            var now = context.Clock.UtcNow;
            var correlation = context.CorrelationId;
            var snapshot = context.Snapshot;
            var observation = snapshot != null ? snapshot.StrategyRiskGate : null;

            // Every check is always emitted, so the report always shows the full M7 check set.
            // When no observation could be made, each check reports why it is inconclusive rather
            // than the module silently collapsing to a single line.
            var results = new List<DiagnosticResult>
            {
                BuildStrategySurfaceCheck(observation, correlation, now),
                BuildPerStrategyCheck(observation, correlation, now),
                BuildCandidateRejectionCheck(observation, correlation, now),
                BuildStage3Check(observation, correlation, now),
                BuildStage4Check(observation, correlation, now),
                BuildCorrelationCheck(observation, correlation, now),
                BuildReasonAttributionCheck(observation, correlation, now)
            };

            return results;
        }

        private DiagnosticResult BuildStrategySurfaceCheck(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            if (observation == null || !observation.StrategySurfaceObservable)
            {
                return Inconclusive(
                    "M7.STRATEGY_SURFACE",
                    correlation,
                    now,
                    "The strategy/risk surface was not observable: no read-only strategy source reported a strategy this pass.",
                    "Run the pass while RiskPilot.BOT is evaluating strategies and exposes its read-only strategy diagnostics.",
                    "STRATEGY_SURFACE_UNOBSERVABLE");
            }

            var strategies = observation.StrategyObservationCount();
            return DiagnosticResult
                .Pass(ModuleId, "M7.STRATEGY_SURFACE", correlation, now)
                .WithFindingId("M7.STRATEGY_SURFACE")
                .WithReasonCode("STRATEGY_SURFACE_OBSERVED", "STRATEGIES=" + strategies)
                .WithObservation("strategies=" + strategies + " records=" + observation.ObservedRecordCount)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Read the strategy evaluation observations supplied by the read-only diagnostic source.")
                .WithLimitation("Only strategies observed while the tool ran are reported; a missing strategy is reported NOT_OBSERVED, never assumed inactive.")
                .WithNextAction("None.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters,
                    "strategies=" + strategies + " records=" + observation.ObservedRecordCount, now));
        }

        private DiagnosticResult BuildPerStrategyCheck(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            var evidence = new List<DiagnosticEvidence>();
            var available = 0;
            var unavailable = 0;
            var missingCapability = 0;
            var notObserved = 0;

            var observed = new Dictionary<string, StrategyEvaluationObservation>(StringComparer.OrdinalIgnoreCase);
            if (observation != null && observation.Strategies != null)
            {
                foreach (var strategy in observation.Strategies)
                {
                    if (strategy != null && !string.IsNullOrEmpty(strategy.StrategyId))
                    {
                        observed[strategy.StrategyId] = strategy;
                    }
                }
            }

            foreach (var id in RegisteredStrategies)
            {
                if (!observed.TryGetValue(id, out var strategy) || !strategy.Observed)
                {
                    notObserved++;
                    evidence.Add(DiagnosticEvidence.Unobservable(FindingFactory.SourceRuntimeCounters, "strategy=" + id + " state=NOT_OBSERVED", now));
                    continue;
                }

                // Availability is only counted when the source actually reported it. A strategy
                // whose availability was not reported is not counted as unavailable.
                if (strategy.AvailableObservable)
                {
                    if (strategy.Available)
                    {
                        available++;
                    }
                    else
                    {
                        unavailable++;
                    }
                }

                if (strategy.MissingCapabilityObservable && strategy.MissingCapability)
                {
                    missingCapability++;
                }

                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters,
                    "strategy=" + id
                    + " activity=" + (strategy.ActivityObservable ? strategy.Activity.ToWire() : "UNOBSERVABLE")
                    + " available=" + (strategy.AvailableObservable ? strategy.Available.ToString() : "UNOBSERVABLE")
                    + " missingCapability=" + (strategy.MissingCapabilityObservable ? strategy.MissingCapability.ToString() : "UNOBSERVABLE")
                    + (strategy.CandidateCountsObservable
                        ? " candidates=" + strategy.CandidateCount + " valid=" + strategy.ValidCandidateCount + " invalid=" + strategy.InvalidCandidateCount
                        : " candidates=UNOBSERVABLE")
                    + (string.IsNullOrEmpty(strategy.UnavailableReason) ? string.Empty : " reason=" + strategy.UnavailableReason), now));
            }

            if (observation == null || !observation.StrategySurfaceObservable)
            {
                return Inconclusive(
                    "M7.STRATEGY_EVALUATION",
                    correlation,
                    now,
                    "No strategy evaluation was observable: the read-only strategy source reported no strategy this pass.",
                    "Run the pass while RiskPilot.BOT is evaluating strategies.",
                    "STRATEGY_EVALUATION_UNOBSERVABLE",
                    "REGISTERED=" + RegisteredStrategies.Length,
                    "NOT_OBSERVED=" + RegisteredStrategies.Length)
                    .WithEvidence(evidence.ToArray());
            }

            // Per-strategy state is a raw observation, so the check stays INCONCLUSIVE (it is a
            // listing, not a pass/fail condition) and carries an explicit reason why: how many
            // registered strategies were not observed this pass.
            return Inconclusive(
                "M7.STRATEGY_EVALUATION",
                correlation,
                now,
                "Observed " + (RegisteredStrategies.Length - notObserved) + " of " + RegisteredStrategies.Length
                    + " registered strategies; " + notObserved + " were not observed this pass (reported NOT_OBSERVED, never assumed inactive).",
                unavailable > 0
                    ? "Correlate each unavailable strategy's reason with the capability it requires."
                    : "None.",
                notObserved > 0 ? "STRATEGY_EVALUATION_PARTIAL" : "STRATEGY_EVALUATION_LISTED",
                "REGISTERED=" + RegisteredStrategies.Length,
                "AVAILABLE=" + available,
                "UNAVAILABLE=" + unavailable,
                "MISSING_CAPABILITY=" + missingCapability,
                "NOT_OBSERVED=" + notObserved)
                .WithObservation("registered=" + RegisteredStrategies.Length + " available=" + available
                    + " unavailable=" + unavailable + " missingCapability=" + missingCapability
                    + " notObserved=" + notObserved)
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildCandidateRejectionCheck(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            if (observation == null || observation.CandidateRejectionsObserved() <= 0)
            {
                return Inconclusive(
                    "M7.CANDIDATE_REJECTION",
                    correlation,
                    now,
                    "No Stage 1/2 candidate rejection reason was observable this pass.",
                    "Run the pass while candidates are being validated, or expose the read-only candidate rejection diagnostics.",
                    "CANDIDATE_REJECTION_UNOBSERVABLE");
            }

            var evidence = new List<DiagnosticEvidence>();
            foreach (var pair in observation.CandidateRejectionReasons)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "rejectionReason=" + pair.Key + "=" + pair.Value, now));
            }

            return Inconclusive(
                "M7.CANDIDATE_REJECTION",
                correlation,
                now,
                "Observed " + observation.CandidateRejectionsObserved() + " candidate rejection(s) across "
                    + observation.CandidateRejectionReasons.Count + " distinct reason(s); raw counts only, not treated as an explained cause.",
                "Review the top rejection reasons and correlate them with the capability/market-data state at the same time.",
                "CANDIDATE_REJECTION_LISTED",
                "CANDIDATE_REJECTIONS=" + observation.CandidateRejectionsObserved(),
                "ALL_CANDIDATES_INVALID=" + observation.AllCandidatesInvalidCount(),
                "DISTINCT_REASONS=" + observation.CandidateRejectionReasons.Count)
                .WithObservation("candidateRejections=" + observation.CandidateRejectionsObserved()
                    + " allCandidatesInvalid=" + observation.AllCandidatesInvalidCount()
                    + " distinctReasons=" + observation.CandidateRejectionReasons.Count)
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildStage3Check(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            if (observation == null || observation.Stage3OutcomesObserved() <= 0)
            {
                return Inconclusive(
                    "M7.STAGE3_SIZING",
                    correlation,
                    now,
                    "No Stage 3 sizing outcome was observable this pass.",
                    "Run the pass while position sizing is being evaluated, or expose the read-only sizing diagnostics.",
                    "STAGE3_SIZING_UNOBSERVABLE");
            }

            var evidence = new List<DiagnosticEvidence>();
            foreach (var pair in observation.Stage3ReasonCodes)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "stage3Reason=" + pair.Key + "=" + pair.Value, now));
            }

            return Inconclusive(
                "M7.STAGE3_SIZING",
                correlation,
                now,
                "Observed " + observation.Stage3OutcomesObserved() + " Stage 3 sizing outcome(s); "
                    + observation.Stage3ZeroQuantityCount() + " produced zero quantity.",
                "Correlate the zero-quantity reasons with the instrument-specification and account-state evidence.",
                "STAGE3_SIZING_LISTED",
                "STAGE3_OUTCOMES=" + observation.Stage3OutcomesObserved(),
                "STAGE3_ZERO_QUANTITY=" + observation.Stage3ZeroQuantityCount(),
                "DISTINCT_REASONS=" + observation.Stage3ReasonCodes.Count)
                .WithObservation("outcomes=" + observation.Stage3OutcomesObserved()
                    + " zeroQuantity=" + observation.Stage3ZeroQuantityCount()
                    + " distinctReasons=" + observation.Stage3ReasonCodes.Count)
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildStage4Check(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            if (observation == null || observation.Stage4EvaluationsObserved() <= 0)
            {
                return Inconclusive(
                    "M7.RISK_GATE",
                    correlation,
                    now,
                    "No Stage 4 risk-gate evaluation was observable this pass.",
                    "Run the pass while proposals are being risk-evaluated, or expose the read-only risk-gate diagnostics.",
                    "RISK_GATE_UNOBSERVABLE");
            }

            var evidence = new List<DiagnosticEvidence>();
            foreach (var pair in observation.RiskGateVetoReasons())
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "vetoReason=" + pair.Key + "=" + pair.Value, now));
            }

            return Inconclusive(
                "M7.RISK_GATE",
                correlation,
                now,
                "Observed " + observation.Stage4EvaluationsObserved() + " Stage 4 risk-gate evaluation(s); "
                    + observation.RiskGateVetoCount() + " were vetoed.",
                "Attribute the top veto reasons to their causes.",
                "RISK_GATE_LISTED",
                "RISK_GATE_EVALUATIONS=" + observation.Stage4EvaluationsObserved(),
                "VETOES=" + observation.RiskGateVetoCount(),
                "DISTINCT_REASONS=" + observation.RiskGateVetoReasons().Count)
                .WithObservation("evaluations=" + observation.Stage4EvaluationsObserved()
                    + " vetoes=" + observation.RiskGateVetoCount()
                    + " distinctReasons=" + observation.RiskGateVetoReasons().Count)
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildCorrelationCheck(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            var stage = observation != null ? observation.Correlation : null;

            if (stage == null || !stage.CorrelationObservable)
            {
                return Inconclusive(
                    "M7.STAGE_CORRELATION",
                    correlation,
                    now,
                    "The Stage 2→3→4 correlation surface was not observable this pass: no decision/proposal/risk-evaluation ids were supplied.",
                    "Supply the Stage 2 decision id, Stage 3 proposal id and Stage 4 risk-evaluation id from the read-only diagnostic source.",
                    "CORRELATION_UNOBSERVABLE",
                    "DECISION=" + Presence(stage != null ? stage.DecisionId : null),
                    "PROPOSAL=" + Presence(stage != null ? stage.ProposalId : null),
                    "RISK_EVALUATION=" + Presence(stage != null ? stage.RiskEvaluationId : null))
                    .WithObservation("decision=" + Presence(stage != null ? stage.DecisionId : null)
                        + " proposal=" + Presence(stage != null ? stage.ProposalId : null)
                        + " riskEvaluation=" + Presence(stage != null ? stage.RiskEvaluationId : null))
                    .WithEvidence(
                        DiagnosticEvidence.Unobservable(FindingFactory.SourceRuntimeCounters, "decision=" + Presence(stage != null ? stage.DecisionId : null), now),
                        DiagnosticEvidence.Unobservable(FindingFactory.SourceRuntimeCounters, "proposal=" + Presence(stage != null ? stage.ProposalId : null), now),
                        DiagnosticEvidence.Unobservable(FindingFactory.SourceRuntimeCounters, "riskEvaluation=" + Presence(stage != null ? stage.RiskEvaluationId : null), now));
            }

            if (!stage.CorrelationComplete)
            {
                return Inconclusive(
                    "M7.STAGE_CORRELATION",
                    correlation,
                    now,
                    "The correlation surface was observed but is incomplete.",
                    "Capture the missing correlation id to complete the decision→proposal→risk linkage.",
                    "CORRELATION_INCOMPLETE",
                    string.IsNullOrEmpty(stage.CorrelationGapReason) ? "NO_GAP_REASON" : stage.CorrelationGapReason,
                    "DECISION=" + Presence(stage.DecisionId),
                    "PROPOSAL=" + Presence(stage.ProposalId),
                    "RISK_EVALUATION=" + Presence(stage.RiskEvaluationId))
                    .WithObservation("decision=" + Presence(stage.DecisionId)
                        + " proposal=" + Presence(stage.ProposalId)
                        + " riskEvaluation=" + Presence(stage.RiskEvaluationId))
                    .WithEvidence(
                        DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "decision=" + Presence(stage.DecisionId), now),
                        DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "proposal=" + Presence(stage.ProposalId), now),
                        DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "riskEvaluation=" + Presence(stage.RiskEvaluationId), now));
            }

            return DiagnosticResult
                .Pass(ModuleId, "M7.STAGE_CORRELATION", correlation, now)
                .WithFindingId("M7.STAGE_CORRELATION")
                .WithReasonCode("CORRELATION_COMPLETE")
                .WithObservation("decision=PRESENT proposal=PRESENT riskEvaluation=PRESENT")
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Correlated the Stage 2 decision id, Stage 3 proposal id and Stage 4 risk-evaluation id.")
                .WithLimitation("Correlation is only PASS when the surface was observed and all three ids are present and consistent.")
                .WithNextAction("None.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "decision=PRESENT", now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "proposal=PRESENT", now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "riskEvaluation=PRESENT", now));
        }

        private DiagnosticResult BuildReasonAttributionCheck(StrategyRiskGateObservation observation, string correlation, DateTime now)
        {
            if (observation == null || (observation.CandidateRejectionsObserved() <= 0 && observation.Stage3OutcomesObserved() <= 0 && observation.Stage4EvaluationsObserved() <= 0 && observation.UnattributedCount() <= 0))
            {
                return Inconclusive(
                    "M7.REASON_ATTRIBUTION",
                    correlation,
                    now,
                    "No reason code was observable this pass, so none could be attributed to a pipeline stage.",
                    "Run the pass while reason codes are being reported.",
                    "REASON_ATTRIBUTION_UNOBSERVABLE");
            }

            var evidence = new List<DiagnosticEvidence>();
            foreach (var pair in observation.UnattributedReasonCodes)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "unattributedReason=" + pair.Key + "=" + pair.Value, now));
            }

            var unattributed = observation.UnattributedCount();
            var status = unattributed > 0 ? "REASON_ATTRIBUTION_UNATTRIBUTED" : "REASON_ATTRIBUTION_COMPLETE";

            return Inconclusive(
                "M7.REASON_ATTRIBUTION",
                correlation,
                now,
                unattributed > 0
                    ? "Observed " + unattributed + " reason code(s) that the published catalog could not attribute to a pipeline stage; they are reported unattributed rather than guessed."
                    : "Every observed reason code was attributed to a published pipeline stage.",
                unattributed > 0
                    ? "Review the unattributed reason codes and extend the published catalog only if the code is a documented pipeline stage."
                    : "None.",
                status,
                "UNATTRIBUTED=" + unattributed,
                "DISTINCT_UNATTRIBUTED=" + observation.UnattributedReasonCodes.Count)
                .WithObservation("unattributed=" + unattributed + " distinctUnattributed=" + observation.UnattributedReasonCodes.Count)
                .WithEvidence(evidence.Count > 0
                    ? evidence.ToArray()
                    : new[] { DiagnosticEvidence.Observable(FindingFactory.SourceRuntimeCounters, "unattributed=0", now) });
        }

        private DiagnosticResult Inconclusive(string checkId, string correlation, DateTime now, string observation, string nextAction, params string[] reasonCodes)
        {
            return DiagnosticResult
                .Inconclusive(ModuleId, checkId, correlation, now)
                .WithFindingId(checkId)
                .WithObservation(observation)
                .WithSource(FindingFactory.SourceRuntimeCounters)
                .WithMethod("Need a read-only strategy/risk-gate observation; none is fabricated.")
                .WithLimitation("The result is INCONCLUSIVE because the required observation was not available; it is never treated as PASS.")
                .WithNextAction(nextAction)
                .WithReasonCode(reasonCodes)
                .WithEvidence(DiagnosticEvidence.Unobservable(FindingFactory.SourceRuntimeCounters, observation, now));
        }

        private static string Presence(string value)
        {
            return string.IsNullOrEmpty(value) ? "ABSENT" : "PRESENT";
        }
    }
}
