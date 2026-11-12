namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// Privacy-preserving observation of the relationship between the target account and the
    /// connection/provider it is actually bound to. The host layer reads the raw account and
    /// connection metadata and reduces it here to booleans, counts and a synthetic account
    /// reference, so no raw account or connection name ever reaches the core, a finding, a log
    /// or an evidence artifact.
    /// <para>
    /// This is the evidence that answers "which connection does the target account actually
    /// belong to?", so the diagnostic never has to guess from the first (or any arbitrary)
    /// connection in <c>Connection.Connections</c>. <see cref="ConnectionCount"/> and
    /// <see cref="MatchingConnectionCount"/> expose how many connections the host enumerated and
    /// how many of them match the required connection identity, which is how a multiple-connection
    /// or ambiguous-association condition is detected rather than silently accepted.
    /// </para>
    /// <para>
    /// Every "Observable" flag distinguishes "the host reported this field" from "the field could
    /// not be read". A field that was not observable can never support a PASS, and a field that
    /// was observed and disagrees with the required association is a definite FAIL.
    /// </para>
    /// </summary>
    public sealed class TargetAccountConnectionObservation
    {
        /// <summary>True when the host could enumerate the account surface at all.</summary>
        public bool AccountSurfaceObservable { get; set; }

        /// <summary>
        /// True when the observation originates from a compile-time stub or a test double rather
        /// than the real host. A stub/mock value can never establish the real association.
        /// </summary>
        public bool IsStubOrMockValue { get; set; }

        /// <summary>Number of accounts the host enumerated.</summary>
        public int ObservedAccountCount { get; set; }

        /// <summary>Number of accounts whose name matched the required account name exactly.</summary>
        public int MatchingAccountCount { get; set; }

        /// <summary>Synthetic reference of the single matched account, when exactly one matched.</summary>
        public string AccountRef { get; set; }

        /// <summary>Number of connections the host enumerated (used to detect multiple connections).</summary>
        public int ConnectionCount { get; set; }

        /// <summary>
        /// Number of enumerated connections whose name and provider both match the required
        /// connection identity. More than one is an ambiguous association.
        /// </summary>
        public int MatchingConnectionCount { get; set; }

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

        public TargetAccountConnectionObservation()
        {
            AccountRef = string.Empty;
        }
    }
}
