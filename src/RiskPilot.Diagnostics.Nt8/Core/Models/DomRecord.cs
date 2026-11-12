namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>Read-only normalized depth-of-market observation.</summary>
    public sealed class DomRecord : NormalizedRecord
    {
        public string InstrumentId { get; set; }

        public DomOperation Operation { get; set; }

        public int Position { get; set; }

        public double Price { get; set; }

        public long Volume { get; set; }

        /// <summary>True when the host signaled a full book reset.</summary>
        public bool IsReset { get; set; }

        public override string StreamKey { get { return "Dom|" + InstrumentId; } }

        protected override string ContentFingerprint()
        {
            return Operation + ":" + Position + ":" + Invariant(Price) + ":" + Invariant(Volume) + (IsReset ? ":R" : "");
        }

        public DomRecord()
        {
            InstrumentId = string.Empty;
        }
    }
}
