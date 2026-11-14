namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>
    /// Read-only, privacy-preserving account snapshot. The account name is never written
    /// verbatim; a stable synthetic reference is used instead (see SyntheticRef).
    /// </summary>
    public sealed class AccountRecord : NormalizedRecord
    {
        /// <summary>Non-reversible reference token for the account identity.</summary>
        public string AccountRef { get; set; }

        /// <summary>Account class observed (SIMULATION / LIVE), never a raw id.</summary>
        public EnvironmentClass AccountClass { get; set; }

        public string Denomination { get; set; }

        public string ProviderName { get; set; }

        /// <summary>
        /// Open positions read from the verified target account's <c>Account.Positions</c>.
        /// <c>null</c> when the collection was missing or unreadable (unknown); <c>0</c> only
        /// when a readable collection was confirmed empty.
        /// </summary>
        public int? PositionCount { get; set; }

        /// <summary>
        /// Active orders read from the verified target account's <c>Account.Orders</c>.
        /// <c>null</c> when the collection was missing, unreadable, or held an order whose
        /// state could not be classified (unknown); <c>0</c> only when a readable, fully
        /// classified collection was confirmed empty.
        /// </summary>
        public int? WorkingOrderCount { get; set; }

        /// <summary>True when execution records were read; false keeps them out of scope.</summary>
        public bool ExecutionsInScope { get; set; }

        public AccountRecord()
        {
            AccountRef = string.Empty;
            Denomination = string.Empty;
            ProviderName = string.Empty;
        }
    }
}
