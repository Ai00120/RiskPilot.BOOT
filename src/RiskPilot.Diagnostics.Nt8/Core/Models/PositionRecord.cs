namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>Read-only normalized position snapshot.</summary>
    public sealed class PositionRecord : NormalizedRecord
    {
        public string InstrumentId { get; set; }

        public PositionSide Side { get; set; }

        public int Quantity { get; set; }

        public double AveragePrice { get; set; }

        /// <summary>Non-reversible reference token for the owning account.</summary>
        public string AccountRef { get; set; }

        public PositionRecord()
        {
            InstrumentId = string.Empty;
            AccountRef = string.Empty;
        }
    }
}
