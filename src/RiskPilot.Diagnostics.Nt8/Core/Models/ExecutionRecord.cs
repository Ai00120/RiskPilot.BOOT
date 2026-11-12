namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>
    /// Read-only normalized execution (fill) observation, used only to correlate
    /// order-to-fill continuity. Execution reads stay out of scope until explicitly
    /// authorized; when out of scope the pipeline simply produces no records.
    /// </summary>
    public sealed class ExecutionRecord : NormalizedRecord
    {
        /// <summary>Non-reversible reference token for the execution id.</summary>
        public string ExecutionRef { get; set; }

        /// <summary>Non-reversible reference token for the parent order id.</summary>
        public string OrderRef { get; set; }

        public string InstrumentId { get; set; }

        public double Price { get; set; }

        public int Quantity { get; set; }

        public PositionSide PositionSide { get; set; }

        public override string StreamKey { get { return "Execution|" + InstrumentId + "|" + ExecutionRef; } }

        protected override string ContentFingerprint()
        {
            return Invariant(Price) + ":" + Quantity + ":" + PositionSide + ":" + (OrderRef ?? string.Empty);
        }

        public ExecutionRecord()
        {
            ExecutionRef = string.Empty;
            OrderRef = string.Empty;
            InstrumentId = string.Empty;
        }
    }
}
