using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// The overall host-readiness state, derived from the five independent readiness facts. It is
    /// the single label M1 reports; the individual facts are reported alongside it so a reader can
    /// see exactly which one is still outstanding.
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

        /// <summary>Host reachable, but no target instrument has been resolved yet.</summary>
        public const string AwaitingInstrument = "AWAITING_INSTRUMENT";

        /// <summary>Instrument resolved, but its master metadata is not yet readable.</summary>
        public const string AwaitingMetadata = "AWAITING_METADATA";

        /// <summary>Everything is ready except the market-data subscription.</summary>
        public const string AwaitingSubscription = "AWAITING_SUBSCRIPTION";

        /// <summary>All five readiness facts hold.</summary>
        public const string Ready = "READY";
    }

    /// <summary>Stable reason codes emitted with the readiness observation.</summary>
    public static class HostReadinessReasonCodes
    {
        public const string Unobservable = "HOST_READINESS_UNOBSERVABLE";
        public const string NotOperational = "ADDON_NOT_OPERATIONAL";
        public const string ShuttingDown = "ADDON_SHUTTING_DOWN";
        public const string HostUnavailable = "HOST_OBJECT_MODEL_UNAVAILABLE";
        public const string InstrumentUnavailable = "INSTRUMENT_NOT_RESOLVED";
        public const string MetadataUnavailable = "HOST_METADATA_UNREADABLE";
        public const string NotSubscribed = "MARKET_DATA_NOT_SUBSCRIBED";
        public const string Ready = "HOST_READY";
    }

    /// <summary>
    /// Read-only observation of the host-readiness model. The five concepts are reported
    /// <b>separately</b> instead of being collapsed into one Boolean, because the previous single
    /// <c>HostIsLive</c> flag conflated the AddOn lifecycle with host availability and, being keyed
    /// on data-processing states an AddOn never reaches, was permanently false:
    /// <list type="number">
    /// <item><see cref="AddOnState"/> / <see cref="LifecycleOperational"/> — the AddOn lifecycle;</item>
    /// <item><see cref="HostAvailable"/> — the NinjaTrader host object model is reachable;</item>
    /// <item><see cref="MetadataReady"/> — the resolved instrument's master metadata is readable;</item>
    /// <item><see cref="InstrumentReady"/> — a target instrument has been resolved;</item>
    /// <item><see cref="MarketDataSubscribed"/> — the Level I/II streams are attached.</item>
    /// </list>
    /// <see cref="FullyReady"/> is true only when all five hold. A missing surface is
    /// <see cref="HostReadinessState.NotObserved"/> and is never reported as ready (fail closed).
    /// </summary>
    public sealed class HostReadinessObservation
    {
        /// <summary>True when the host layer supplied a readiness surface at all.</summary>
        public bool SurfaceObservable { get; set; }

        /// <summary>The raw AddOn <c>State</c> member last observed, or empty.</summary>
        public string AddOnState { get; set; }

        /// <summary>True when the AddOn reached an operational state (its <c>Active</c> state).</summary>
        public bool LifecycleOperational { get; set; }

        /// <summary>True when the AddOn observed its shutdown state.</summary>
        public bool ShuttingDown { get; set; }

        /// <summary>True when the NinjaTrader host object model was reachable.</summary>
        public bool HostAvailable { get; set; }

        /// <summary>True when the resolved instrument's master metadata was readable.</summary>
        public bool MetadataReady { get; set; }

        /// <summary>True when a target instrument was resolved.</summary>
        public bool InstrumentReady { get; set; }

        /// <summary>True when the Level I/II market-data streams were attached.</summary>
        public bool MarketDataSubscribed { get; set; }

        /// <summary>True only when every readiness fact holds.</summary>
        public bool FullyReady { get; set; }

        /// <summary>The derived overall state (<see cref="HostReadinessState"/>).</summary>
        public string State { get; set; }

        /// <summary>The reason codes explaining the current state.</summary>
        public IList<string> ReasonCodes { get; set; }

        public HostReadinessObservation()
        {
            AddOnState = string.Empty;
            State = HostReadinessState.NotObserved;
            ReasonCodes = new List<string>();
        }
    }
}
