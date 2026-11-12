using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>Per-strategy evaluation observation (M7).</summary>
    public sealed class StrategyEvaluationObservation
    {
        public string StrategyId { get; set; }

        /// <summary>Activity state observed for the strategy, when reported.</summary>
        public StrategyActivity Activity { get; set; }

        /// <summary>Candidate signals produced, when observable.</summary>
        public long CandidateCount { get; set; }

        /// <summary>Valid candidates, when observable.</summary>
        public long ValidCandidateCount { get; set; }

        /// <summary>Invalid candidates, when observable.</summary>
        public long InvalidCandidateCount { get; set; }

        /// <summary>True when the strategy was skipped because a required capability was missing.</summary>
        public bool MissingCapability { get; set; }

        /// <summary>True when the strategy was evaluated and available.</summary>
        public bool Available { get; set; }

        /// <summary>Reason the strategy was unavailable, when reported.</summary>
        public string UnavailableReason { get; set; }

        /// <summary>Raw reason codes reported for this strategy.</summary>
        public IList<string> ReasonCodes { get; set; }

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
    /// M7 strategy / risk-gate observation. Raw counts and reason codes only; the module
    /// never declares an observed baseline figure "resolved" or "contradictory".
    /// </summary>
    public sealed class StrategyRiskGateObservation
    {
        /// <summary>Per-strategy observations for the registered strategies.</summary>
        public IList<StrategyEvaluationObservation> Strategies { get; set; }

        /// <summary>Raw distribution of Stage 1 candidate rejection reason codes.</summary>
        public IDictionary<string, long> CandidateRejectionReasons { get; set; }

        /// <summary>Number of evaluations that produced ALL_CANDIDATES_INVALID.</summary>
        public long AllCandidatesInvalidCount { get; set; }

        /// <summary>Number of Stage 3 proposals that produced zero quantity.</summary>
        public long Stage3ZeroQuantityCount { get; set; }

        /// <summary>Raw distribution of Stage 3 sizing reason codes.</summary>
        public IDictionary<string, long> Stage3ReasonCodes { get; set; }

        /// <summary>Number of Stage 4 risk-gate vetoes.</summary>
        public long RiskGateVetoCount { get; set; }

        /// <summary>Raw distribution of Stage 4 veto reason codes.</summary>
        public IDictionary<string, long> RiskGateVetoReasons { get; set; }

        /// <summary>Stage 2 / 3 / 4 correlation evidence.</summary>
        public StageCorrelationObservation Correlation { get; set; }

        /// <summary>True when the strategy/risk surface was observable at all.</summary>
        public bool StrategySurfaceObservable { get; set; }

        public StrategyRiskGateObservation()
        {
            Strategies = new List<StrategyEvaluationObservation>();
            CandidateRejectionReasons = new Dictionary<string, long>();
            Stage3ReasonCodes = new Dictionary<string, long>();
            RiskGateVetoReasons = new Dictionary<string, long>();
            Correlation = new StageCorrelationObservation();
        }
    }
}
