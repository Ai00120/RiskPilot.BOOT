using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// How the provider identity was attributed to a connection. It records whether the provider
    /// was read from the target account's own connection (authoritative) or from a single
    /// unambiguous connection, so a reader can see the tool never assumed the first connection.
    /// </summary>
    public static class ProviderResolutionBasis
    {
        /// <summary>Read from the exact target account's own associated connection.</summary>
        public const string TargetAccountConnection = "TARGET_ACCOUNT_CONNECTION";

        /// <summary>Read from the only enumerated connection (unambiguous, no target account).</summary>
        public const string SingleConnection = "SINGLE_CONNECTION";

        /// <summary>No connection could be attributed to the provider (fail closed).</summary>
        public const string Unavailable = "UNAVAILABLE";
    }

    /// <summary>
    /// Provider identity evidence for M2. Records the *raw* provider value the host reported
    /// and how it was resolved, plus whether the value is real runtime evidence or a
    /// stub/mock value. The module must never treat a stub value as proof of the real
    /// provider identity (PR #275 resolved the provider by string comparison because no
    /// Rithmic enum member is guaranteed).
    /// </summary>
    public sealed class ProviderEvidence
    {
        /// <summary>Raw provider value as reported by the host (<c>Options.Provider.ToString()</c>).</summary>
        public string RawProviderValue { get; set; }

        /// <summary>How the value was resolved ("String" / "Enum" / "Unavailable").</summary>
        public string ResolutionMethod { get; set; }

        /// <summary>
        /// Which connection the provider was attributed to
        /// (<see cref="ProviderResolutionBasis"/>). Never the first of several connections.
        /// </summary>
        public string ResolutionBasis { get; set; }

        /// <summary>True when the raw value equals the expected evidence provider ("Rithmic").</summary>
        public bool MatchesExpectedProvider { get; set; }

        /// <summary>
        /// True when the observed value originates from a compile-time stub or a test double
        /// rather than the real host. A stub value can never establish the real provider.
        /// </summary>
        public bool IsStubOrMockValue { get; set; }

        /// <summary>True when the provider build/version was observable (never, in practice).</summary>
        public bool ProviderBuildObservable { get; set; }

        /// <summary>Reason the provider build is unobservable.</summary>
        public string ProviderBuildUnobservableReason { get; set; }

        public ProviderEvidence()
        {
            RawProviderValue = string.Empty;
            ResolutionMethod = string.Empty;
            ResolutionBasis = ProviderResolutionBasis.Unavailable;
            ProviderBuildUnobservableReason = "NO_PUBLIC_API_FOR_PROVIDER_BUILD";
        }
    }

    /// <summary>
    /// Account-to-connection association evidence for M2. Records whether each observed
    /// account could be associated with a connection and the provider behind it. Identities
    /// are hashed; the raw account name is never carried here.
    /// </summary>
    public sealed class AccountConnectionAssociation
    {
        public string AccountRef { get; set; }

        /// <summary>Provider name reported for the account, when readable.</summary>
        public string AccountProvider { get; set; }

        /// <summary>True when the account reported an associated connection.</summary>
        public bool HasConnection { get; set; }

        /// <summary>Name of the associated connection, when readable.</summary>
        public string ConnectionName { get; set; }

        /// <summary>Provider reported by the associated connection, when readable.</summary>
        public string ConnectionProvider { get; set; }

        /// <summary>True when the account provider and connection provider agree.</summary>
        public bool ProvidersAgree { get; set; }

        public AccountConnectionAssociation()
        {
            AccountRef = string.Empty;
            AccountProvider = string.Empty;
            ConnectionName = string.Empty;
            ConnectionProvider = string.Empty;
        }
    }

    /// <summary>
    /// Complete read-only observation set for one diagnostic pass. The host layer fills it;
    /// every probe reads from it. Each member may be null when the host could not provide it,
    /// and a null member is always reported as INCONCLUSIVE, never PASS.
    /// </summary>
    public sealed class DiagnosticSnapshot
    {
        public RuntimeObservation Runtime { get; set; }

        /// <summary>
        /// AddOn/host lifecycle observation for M1. It carries the raw <c>State</c> transitions the
        /// host reported; the host-free <see cref="Core.Identity.LifecycleVerifier"/> classifies
        /// them. A null member means the lifecycle surface could not be observed, which M1 reports
        /// as INCONCLUSIVE.
        /// </summary>
        public LifecycleObservation Lifecycle { get; set; }

        public ProviderEvidence Provider { get; set; }

        public IList<AccountConnectionAssociation> AccountAssociations { get; set; }

        /// <summary>
        /// Exact account identity observation for M2. It carries only booleans, counts and a
        /// synthetic account reference; a null member means the identity surface could not be
        /// observed, which M2 reports as INCONCLUSIVE.
        /// </summary>
        public AccountIdentityObservation AccountIdentity { get; set; }

        /// <summary>
        /// Observation of the target account's actual connection/provider association for M2. It
        /// proves which connection the target account is bound to, instead of assuming the first
        /// (or any arbitrary) connection. Like the identity observation it carries only booleans,
        /// counts and a synthetic account reference; a null member means the association surface
        /// could not be observed, which M2 reports as INCONCLUSIVE.
        /// </summary>
        public TargetAccountConnectionObservation TargetAccountConnection { get; set; }

        public OrderLifecycleObservation OrderLifecycle { get; set; }

        /// <summary>M9 post-trade audit observation (already-recorded events only).</summary>
        public PostTradeAuditObservation PostTradeAudit { get; set; }

        public InstrumentAttestationObservation Attestation { get; set; }

        /// <summary>
        /// Observation of the instrument NinjaTrader actually has selected/loaded for M4. It
        /// carries only booleans, counts and a synthetic instrument reference; a null member means
        /// the instrument surface could not be observed, which M4 reports as INCONCLUSIVE. This is
        /// distinct from <see cref="Attestation"/>: the attestation covers the D-078/D-096
        /// operator gate, this covers the raw selected-instrument identity.
        /// </summary>
        public InstrumentIdentityObservation InstrumentIdentity { get; set; }

        public DomMarketDataObservation DomMarketData { get; set; }

        public TradeDeltaObservation TradeDelta { get; set; }

        public StrategyRiskGateObservation StrategyRiskGate { get; set; }

        public SafetyObservation Safety { get; set; }

        public DiagnosticSnapshot()
        {
            AccountAssociations = new List<AccountConnectionAssociation>();
        }
    }
}
