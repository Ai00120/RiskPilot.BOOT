namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// Privacy-preserving observation of the account identity surface. The host layer reads the
    /// raw account and connection metadata and reduces it here to booleans, counts and a
    /// synthetic account reference, so no raw account or connection name ever reaches the core,
    /// a finding, a log or an evidence artifact. The verifier
    /// (<see cref="Core.Identity.AccountIdentityVerifier"/>) turns this into a verdict.
    /// <para>
    /// Every "Observable" flag distinguishes "the host reported this field" from "the field
    /// could not be read". A field that was not observable can never support a PASS, and a field
    /// that was observed and disagrees with the required identity is a definite FAIL.
    /// </para>
    /// </summary>
    public sealed class AccountIdentityObservation
    {
        /// <summary>True when the host could enumerate the account surface at all.</summary>
        public bool AccountSurfaceObservable { get; set; }

        /// <summary>
        /// True when the observation originates from a compile-time stub or a test double rather
        /// than the real host. A stub/mock value can never establish real identity.
        /// </summary>
        public bool IsStubOrMockValue { get; set; }

        /// <summary>Number of accounts the host enumerated.</summary>
        public int ObservedAccountCount { get; set; }

        /// <summary>Number of accounts whose name matched the required name exactly.</summary>
        public int MatchingAccountCount { get; set; }

        /// <summary>Synthetic reference of the single matched account, when exactly one matched.</summary>
        public string AccountRef { get; set; }

        /// <summary>True when the account provider was read.</summary>
        public bool AccountProviderObservable { get; set; }

        /// <summary>True when the account provider equals the required provider.</summary>
        public bool AccountProviderMatches { get; set; }

        /// <summary>True when the matched account reported an associated connection.</summary>
        public bool ConnectionObserved { get; set; }

        /// <summary>True when the associated connection name was read.</summary>
        public bool ConnectionNameObservable { get; set; }

        /// <summary>True when the associated connection name equals the required name exactly.</summary>
        public bool ConnectionNameMatches { get; set; }

        /// <summary>True when the associated connection provider was read.</summary>
        public bool ConnectionProviderObservable { get; set; }

        /// <summary>True when the associated connection provider equals the required provider.</summary>
        public bool ConnectionProviderMatches { get; set; }

        /// <summary>True when the associated connection status was read.</summary>
        public bool ConnectionStatusObservable { get; set; }

        /// <summary>True when the associated connection reported itself connected.</summary>
        public bool ConnectionConnected { get; set; }

        /// <summary>True when the connection mode was read.</summary>
        public bool ModeObservable { get; set; }

        /// <summary>True when the connection mode is simulation.</summary>
        public bool ModeIsSimulation { get; set; }

        /// <summary>True when the account denomination was read.</summary>
        public bool DenominationObservable { get; set; }

        /// <summary>True when the account denomination is USD.</summary>
        public bool DenominationIsUsd { get; set; }

        public AccountIdentityObservation()
        {
            AccountRef = string.Empty;
        }
    }
}
