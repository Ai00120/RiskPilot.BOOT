namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>
    /// Normalized instrument identity and contract metadata. The evidence instrument is
    /// ES 12-26 (ES_FUTURES_CME_DEC26) but the tool never assumes that at runtime.
    /// </summary>
    public sealed class InstrumentRecord : NormalizedRecord
    {
        public string FullName { get; set; }

        public string InstrumentId { get; set; }

        public string RootSymbol { get; set; }

        public string Expiry { get; set; }

        public string Exchange { get; set; }

        public string InstrumentType { get; set; }

        public string Currency { get; set; }

        public double TickSize { get; set; }

        public double PointValue { get; set; }

        /// <summary>True when master-instrument metadata was safe to read (post DataLoaded).</summary>
        public bool MasterMetadataAvailable { get; set; }

        /// <summary>True when the tool matched this instrument to the recorded evidence identity.</summary>
        public bool MatchesEvidenceIdentity { get; set; }

        public InstrumentRecord()
        {
            FullName = string.Empty;
            InstrumentId = string.Empty;
            RootSymbol = string.Empty;
            Expiry = string.Empty;
            Exchange = string.Empty;
            InstrumentType = string.Empty;
            Currency = string.Empty;
        }
    }
}
