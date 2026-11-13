using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers
{
    /// <summary>
    /// Thread-safe accumulator for the strategy/risk-gate evidence observed on the drain thread.
    /// The M7 consumer writes to it and <see cref="SnapshotAssembler"/> reads it, so both the
    /// live diagnostic pass and the exported evidence package derive the same M7 observation
    /// from the same real records.
    /// <para>
    /// It preserves per-strategy observations (which a flat counter cannot hold) and the raw
    /// reason-code distributions. It never interprets a code as a cause: attribution to a stage
    /// is delegated to the published <see cref="StrategyReasonCodes"/> catalog, and a code the
    /// catalog cannot place is kept in the unattributed bucket rather than dropped.
    /// </para>
    /// </summary>
    public sealed class StrategyEvidenceAccumulator
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, StrategyEvaluationObservation> _strategies =
            new Dictionary<string, StrategyEvaluationObservation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _candidateRejections = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _stage3 = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _stage4 = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _unattributed = new Dictionary<string, long>(StringComparer.Ordinal);
        private long _observedRecords;
        private StageCorrelationObservation _correlation;

        /// <summary>Number of strategy records observed (positive observation signal).</summary>
        public long ObservedRecordCount { get { lock (_gate) { return _observedRecords; } } }

        /// <summary>Records one read-only strategy observation. Never throws; null-safe.</summary>
        public void Add(StrategyRecord record)
        {
            if (record == null)
            {
                return;
            }

            lock (_gate)
            {
                _observedRecords++;

                var strategy = Merge(record);
                AttributeReasonCodes(strategy, record.ReasonCodes);
                MergeCorrelation(record);
            }
        }

        private void MergeCorrelation(StrategyRecord record)
        {
            if (!record.CorrelationObservable)
            {
                return;
            }

            var decision = record.DecisionId ?? string.Empty;
            var proposal = record.ProposalId ?? string.Empty;
            var risk = record.RiskEvaluationId ?? string.Empty;
            var complete = !string.IsNullOrEmpty(decision) && !string.IsNullOrEmpty(proposal) && !string.IsNullOrEmpty(risk);

            _correlation = new StageCorrelationObservation
            {
                DecisionId = decision,
                ProposalId = proposal,
                RiskEvaluationId = risk,
                CorrelationObservable = true,
                CorrelationComplete = complete,
                CorrelationGapReason = complete ? string.Empty : FirstMissing(decision, proposal, risk)
            };
        }

        private static string FirstMissing(string decision, string proposal, string risk)
        {
            if (string.IsNullOrEmpty(decision))
            {
                return "DECISION_ID_MISSING";
            }

            if (string.IsNullOrEmpty(proposal))
            {
                return "PROPOSAL_ID_MISSING";
            }

            return "RISK_EVALUATION_ID_MISSING";
        }

        private StrategyEvaluationObservation Merge(StrategyRecord record)
        {
            if (string.IsNullOrEmpty(record.StrategyId))
            {
                // No readable id: the surface is still observable (the record count proves it)
                // but the entry cannot be attributed, so it is not added under a guessed id.
                return null;
            }

            if (!_strategies.TryGetValue(record.StrategyId, out var strategy))
            {
                strategy = new StrategyEvaluationObservation { StrategyId = record.StrategyId };
                _strategies[record.StrategyId] = strategy;
            }

            strategy.RecordObserved = true;

            if (record.ActivityObservable)
            {
                strategy.Activity = record.Activity;
                strategy.ActivityObservable = true;
            }

            if (record.CandidateCountsObservable)
            {
                strategy.CandidateCount = record.CandidateCount;
                strategy.ValidCandidateCount = record.ValidCandidateCount;
                strategy.InvalidCandidateCount = record.InvalidCandidateCount;
                strategy.CandidateCountsObservable = true;
            }

            if (record.MissingCapabilityObservable)
            {
                strategy.MissingCapability = record.MissingCapability;
                strategy.MissingCapabilityObservable = true;
            }

            if (record.AvailableObservable)
            {
                strategy.Available = record.Available;
                strategy.AvailableObservable = true;
            }

            if (record.Evaluated)
            {
                strategy.Evaluated = true;
            }

            if (!string.IsNullOrEmpty(record.UnavailableReason))
            {
                strategy.UnavailableReason = record.UnavailableReason;
            }

            MergeReasonCodes(strategy.ReasonCodes, record.ReasonCodes);

            return strategy;
        }

        private static void MergeReasonCodes(IList<string> target, string[] reported)
        {
            if (reported == null)
            {
                return;
            }

            foreach (var code in reported)
            {
                if (!string.IsNullOrEmpty(code) && !target.Contains(code))
                {
                    target.Add(code);
                }
            }
        }

        private void AttributeReasonCodes(StrategyEvaluationObservation strategy, string[] reasonCodes)
        {
            if (reasonCodes == null)
            {
                return;
            }

            foreach (var reason in reasonCodes)
            {
                if (string.IsNullOrEmpty(reason))
                {
                    continue;
                }

                switch (StrategyReasonCodes.StageOf(reason))
                {
                    case StrategyReasonCodes.Stage1:
                        Bump(_candidateRejections, reason);
                        break;
                    case StrategyReasonCodes.Stage2:
                        Bump(_candidateRejections, reason);
                        break;
                    case StrategyReasonCodes.Stage3:
                        Bump(_stage3, reason);
                        break;
                    case StrategyReasonCodes.Stage4:
                        Bump(_stage4, reason);
                        break;
                    default:
                        Bump(_unattributed, reason);
                        break;
                }
            }
        }

        private static void Bump(Dictionary<string, long> distribution, string key)
        {
            distribution.TryGetValue(key, out var current);
            distribution[key] = current + 1;
        }

        /// <summary>Builds a point-in-time M7 observation from the accumulated evidence.</summary>
        public StrategyRiskGateObservation ToObservation()
        {
            lock (_gate)
            {
                var observation = new StrategyRiskGateObservation
                {
                    ObservedRecordCount = _observedRecords,
                    Correlation = _correlation ?? new StageCorrelationObservation()
                };

                foreach (var strategy in _strategies.Values)
                {
                    observation.Strategies.Add(strategy);
                }

                Copy(_candidateRejections, observation.CandidateRejectionReasons);
                Copy(_stage3, observation.Stage3ReasonCodes);
                Copy(_stage4, observation.RiskGateReasonCodes);
                Copy(_unattributed, observation.UnattributedReasonCodes);

                return observation;
            }
        }

        private static void Copy(Dictionary<string, long> source, IDictionary<string, long> target)
        {
            foreach (var pair in source)
            {
                target[pair.Key] = pair.Value;
            }
        }
    }
}
