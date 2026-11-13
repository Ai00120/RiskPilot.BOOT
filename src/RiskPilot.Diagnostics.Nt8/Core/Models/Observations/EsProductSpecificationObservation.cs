namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// Read-only observation of the loaded ES instrument's economic metadata, reduced to numbers,
    /// booleans and a synthetic instrument reference. The host layer reads the raw
    /// <c>MasterInstrument.*</c> economics and reduces it here; the verifier
    /// (<see cref="Core.Identity.EsProductSpecificationVerifier"/>) turns it into a verdict. No
    /// raw symbol is carried, and no verdict logic lives in the host layer.
    /// <para>
    /// Every "Observable" flag distinguishes "the host reported this field" from "the field could
    /// not be read". A field that was not observable can never support a PASS, and a field that
    /// was observed and contradicts the canonical CME ES specification is a definite FAIL.
    /// </para>
    /// </summary>
    public sealed class EsProductSpecificationObservation
    {
        /// <summary>True when the host could enumerate the loaded instrument surface at all.</summary>
        public bool InstrumentSurfaceObservable { get; set; }

        /// <summary>
        /// True when the observation originates from a compile-time stub or a test double rather
        /// than the real host. A stub/mock value can never establish the real economics.
        /// </summary>
        public bool IsStubOrMockValue { get; set; }

        /// <summary>True when a single ES root instrument was uniquely observed and read.</summary>
        public bool InstrumentObserved { get; set; }

        /// <summary>Synthetic reference of the selected instrument, or empty when none was read.</summary>
        public string InstrumentRef { get; set; }

        /// <summary>Point value reported by the host (<c>MasterInstrument.PointValue</c>).</summary>
        public double PointValue { get; set; }

        /// <summary>True when the host reported a point value.</summary>
        public bool PointValueObservable { get; set; }

        /// <summary>Tick size reported by the host (<c>MasterInstrument.TickSize</c>).</summary>
        public double TickSize { get; set; }

        /// <summary>True when the host reported a tick size.</summary>
        public bool TickSizeObservable { get; set; }

        /// <summary>Derived tick value (point value × tick size) when both inputs were readable.</summary>
        public double TickValue { get; set; }

        /// <summary>True when a tick value could be derived from the observed economics.</summary>
        public bool TickValueObservable { get; set; }

        /// <summary>
        /// Product currency: the ISO code "USD" when the approved currency was observed, otherwise
        /// the raw NT8 Currency member name (a definite mismatch), or empty when unreadable. No
        /// foreign currency is ever converted into USD here.
        /// </summary>
        public string Currency { get; set; }

        /// <summary>True when the host reported a product currency member.</summary>
        public bool CurrencyObservable { get; set; }

        public EsProductSpecificationObservation()
        {
            InstrumentRef = string.Empty;
            Currency = string.Empty;
        }
    }
}
