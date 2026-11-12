using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// M6 TradeDelta / OrderFlow observation. Direction provenance is recorded exactly as
    /// the source reported it: a direction derived from a quote comparison is never
    /// presented as provider-confirmed, and an unknown direction is counted, never filled in.
    /// </summary>
    public sealed class TradeDeltaObservation
    {
        /// <summary>Trades classified as BUY by the source or by quote derivation.</summary>
        public long BuyDerivedCount { get; set; }

        /// <summary>Trades classified as SELL by the source or by quote derivation.</summary>
        public long SellDerivedCount { get; set; }

        /// <summary>Trades whose direction could not be determined.</summary>
        public long UnknownCount { get; set; }

        /// <summary>Unknown-direction trades that fell strictly inside the spread.</summary>
        public long UnknownInsideSpreadCount { get; set; }

        /// <summary>Unknown-direction trades with no usable BBO.</summary>
        public long UnknownNoBboCount { get; set; }

        /// <summary>Unknown-direction trades with an incomplete BBO.</summary>
        public long UnknownBboIncompleteCount { get; set; }

        /// <summary>Unknown-direction trades with a crossed/invalid BBO.</summary>
        public long UnknownBboInvalidCount { get; set; }

        /// <summary>Unknown-direction trades the provider itself reported as unknown.</summary>
        public long UnknownProviderReportedCount { get; set; }

        /// <summary>
        /// True when every trade direction came from a quote derivation (no provider-supplied
        /// direction). When true, no direction may be reported as provider-confirmed.
        /// </summary>
        public bool AllDirectionsQuoteDerived { get; set; }

        /// <summary>True when at least one direction was supplied explicitly by the provider.</summary>
        public bool AnyProviderSuppliedDirection { get; set; }

        /// <summary>OrderFlow readiness state observed ("AVAILABLE", "UNAVAILABLE", "UNOBSERVABLE").</summary>
        public string OrderFlowReadiness { get; set; }

        /// <summary>Reason the OrderFlow stream is not ready, when reported.</summary>
        public string OrderFlowUnavailableReason { get; set; }

        /// <summary>TradeDelta completeness state observed ("COMPLETE", "INCOMPLETE", "UNOBSERVABLE").</summary>
        public string TradeDeltaCompleteness { get; set; }

        /// <summary>Reason TradeDelta is incomplete/unavailable, when reported.</summary>
        public string TradeDeltaUnavailableReason { get; set; }

        /// <summary>True when a stream reset (RESET operation) was observed.</summary>
        public bool ResetObserved { get; set; }

        /// <summary>Number of stream resets observed.</summary>
        public long ResetCount { get; set; }

        /// <summary>Number of per-stream sequence gaps observed for the order-flow stream.</summary>
        public long SequenceGapCount { get; set; }

        /// <summary>Source-time basis observed for the trade stream.</summary>
        public SourceTimeBasis SourceTimeBasis { get; set; }

        public TradeDeltaObservation()
        {
            OrderFlowReadiness = "UNOBSERVABLE";
            OrderFlowUnavailableReason = string.Empty;
            TradeDeltaCompleteness = "UNOBSERVABLE";
            TradeDeltaUnavailableReason = string.Empty;
        }
    }
}
