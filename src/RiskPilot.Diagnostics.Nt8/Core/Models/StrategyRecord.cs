namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>
    /// Read-only normalized strategy observation. Strategy identity comes from the
    /// RiskPilot read-only diagnostic contract or telemetry, never from reflection.
    /// </summary>
    public sealed class StrategyRecord : NormalizedRecord
    {
        public string StrategyId { get; set; }

        public StrategyActivity Activity { get; set; }

        /// <summary>Free-form reason codes reported alongside the strategy state.</summary>
        public string[] ReasonCodes { get; set; }

        public StrategyRecord()
        {
            StrategyId = string.Empty;
            ReasonCodes = new string[0];
        }
    }
}
