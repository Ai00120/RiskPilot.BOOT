using System;
using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Canonical reason-code catalog for the RiskPilot.BOT strategy pipeline. It mirrors the
    /// public reason-code constants of the reference domain (CandidateValidator,
    /// CandidateSelectionEngine, SizingReasonCode, RiskReasonCode) and maps each code to the
    /// pipeline stage that emits it.
    /// <para>
    /// It exists so M7 can attribute a reported reason code to a stage using published,
    /// documented values instead of guessing. A code that is not in the catalog is never
    /// silently dropped or assumed: it is counted as unattributed, so the module can report
    /// that it could not place it rather than fabricating a stage.
    /// </para>
    /// <para>
    /// This is a read-only, host-free constant catalog. It does not run any strategy logic and
    /// does not import any RiskPilot.BOT type.
    /// </para>
    /// </summary>
    public static class StrategyReasonCodes
    {
        /// <summary>Stage 1 candidate validity checks (VAL_01..VAL_09).</summary>
        public const string Stage1 = "STAGE1_CANDIDATE_VALIDITY";

        /// <summary>Stage 2 candidate selection / conflict resolution.</summary>
        public const string Stage2 = "STAGE2_SELECTION";

        /// <summary>Stage 3 position sizing.</summary>
        public const string Stage3 = "STAGE3_SIZING";

        /// <summary>Stage 4 risk gate.</summary>
        public const string Stage4 = "STAGE4_RISK_GATE";

        /// <summary>A code that does not appear in the catalog, so it cannot be attributed.</summary>
        public const string Unattributed = "UNATTRIBUTED";

        // Stage 1 — CandidateValidator (VAL_01..VAL_09).
        private static readonly string[] Stage1Codes =
        {
            "INVALID_RESULT_STATUS",
            "NULL_CANDIDATE_SIGNAL",
            "FLAT_DIRECTION",
            "NON_ACTIONABLE_INTENTION",
            "UNSATISFIED_REQUIRED_CAPABILITIES",
            "INSTRUMENT_IDENTITY_MISMATCH",
            "CAUSAL_ALIGNMENT_FAILURE",
            "MISSING_REFERENCE_PRICE",
            "INVALID_REFERENCE_PRICE",
            "MISSING_INVALIDATION_CRITERIA",
            "INVALIDATION_DIRECTIONAL_MISALIGNMENT"
        };

        // Stage 2 — CandidateSelectionEngine (selection outcome codes).
        private static readonly string[] Stage2Codes =
        {
            "SINGLE_VALID_CANDIDATE_SELECTED",
            "ALL_CANDIDATES_INVALID",
            "NO_STRATEGY_EMITTED_SIGNAL",
            "ALL_CANDIDATES_UNSATISFIED_CAPABILITIES",
            "OPPOSING_LONG_SHORT_CONFLICT",
            "UNAPPROVED_SAME_DIRECTION_PRIORITY"
        };

        // Stage 3 — SizingReasonCode.
        private static readonly string[] Stage3Codes =
        {
            "SIZED_SUCCESSFULLY",
            "ANALYTICAL_NO_TRADE",
            "FLAT_DIRECTION",
            "MISSING_DECISION",
            "MISSING_INSTRUMENT_SPECIFICATION",
            "INSTRUMENT_MISMATCH",
            "UNSUPPORTED_POLICY",
            "FIXED_QUANTITY_INVALID_CONFIG",
            "PERCENTAGE_OF_RISK_MISSING_RISK_PERCENTAGE",
            "PERCENTAGE_OF_RISK_MISSING_MAX_STALENESS",
            "PERCENTAGE_OF_RISK_MISSING_ACCOUNT_STATE",
            "PERCENTAGE_OF_RISK_FUTURE_ACCOUNT_STATE",
            "PERCENTAGE_OF_RISK_STALE_ACCOUNT_STATE",
            "PERCENTAGE_OF_RISK_MISSING_ENTRY_PRICE",
            "PERCENTAGE_OF_RISK_MISSING_INVALIDATION_LEVEL",
            "PERCENTAGE_OF_RISK_INVALID_RISK_DISTANCE",
            "MINIMUM_STOP_DISTANCE_VIOLATION",
            "CURRENCY_MISMATCH",
            "INCONSISTENT_INSTRUMENT_SPECIFICATION",
            "CALCULATED_QUANTITY_BELOW_MINIMUM",
            "CAPPED_AT_QUANTITY_MAX",
            "CAUSAL_SEQUENCE_MISMATCH",
            "TRADING_DISABLED",
            "MISSING_RISK_POLICY",
            "CAPITAL_BASIS_MISMATCH",
            "STOP_DISTANCE_NOT_TICK_ALIGNED",
            "MAX_STOP_RISK_PER_TRADE_EXCEEDED",
            "COST_BUFFER_NOT_RESERVED",
            "ONE_CONTRACT_EXCEEDS_RISK_BUDGET",
            "RISK_ARITHMETIC_OVERFLOW",
            "REWARD_TARGET_MAPPING_MISSING"
        };

        // Stage 4 — RiskReasonCode.
        private static readonly string[] Stage4Codes =
        {
            "RISK_APPROVED",
            "ANALYTICAL_NO_TRADE",
            "MISSING_PROPOSAL",
            "MISSING_RISK_POLICY",
            "INVALID_RISK_POLICY",
            "MISSING_ACCOUNT_STATE",
            "UNVERIFIED_ACCOUNT_STATE",
            "STALE_ACCOUNT_STATE",
            "FUTURE_ACCOUNT_STATE",
            "CAUSAL_SEQUENCE_MISMATCH",
            "MISSING_INSTRUMENT_SPECIFICATION",
            "UNVERIFIED_INSTRUMENT_SPECIFICATION",
            "INSTRUMENT_MISMATCH",
            "CURRENCY_MISMATCH",
            "MAX_DAILY_LOSS_EXCEEDED",
            "ACCOUNT_DRAWDOWN_REACHED",
            "MAX_POSITION_COUNT_EXCEEDED",
            "MAX_QUANTITY_EXCEEDED",
            "QUANTITY_REDUCED_BY_RISK_LIMIT",
            "BELOW_MINIMUM_QUANTITY_VETO",
            "MISSING_PORTFOLIO_EVIDENCE",
            "INVALID_PORTFOLIO_EVIDENCE",
            "STALE_PORTFOLIO_EVIDENCE",
            "FUTURE_PORTFOLIO_EVIDENCE",
            "INVALID_ACCOUNT_STATE",
            "ATTESTATION_STALE_EPOCH",
            "CAPITAL_BASIS_MISMATCH",
            "STOP_DISTANCE_NOT_TICK_ALIGNED",
            "MAX_STOP_RISK_PER_TRADE_EXCEEDED",
            "COST_BUFFER_NOT_RESERVED",
            "ONE_CONTRACT_EXCEEDS_RISK_BUDGET",
            "RISK_ARITHMETIC_OVERFLOW",
            "AGGREGATE_OPEN_STOP_RISK_EXCEEDED",
            "RISK_AT_STOP_ASSESSMENT_MISMATCH",
            "PORTFOLIO_STATE_MISSING",
            "UNVERIFIED_PORTFOLIO_STATE",
            "INVALID_PORTFOLIO_STATE",
            "STALE_PORTFOLIO_STATE",
            "FUTURE_PORTFOLIO_STATE",
            "OPEN_POSITION_STOP_RISK_MISSING",
            "INVALID_OPEN_POSITION_STOP_RISK",
            "PORTFOLIO_CAPTURE_UNSTABLE",
            "PORTFOLIO_LIFECYCLE_EPOCH_MISMATCH",
            "PORTFOLIO_ACCOUNT_SEQUENCE_INVALID",
            "PORTFOLIO_CAPTURE_GENERATION_INVALID",
            "PORTFOLIO_ACCOUNT_IDENTITY_MISMATCH",
            "PORTFOLIO_CURRENCY_MISMATCH",
            "ACCOUNT_ENVIRONMENT_UNSUPPORTED",
            "PORTFOLIO_SOURCE_UNSUPPORTED",
            "PORTFOLIO_POSITION_COUNT_MISMATCH",
            "PORTFOLIO_AGGREGATE_RISK_MISMATCH",
            "PORTFOLIO_DRAWDOWN_BASELINE_UNKNOWN",
            "PORTFOLIO_HIGH_WATER_MARK_INVALID",
            "PORTFOLIO_RISK_SESSION_UNKNOWN",
            "PORTFOLIO_INSTRUMENT_IDENTITY_MISMATCH",
            "PORTFOLIO_PNL_OBSERVATION_INCONSISTENT",
            "PROPOSAL_EXPIRED",
            "OPERATOR_MODIFICATION_OUTSIDE_LIMITS",
            "REWARD_TARGET_MAPPING_MISSING"
        };

        private static readonly Dictionary<string, string> StageByCode = BuildStageMap();

        private static Dictionary<string, string> BuildStageMap()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            AddStage(map, Stage1, Stage1Codes);
            AddStage(map, Stage2, Stage2Codes);
            AddStage(map, Stage3, Stage3Codes);
            AddStage(map, Stage4, Stage4Codes);
            return map;
        }

        private static void AddStage(Dictionary<string, string> map, string stage, string[] codes)
        {
            foreach (var code in codes)
            {
                // A code that appears in more than one stage is ambiguous by design (for
                // example FLAT_DIRECTION and ANALYTICAL_NO_TRADE). It is recorded as
                // unattributed so the tool never claims a stage it cannot prove.
                if (map.ContainsKey(code))
                {
                    map[code] = Unattributed;
                }
                else
                {
                    map[code] = stage;
                }
            }
        }

        /// <summary>True when the code appears in the published catalog.</summary>
        public static bool IsKnown(string reasonCode)
        {
            return !string.IsNullOrEmpty(reasonCode) && StageByCode.ContainsKey(reasonCode);
        }

        /// <summary>
        /// The stage the code belongs to, or <see cref="Unattributed"/> when it is not in the
        /// catalog or is ambiguous across stages. Never a guessed value.
        /// </summary>
        public static string StageOf(string reasonCode)
        {
            if (string.IsNullOrEmpty(reasonCode))
            {
                return Unattributed;
            }

            return StageByCode.TryGetValue(reasonCode, out var stage) ? stage : Unattributed;
        }

        /// <summary>The codes published for one stage, or an empty list for an unknown stage.</summary>
        public static IReadOnlyList<string> CodesFor(string stage)
        {
            if (string.Equals(stage, Stage1, StringComparison.Ordinal))
            {
                return Stage1Codes;
            }

            if (string.Equals(stage, Stage2, StringComparison.Ordinal))
            {
                return Stage2Codes;
            }

            if (string.Equals(stage, Stage3, StringComparison.Ordinal))
            {
                return Stage3Codes;
            }

            if (string.Equals(stage, Stage4, StringComparison.Ordinal))
            {
                return Stage4Codes;
            }

            return new string[0];
        }
    }
}
