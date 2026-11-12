namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>Read-only normalized market data observation (quote, trade, or bar).</summary>
    public sealed class MarketDataRecord : NormalizedRecord
    {
        public string InstrumentId { get; set; }

        public MarketDataKind Kind { get; set; }

        public double Bid { get; set; }

        public double Ask { get; set; }

        public double Last { get; set; }

        public long Volume { get; set; }

        /// <summary>True when this event signals a stream reset (e.g. session roll).</summary>
        public bool IsReset { get; set; }

        /// <summary>Direction of a trade print, when one could be determined (trade kind only).</summary>
        public TradeDirection TradeDirection { get; set; }

        /// <summary>How the trade direction was determined, or Unspecified when unknown.</summary>
        public TradeDirectionSource TradeDirectionSource { get; set; }

        /// <summary>Reason the direction is unknown, or NotApplicable when it is known.</summary>
        public TradeDirectionUnknownReason TradeDirectionUnknownReason { get; set; }

        public override string StreamKey { get { return "MarketData|" + Kind + "|" + InstrumentId; } }

        protected override string ContentFingerprint()
        {
            return Invariant(Bid) + "," + Invariant(Ask) + "," + Invariant(Last) + "," + Invariant(Volume) + (IsReset ? ",R" : "");
        }

        public MarketDataRecord()
        {
            InstrumentId = string.Empty;
        }
    }
}
