namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>
    /// Read-only normalized order observation. No submission, change, or cancel path
    /// exists; this record is a passive mirror of what the host already reported.
    /// </summary>
    public sealed class OrderRecord : NormalizedRecord
    {
        /// <summary>Non-reversible reference token for the order id.</summary>
        public string OrderRef { get; set; }

        public string InstrumentId { get; set; }

        public OrderLifecycleState State { get; set; }

        /// <summary>True when the order is a protective stop (classification only).</summary>
        public bool IsProtectiveStop { get; set; }

        public int Quantity { get; set; }

        public int FilledQuantity { get; set; }

        public bool OcoLinked { get; set; }

        public override string StreamKey { get { return "Order|" + InstrumentId + "|" + OrderRef; } }

        protected override string ContentFingerprint()
        {
            return State + ":" + Quantity + ":" + FilledQuantity + (IsProtectiveStop ? ":S" : "") + (OcoLinked ? ":O" : "");
        }

        public OrderRecord()
        {
            OrderRef = string.Empty;
            InstrumentId = string.Empty;
        }
    }
}
