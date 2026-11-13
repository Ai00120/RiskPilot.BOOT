using System;
using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>Per-strategy evaluation observation (M7).</summary>
    /// <remarks>
    /// Every figure carries an explicit observation flag. A field the source did not report is
    /// left at its default and its flag stays false, so the module reports it as UNOBSERVABLE
    /// instead of presenting the default (zero/false) as an observed value. This is how M7
    /// avoids claiming a strategy was inactive merely because no activity was reported.
    /// </remarks>
    public sealed class StrategyEvaluationObservation
    {
        public string StrategyId { get; set; }

        /// <summary>Activity state observed for the strategy, when reported.</summary>
        public StrategyActivity Activity { get; set; }

        /// <summary>True when the source reported the activity state.</summary>
        public bool ActivityObservable { get; set; }

        /// <summary>Candidate signals produced, when observable.</summary>
        public long CandidateCount { get; set; }

        /// <summary>Valid candidates, when observable.</summary>
        public long ValidCandidateCount { get; set; }

        /// <summary>Invalid candidates, when observable.</summary>
        public long InvalidCandidateCount { get; set; }

        /// <summary>True when the source reported the candidate counts.</summary>
        public bool CandidateCountsObservable { get; set; }

        /// <summary>True when the strategy was skipped because a required capability was missing.</summary>
        public bool MissingCapability { get; set; }

        /// <summary>True when the source reported the missing-capability state.</summary>
        public bool MissingCapabilityObservable { get; set; }

        /// <summary>True when the strategy was evaluated and available.</summary>
        public bool Available { get; set; }

        /// <summary>True when the source reported the availability state.</summary>
        public bool AvailableObservable { get; set; }

        /// <summary>True when the strategy produced an evaluation result this pass.</summary>
        public bool Evaluated { get; set; }

        /// <summary>Reason the strategy was unavailable, when reported.</summary>
        public string UnavailableReason { get; set; }

        /// <summary>Raw reason codes reported for this strategy.</summary>
        public IList<string> ReasonCodes { get; set; }

        /// <summary>True when a read-only source actually reported this strategy.</summary>
        public bool RecordObserved { get; set; }

        /// <summary>
        /// True when at least one field of this strategy was actually observed. A placeholder
        /// entry (added only so the id is listed) carries no observation and is not counted.
        /// </summary>
        public bool Observed
        {
            get
            {
                return RecordObserved
                    || ActivityObservable
                    || CandidateCountsObservable
                    || MissingCapabilityObservable
                    || AvailableObservable
                    || Evaluated
                    || (ReasonCodes != null && ReasonCodes.Count > 0);
            }
        }

        public StrategyEvaluationObservation()
        {
            StrategyId = string.Empty;
            UnavailableReason = string.Empty;
            ReasonCodes = new List<string>();
        }
    }

    /// <summary>Correlation ids observed across Stage 2 decision, Stage 3 proposal and Stage 4 evaluation.</summary>
    public sealed class StageCorrelationObservation
    {
        public string DecisionId { get; set; }

        public string ProposalId { get; set; }

        public string RiskEvaluationId { get; set; }

        /// <summary>
        /// True when the correlation surface was actually observed. Without it the correlation
        /// check is INCONCLUSIVE: a boolean alone is not evidence that the linkage was checked.
        /// </summary>
        public bool CorrelationObservable { get; set; }

        /// <summary>True when all three ids are present and consistent.</summary>
        public bool CorrelationComplete { get; set; }

        /// <summary>Why correlation is incomplete, when it is.</summary>
        public string CorrelationGapReason { get; set; }

        public StageCorrelationObservation()
        {
            DecisionId = string.Empty;
            ProposalId = string.Empty;
            RiskEvaluationId = string.Empty;
            CorrelationGapReason = string.Empty;
        }
    }

    /// <summary>
    /// M7 strategy / risk-gate observation. It carries the raw, source-reported reason-code
    /// distributions and per-strategy observations; every aggregate figure is <b>derived</b>
    /// from those distributions so an aggregate can never disagree with the evidence it
    /// summarises.
    /// <para>
    /// <see cref="StrategySurfaceObservable"/> is derived too: it is true only when at least one
    /// piece of evidence was actually observed. There is deliberately no setter, so a host
    /// cannot assert the surface was reached without an observation.
    /// </para>
    /// </summary>
    public sealed class StrategyRiskGateObservation
    {
        /// <summary>Per-strategy observations for the registered strategies.</summary>
        public IList<StrategyEvaluationObservation> Strategies { get; set; }

        /// <summary>Raw distribution of Stage 1/2 candidate rejection reason codes.</summary>
        public IDictionary<string, long> CandidateRejectionReasons { get; set; }

        /// <summary>Raw distribution of Stage 3 sizing reason codes.</summary>
        public IDictionary<string, long> Stage3ReasonCodes { get; set; }

        /// <summary>Raw distribution of Stage 4 risk-gate evaluation reason codes (including approvals).</summary>
        public IDictionary<string, long> RiskGateReasonCodes { get; set; }

        /// <summary>
        /// Reason codes observed that the published catalog could not attribute to a stage.
        /// They are preserved here (never dropped) so the module can report that it could not
        /// place them rather than fabricating a stage.
        /// </summary>
        public IDictionary<string, long> UnattributedReasonCodes { get; set; }

        /// <summary>Stage 2 / 3 / 4 correlation evidence.</summary>
        public StageCorrelationObservation Correlation { get; set; }

        /// <summary>
        /// Number of strategy records the read-only source actually reported. Set by the host
        /// adapter (or the counter merge when only the counter stream is present). It is a
        /// positive observation signal even when the source did not supply per-strategy detail.
        /// </summary>
        public long ObservedRecordCount { get; set; }

        /// <summary>
        /// Number of per-strategy observations actually observed. It is the larger of the
        /// detailed entries and the reported record count, so a source that only reports a
        /// record count still makes the surface observable.
        /// </summary>
        public long StrategyObservationCount()
        {
            long detailed = 0;
            if (Strategies != null)
            {
                foreach (var strategy in Strategies)
                {
                    if (strategy != null && strategy.Observed)
                    {
                        detailed++;
                    }
                }
            }

            return detailed > ObservedRecordCount ? detailed : ObservedRecordCount;
        }

        /// <summary>Number of Stage 1/2 candidate rejection reason codes observed.</summary>
        public long CandidateRejectionsObserved()
        {
            return Sum(CandidateRejectionReasons);
        }

        /// <summary>Number of Stage 2 evaluations that produced ALL_CANDIDATES_INVALID.</summary>
        public long AllCandidatesInvalidCount()
        {
            return ValueOf(CandidateRejectionReasons, "ALL_CANDIDATES_INVALID");
        }

        /// <summary>Number of Stage 3 sizing outcomes observed (sized and not sized).</summary>
        public long Stage3OutcomesObserved()
        {
            return Sum(Stage3ReasonCodes);
        }

        /// <summary>Number of Stage 3 proposals that produced zero quantity.</summary>
        public long Stage3ZeroQuantityCount()
        {
            var sized = ValueOf(Stage3ReasonCodes, "SIZED_SUCCESSFULLY");
            var observed = Stage3OutcomesObserved();
            return observed > sized ? observed - sized : 0;
        }

        /// <summary>Number of Stage 4 risk-gate evaluations observed (approved and vetoed).</summary>
        public long Stage4EvaluationsObserved()
        {
            return Sum(RiskGateReasonCodes);
        }

        /// <summary>Number of Stage 4 risk-gate vetoes (everything except RISK_APPROVED).</summary>
        public long RiskGateVetoCount()
        {
            var approved = ValueOf(RiskGateReasonCodes, "RISK_APPROVED");
            var observed = Stage4EvaluationsObserved();
            return observed > approved ? observed - approved : 0;
        }

        /// <summary>The Stage 4 veto reason distribution, with RISK_APPROVED removed.</summary>
        public IDictionary<string, long> RiskGateVetoReasons()
        {
            var vetoes = new Dictionary<string, long>();
            if (RiskGateReasonCodes == null)
            {
                return vetoes;
            }

            foreach (var pair in RiskGateReasonCodes)
            {
                if (!string.Equals(pair.Key, "RISK_APPROVED", StringComparison.Ordinal))
                {
                    vetoes[pair.Key] = pair.Value;
                }
            }

            return vetoes;
        }

        /// <summary>Number of observed reason codes the catalog could not attribute to a stage.</summary>
        public long UnattributedCount()
        {
            return Sum(UnattributedReasonCodes);
        }

        /// <summary>
        /// True when the strategy/risk surface was reached at all. Derived from the observed
        /// evidence so it can never be asserted without an observation.
        /// </summary>
        public bool StrategySurfaceObservable
        {
            get
            {
                return StrategyObservationCount() > 0
                    || CandidateRejectionsObserved() > 0
                    || Stage3OutcomesObserved() > 0
                    || Stage4EvaluationsObserved() > 0
                    || UnattributedCount() > 0
                    || (Correlation != null && Correlation.CorrelationObservable);
            }
        }

        private static long Sum(IDictionary<string, long> distribution)
        {
            if (distribution == null)
            {
                return 0;
            }

            long total = 0;
            foreach (var pair in distribution)
            {
                if (pair.Value > 0)
                {
                    total += pair.Value;
                }
            }

            return total;
        }

        private static long ValueOf(IDictionary<string, long> distribution, string key)
        {
            if (distribution == null || string.IsNullOrEmpty(key))
            {
                return 0;
            }

            return distribution.TryGetValue(key, out var value) && value > 0 ? value : 0;
        }

        public StrategyRiskGateObservation()
        {
            Strategies = new List<StrategyEvaluationObservation>();
            CandidateRejectionReasons = new Dictionary<string, long>();
            Stage3ReasonCodes = new Dictionary<string, long>();
            RiskGateReasonCodes = new Dictionary<string, long>();
            UnattributedReasonCodes = new Dictionary<string, long>();
            Correlation = new StageCorrelationObservation();
        }
    }
}
