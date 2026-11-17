using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// The three-valued state of one readiness fact. A Boolean cannot express the distinction the
    /// readiness model needs: a fact that was never observed is <see cref="Unknown"/>, which is
    /// neither true nor false. Keeping the third state is what stops an unobserved fact from being
    /// reported as satisfied (or as a definite failure).
    /// </summary>
    public enum ReadinessFactState
    {
        /// <summary>The fact was not observed (fail closed: never treated as satisfied).</summary>
        Unknown = 0,

        /// <summary>The fact was observed and holds.</summary>
        Satisfied = 1,

        /// <summary>The fact was observed and does not hold.</summary>
        Unsatisfied = 2
    }

    /// <summary>Helpers for <see cref="ReadinessFactState"/>.</summary>
    public static class ReadinessFactStates
    {
        /// <summary>Projects a Boolean observation onto a satisfied/unsatisfied fact state.</summary>
        public static ReadinessFactState Of(bool observed)
        {
            return observed ? ReadinessFactState.Satisfied : ReadinessFactState.Unsatisfied;
        }

        /// <summary>True only when the fact was observed and holds.</summary>
        public static bool IsSatisfied(this ReadinessFactState state)
        {
            return state == ReadinessFactState.Satisfied;
        }

        /// <summary>Stable wire string for evidence.</summary>
        public static string ToWire(this ReadinessFactState state)
        {
            switch (state)
            {
                case ReadinessFactState.Satisfied:
                    return "SATISFIED";
                case ReadinessFactState.Unsatisfied:
                    return "UNSATISFIED";
                default:
                    return "UNKNOWN";
            }
        }
    }

    /// <summary>
    /// The overall host-readiness state, derived from the individual readiness facts. It is the
    /// single label M1 reports; the individual facts are reported alongside it so a reader can see
    /// exactly which one is still outstanding. Readiness is never inferred from the mere fact the
    /// AddOn was constructed: every prerequisite must have been observed and must hold.
    /// </summary>
    public static class HostReadinessState
    {
        /// <summary>No readiness surface was supplied by the host layer.</summary>
        public const string NotObserved = "NOT_OBSERVED";

        /// <summary>The AddOn has not reached an operational lifecycle state (e.g. still Configure).</summary>
        public const string NotOperational = "ADDON_NOT_OPERATIONAL";

        /// <summary>The AddOn is shutting down; readiness will never complete.</summary>
        public const string ShuttingDown = "ADDON_SHUTTING_DOWN";

        /// <summary>Operational, but the NinjaTrader host object model is not yet reachable.</summary>
        public const string AwaitingHost = "AWAITING_HOST";

        /// <summary>The lifecycle does not yet permit attempting the host metadata read.</summary>
        public const string AwaitingMetadataRead = "AWAITING_METADATA_READ";

        /// <summary>The host is reachable, but no target instrument has been resolved yet.</summary>
        public const string AwaitingInstrument = "AWAITING_INSTRUMENT";

        /// <summary>The metadata read was permitted, but the required metadata has not been read.</summary>
        public const string AwaitingMetadata = "AWAITING_METADATA";

        /// <summary>The target account has not been verified (identity evidence incomplete).</summary>
        public const string AwaitingAccount = "AWAITING_ACCOUNT";

        /// <summary>The target account's connection association/state has not been verified.</summary>
        public const string AwaitingConnection = "AWAITING_CONNECTION";

        /// <summary>Everything is ready except the market-data subscription.</summary>
        public const string AwaitingSubscription = "AWAITING_SUBSCRIPTION";

        /// <summary>The market-data streams are subscribed, but no event has been observed yet.</summary>
        public const string AwaitingMarketData = "AWAITING_MARKET_DATA";

        /// <summary>Every readiness fact was observed and holds.</summary>
        public const string Ready = "READY";
    }

    /// <summary>Stable reason codes emitted with the readiness observation.</summary>
    public static class HostReadinessReasonCodes
    {
        public const string Unobservable = "HOST_READINESS_UNOBSERVABLE";
        public const string NotOperational = "ADDON_NOT_OPERATIONAL";
        public const string ShuttingDown = "ADDON_SHUTTING_DOWN";
        public const string HostUnavailable = "HOST_OBJECT_MODEL_UNAVAILABLE";
        public const string MetadataReadNotAllowed = "HOST_METADATA_READ_NOT_ALLOWED";
        public const string InstrumentUnavailable = "INSTRUMENT_NOT_RESOLVED";
        public const string MetadataUnavailable = "HOST_METADATA_UNREADABLE";
        public const string AccountUnverified = "TARGET_ACCOUNT_NOT_VERIFIED";
        public const string ConnectionUnverified = "TARGET_CONNECTION_NOT_VERIFIED";
        public const string NotSubscribed = "MARKET_DATA_NOT_SUBSCRIBED";
        public const string MarketDataNotObserved = "MARKET_DATA_NOT_OBSERVED";
        public const string Ready = "HOST_READY";
    }

    /// <summary>
    /// Read-only observation of the host-readiness model. Each readiness fact is reported
    /// <b>separately</b> and with its own three-valued state, instead of being collapsed into one
    /// Boolean or derived from another fact. The previous model conflated the AddOn lifecycle with
    /// metadata observability: it recorded <c>metadataReady</c> from the lifecycle gate, so an
    /// AddOn that merely reached <c>Active</c> reported metadata as ready even though
    /// <c>MasterInstrument.*</c> had never been read.
    /// <para>
    /// The facts, in dependency order:
    /// </para>
    /// <list type="number">
    /// <item><see cref="HostOperational"/> — the AddOn reached its operational lifecycle state;</item>
    /// <item><see cref="HostAvailable"/> — the NinjaTrader host object model is reachable;</item>
    /// <item><see cref="MetadataReadAllowed"/> — the lifecycle permits attempting the metadata read;</item>
    /// <item><see cref="InstrumentResolved"/> — the explicitly selected target instrument resolved uniquely;</item>
    /// <item><see cref="MetadataObserved"/> — the required metadata was actually read from the host adapter;</item>
    /// <item><see cref="AccountVerified"/> — the target account's identity evidence was verified;</item>
    /// <item><see cref="ConnectionVerified"/> — the account→connection association and state were verified;</item>
    /// <item><see cref="MarketDataSubscribed"/> — the Level I/II subscriptions were established;</item>
    /// <item><see cref="MarketDataObserved"/> — actual market-data events were received.</item>
    /// </list>
    /// <see cref="FullyReady"/> is true only when every fact is satisfied. A missing surface is
    /// <see cref="HostReadinessState.NotObserved"/> and is never reported as ready (fail closed).
    /// </summary>
    public sealed class HostReadinessObservation
    {
        /// <summary>True when the host layer supplied a readiness surface at all.</summary>
        public bool SurfaceObservable { get; set; }

        /// <summary>The raw AddOn <c>State</c> member last observed, or empty.</summary>
        public string AddOnState { get; set; }

        /// <summary>1. The AddOn reached an operational lifecycle state.</summary>
        public ReadinessFactState HostOperational { get; set; }

        /// <summary>True when the AddOn observed its shutdown state.</summary>
        public bool ShuttingDown { get; set; }

        /// <summary>2. The NinjaTrader host object model was reachable.</summary>
        public ReadinessFactState HostAvailable { get; set; }

        /// <summary>3. The lifecycle permits attempting the metadata read.</summary>
        public ReadinessFactState MetadataReadAllowed { get; set; }

        /// <summary>4. The explicitly selected target instrument was resolved uniquely.</summary>
        public ReadinessFactState InstrumentResolved { get; set; }

        /// <summary>5. The required metadata was actually read from the host adapter.</summary>
        public ReadinessFactState MetadataObserved { get; set; }

        /// <summary>6. The target account's identity evidence was verified.</summary>
        public ReadinessFactState AccountVerified { get; set; }

        /// <summary>7. The account→connection association and connection state were verified.</summary>
        public ReadinessFactState ConnectionVerified { get; set; }

        /// <summary>8. The Level I/II market-data streams were attached.</summary>
        public ReadinessFactState MarketDataSubscribed { get; set; }

        /// <summary>9. Actual market-data events were received.</summary>
        public ReadinessFactState MarketDataObserved { get; set; }

        /// <summary>True only when every readiness fact holds.</summary>
        public bool FullyReady { get; set; }

        /// <summary>The derived overall state (<see cref="HostReadinessState"/>).</summary>
        public string State { get; set; }

        /// <summary>
        /// The lifecycle epoch the facts belong to. A restarted AddOn begins a new epoch so a fact
        /// observed in an earlier epoch can never keep the restarted tool reported as ready.
        /// </summary>
        public long Epoch { get; set; }

        /// <summary>The reason codes explaining the current state.</summary>
        public IList<string> ReasonCodes { get; set; }

        /// <summary>Convenience: true when the AddOn lifecycle is operational.</summary>
        public bool LifecycleOperational
        {
            get { return HostOperational == ReadinessFactState.Satisfied; }
        }

        public HostReadinessObservation()
        {
            AddOnState = string.Empty;
            State = HostReadinessState.NotObserved;
            ReasonCodes = new List<string>();
        }
    }
}
