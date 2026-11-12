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

        public override string StreamKey { get { return "MarketData|" + Kind + "|" + InstrumentId; } }

        public MarketDataRecord()
        {
            InstrumentId = string.Empty;
        }
    }
}
