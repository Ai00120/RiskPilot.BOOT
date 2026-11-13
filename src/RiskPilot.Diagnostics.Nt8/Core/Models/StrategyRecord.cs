namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>
    /// Read-only normalized strategy observation. Strategy identity comes from the
    /// RiskPilot read-only diagnostic contract or telemetry, never from reflection.
    /// <para>
    /// Only <see cref="StrategyId"/> and <see cref="ReasonCodes"/> are guaranteed. The optional
    /// fields each carry their own observation flag: a source that does not report a value
    /// leaves the flag false and the value at its default, so the module reports it as
    /// UNOBSERVABLE rather than presenting the default (false/zero) as an observed value.
    /// </para>
    /// </summary>
    public sealed class StrategyRecord : NormalizedRecord
    {
        public string StrategyId { get; set; }

        public StrategyActivity Activity { get; set; }

        /// <summary>Free-form reason codes reported alongside the strategy state.</summary>
        public string[] ReasonCodes { get; set; }

        /// <summary>True when the source reported the <see cref="Activity"/> state.</summary>
        public bool ActivityObservable { get; set; }

        /// <summary>Candidate signals produced, when the source reported them.</summary>
        public long CandidateCount { get; set; }

        /// <summary>Valid candidates, when the source reported them.</summary>
        public long ValidCandidateCount { get; set; }

        /// <summary>Invalid candidates, when the source reported them.</summary>
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

        /// <summary>Reason the strategy was unavailable, when the source reported one.</summary>
        public string UnavailableReason { get; set; }

        /// <summary>Stage 2 decision id, when the source reported the correlation surface.</summary>
        public string DecisionId { get; set; }

        /// <summary>Stage 3 proposal id, when the source reported the correlation surface.</summary>
        public string ProposalId { get; set; }

        /// <summary>Stage 4 risk-evaluation id, when the source reported the correlation surface.</summary>
        public string RiskEvaluationId { get; set; }

        /// <summary>
        /// True when the source reported the Stage 2→3→4 correlation ids. Without it the
        /// correlation surface is UNOBSERVABLE, never assumed complete.
        /// </summary>
        public bool CorrelationObservable { get; set; }

        public StrategyRecord()
        {
            StrategyId = string.Empty;
            ReasonCodes = new string[0];
            UnavailableReason = string.Empty;
            DecisionId = string.Empty;
            ProposalId = string.Empty;
            RiskEvaluationId = string.Empty;
        }
    }
}
