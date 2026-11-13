namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// Read-only observation of the instrument NinjaTrader actually has selected/loaded, reduced
    /// to booleans, counts and a synthetic instrument reference. The host layer reads the raw
    /// instrument metadata and reduces it here; the verifier
    /// (<see cref="Core.Identity.InstrumentIdentityVerifier"/>) turns it into a verdict. No
    /// verdict logic lives in the host layer.
    /// <para>
    /// Every "Observable" flag distinguishes "the host reported this field" from "the field could
    /// not be read". A field that was not observable can never support a PASS, and a field that
    /// was observed and contradicts the required ES/CME identity is a definite FAIL.
    /// </para>
    /// <para>
    /// The exchange is deliberately NOT derived from the symbol text: <see cref="ExchangeConfirmedCme"/>
    /// is only true when the observed configured-route metadata (selected route Globex, configured
    /// exchange set exactly {Globex}, sole connected provider path Rithmic) confirms CME. When that
    /// metadata cannot be read, <see cref="ExchangeMetadataObservable"/> is false and the identity
    /// can never PASS.
    /// </para>
    /// </summary>
    public sealed class InstrumentIdentityObservation
    {
        /// <summary>True when the host could enumerate the loaded instrument surface at all.</summary>
        public bool InstrumentSurfaceObservable { get; set; }

        /// <summary>
        /// True when the observation originates from a compile-time stub or a test double rather
        /// than the real host. A stub/mock value can never establish real identity.
        /// </summary>
        public bool IsStubOrMockValue { get; set; }

        /// <summary>Number of instruments the host enumerated on the discovery surface.</summary>
        public int ObservedInstrumentCount { get; set; }

        /// <summary>
        /// True when at least one observed instrument's master metadata (its root symbol) could be
        /// read. When false the surface exists but nothing about it is observable, so the identity
        /// stays INCONCLUSIVE rather than being reported as a wrong instrument.
        /// </summary>
        public bool RootMetadataObservable { get; set; }

        /// <summary>Number of observed instruments whose root symbol matched the required root exactly.</summary>
        public int MatchingInstrumentCount { get; set; }

        /// <summary>Synthetic reference of the single selected instrument, when exactly one matched.</summary>
        public string InstrumentRef { get; set; }

        /// <summary>True when the root symbol of the selected instrument was read.</summary>
        public bool RootSymbolObservable { get; set; }

        /// <summary>True when the root symbol equals the required root exactly.</summary>
        public bool RootSymbolMatches { get; set; }

        /// <summary>True when the instrument type of the selected instrument was read.</summary>
        public bool InstrumentTypeObservable { get; set; }

        /// <summary>True when the instrument type equals the required futures type.</summary>
        public bool InstrumentTypeMatches { get; set; }

        /// <summary>True when the currency of the selected instrument was read.</summary>
        public bool CurrencyObservable { get; set; }

        /// <summary>True when the currency equals the required currency.</summary>
        public bool CurrencyMatches { get; set; }

        /// <summary>True when the full contract symbol of the selected instrument parsed as a futures contract.</summary>
        public bool SymbolParsed { get; set; }

        /// <summary>Canonical instrument id derived from the host symbol and the mapped exchange, or empty.</summary>
        public string CanonicalInstrumentId { get; set; }

        /// <summary>True when a canonical instrument id could be computed (the exchange was mapped).</summary>
        public bool CanonicalIdObservable { get; set; }

        /// <summary>True when the canonical id reduces to the required ES/CME root/venue form.</summary>
        public bool CanonicalIdMatches { get; set; }

        /// <summary>True when the selected route exchange and the configured exchange set were readable.</summary>
        public bool ExchangeMetadataObservable { get; set; }

        /// <summary>True when the selected route exchange is Globex.</summary>
        public bool SelectedRouteIsGlobex { get; set; }

        /// <summary>True when the configured exchange set is exactly {Globex}.</summary>
        public bool ConfiguredExchangesAreGlobexOnly { get; set; }

        /// <summary>True when the sole connected provider path is Rithmic.</summary>
        public bool ProviderRouteIsRithmicOnly { get; set; }

        /// <summary>
        /// True when the observed configured-route metadata confirms CME. This is the D-096
        /// configured-route mapping and never a symbol-text inference.
        /// </summary>
        public bool ExchangeConfirmedCme { get; set; }

        public InstrumentIdentityObservation()
        {
            InstrumentRef = string.Empty;
            CanonicalInstrumentId = string.Empty;
        }
    }
}
