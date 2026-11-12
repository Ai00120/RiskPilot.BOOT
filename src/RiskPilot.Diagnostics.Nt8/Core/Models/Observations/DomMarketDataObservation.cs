using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// A raw depth-of-market counter block. Two such blocks are reported separately because
    /// they belong to two different layers: the NT8 host adapter and the recorder. They must
    /// never be added together or presented as one number.
    /// </summary>
    public sealed class DomCounterBlock
    {
        /// <summary>Layer this block describes ("HOST_ADAPTER", "RECORDER", ...).</summary>
        public string Layer { get; set; }

        /// <summary>True when the counters were actually observed for this layer.</summary>
        public bool Observable { get; set; }

        public long SnapshotSyncCount { get; set; }

        public long SnapshotSyncFailureCount { get; set; }

        public long SnapshotSyncResetCount { get; set; }

        public long InsertOutOfBoundsCount { get; set; }

        public long UpdateOutOfBoundsCount { get; set; }

        public long RemoveOutOfBoundsCount { get; set; }

        public long InsertCount { get; set; }

        public long UpdateCount { get; set; }

        public long RemoveCount { get; set; }

        public long ResetCount { get; set; }

        public long InvalidPriceCount { get; set; }

        public long InvalidSizeCount { get; set; }

        public long InvalidPositionCount { get; set; }

        /// <summary>Native snapshot depth last reported (bids / asks), when observable.</summary>
        public int? NativeSnapshotBids { get; set; }

        public int? NativeSnapshotAsks { get; set; }

        /// <summary>Internal mirror depth last reported (bids / asks), when observable.</summary>
        public int? InternalBidLevels { get; set; }

        public int? InternalAskLevels { get; set; }

        public DomCounterBlock()
        {
            Layer = string.Empty;
        }
    }

    /// <summary>
    /// M5 DOM / market-data observation. Holds the two independent counter layers plus
    /// Level I quote/trade presence and freshness/gap signals. Every figure is raw evidence;
    /// the module does not interpret a counter as "healthy" or "resolved".
    /// </summary>
    public sealed class DomMarketDataObservation
    {
        public DomCounterBlock HostAdapter { get; set; }

        public DomCounterBlock Recorder { get; set; }

        /// <summary>True when a Level I quote stream was observed.</summary>
        public bool Level1QuoteObserved { get; set; }

        /// <summary>True when a Level I last-trade stream was observed.</summary>
        public bool LastTradeObserved { get; set; }

        /// <summary>Seconds since the last DOM event, when observable.</summary>
        public double? LastDomEventAgeSeconds { get; set; }

        /// <summary>Last observed snapshot-sync trigger reason, when observable.</summary>
        public string LastSnapshotSyncTrigger { get; set; }

        /// <summary>True when the native snapshot exceeded the retained mirror window.</summary>
        public bool? NativeSnapshotExceedsWindow { get; set; }

        /// <summary>Number of market-data sequence gaps observed by the ingest pipeline.</summary>
        public long SequenceGapCount { get; set; }

        /// <summary>Number of dropped market-data events observed by the ingest pipeline.</summary>
        public long DroppedEventCount { get; set; }

        /// <summary>Number of malformed DOM events observed.</summary>
        public long MalformedCount { get; set; }

        public DomMarketDataObservation()
        {
            HostAdapter = new DomCounterBlock { Layer = "HOST_ADAPTER" };
            Recorder = new DomCounterBlock { Layer = "RECORDER" };
            LastSnapshotSyncTrigger = string.Empty;
        }
    }
}
