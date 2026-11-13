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
        /// Unknown-direction trades that carried no classification and no reason at all. These are
        /// counted so the unknown total is always fully attributed and the completeness derivation
        /// can see unclassified coverage.
        /// </summary>
        public long UnknownUnclassifiedCount { get; set; }

        /// <summary>
        /// True when at least one trade print was actually observed on the trade stream. Until a
        /// print is seen, a zero unknown/gap count proves nothing, so the derived checks stay
        /// INCONCLUSIVE rather than reporting a clean PASS on absent data.
        /// </summary>
        public bool TradeStreamObservable { get; set; }

        /// <summary>
        /// True when at least one observed trade print carried a source-reported sequence number,
        /// so per-stream continuity was actually measurable. NT8 Level I does not expose a source
        /// sequence, so this is normally false and sequence continuity stays INCONCLUSIVE.
        /// </summary>
        public bool SequenceObservable { get; set; }

        /// <summary>True when every trade direction came from a quote derivation (no provider-supplied
        /// direction). When true, no direction may be reported as provider-confirmed.</summary>
        public bool AllDirectionsQuoteDerived { get; set; }

        /// <summary>True when at least one direction was supplied explicitly by the provider.</summary>
        public bool AnyProviderSuppliedDirection { get; set; }

        /// <summary>
        /// OrderFlow readiness state observed ("AVAILABLE", "UNAVAILABLE", "UNOBSERVABLE").
        /// No public NinjaTrader API exposes the OrderFlow engine readiness (it is the trading
        /// engine's own derived state), so the runtime leaves this UNOBSERVABLE.
        /// </summary>
        public string OrderFlowReadiness { get; set; }

        /// <summary>Reason the OrderFlow stream is not ready, when reported.</summary>
        public string OrderFlowUnavailableReason { get; set; }

        /// <summary>
        /// TradeDelta completeness state observed ("COMPLETE", "INCOMPLETE", "UNOBSERVABLE").
        /// No public NinjaTrader API exposes the TradeDelta completeness; when it is not supplied
        /// explicitly the assembler derives it from the observed trade stream (unclassified prints
        /// or sequence gaps make it INCOMPLETE), and the probe only PASSes a COMPLETE that the
        /// observed stream actually corroborates.
        /// </summary>
        public string TradeDeltaCompleteness { get; set; }

        /// <summary>Reason TradeDelta is incomplete/unavailable, when reported.</summary>
        public string TradeDeltaUnavailableReason { get; set; }

        /// <summary>True when a stream reset (RESET operation) was observed on either feed.</summary>
        public bool ResetObserved { get; set; }

        /// <summary>Number of stream resets observed across both feeds.</summary>
        public long ResetCount { get; set; }

        /// <summary>Stream resets observed on the Level I market-data feed.</summary>
        public long MarketDataResetCount { get; set; }

        /// <summary>
        /// Stream resets observed on the Level II depth feed. A depth reset is what invalidates the
        /// derived order-flow/DOM mirror, so it is reported separately from a Level I reset.
        /// </summary>
        public long DepthResetCount { get; set; }

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
