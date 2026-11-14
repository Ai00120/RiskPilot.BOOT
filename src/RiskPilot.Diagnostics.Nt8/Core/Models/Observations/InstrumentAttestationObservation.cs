using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// Host-reported instrument metadata, read only through documented public APIs and only
    /// after <c>DataLoaded</c>. Each field records whether it was actually observable so a
    /// missing value is never mistaken for an attested one.
    /// </summary>
    public sealed class InstrumentMetadataObservation
    {
        public string FullName { get; set; }

        public string ExpiryRaw { get; set; }

        /// <summary>DateTime kind of the expiry value (Unspecified / Utc / Local).</summary>
        public string ExpiryKind { get; set; }

        /// <summary>Selected route exchange as configured on the instrument.</summary>
        public string ExchangeRaw { get; set; }

        /// <summary>Root symbol from <c>MasterInstrument.Name</c>.</summary>
        public string RootName { get; set; }

        public string InstrumentTypeRaw { get; set; }

        public string CurrencyRaw { get; set; }

        public double? PointValue { get; set; }

        public double? TickSize { get; set; }

        /// <summary>Derived tick value = point value × tick size (no host field exists).</summary>
        public double? TickValue { get; set; }

        /// <summary>True when <c>MasterInstrument</c> was safe to read (post DataLoaded).</summary>
        public bool MasterMetadataAvailable { get; set; }

        /// <summary>
        /// True when <c>Instrument.FullName</c> was read (the getter did not throw and
        /// returned a value). A read failure leaves this false rather than reporting an
        /// empty string as a valid value.
        /// </summary>
        public bool FullNameObservable { get; set; }

        /// <summary>True when <c>Instrument.Expiry</c> was read without error.</summary>
        public bool ExpiryObservable { get; set; }

        /// <summary>True when <c>Instrument.Exchange</c> was read without error.</summary>
        public bool ExchangeObservable { get; set; }

        /// <summary>True when <c>MasterInstrument.Name</c> was read without error.</summary>
        public bool RootNameObservable { get; set; }

        /// <summary>True when <c>MasterInstrument.InstrumentType</c> was read without error.</summary>
        public bool InstrumentTypeObservable { get; set; }

        /// <summary>True when <c>MasterInstrument.Currency</c> was read without error.</summary>
        public bool CurrencyObservable { get; set; }

        /// <summary>
        /// Property reads that threw while reading host metadata. Each entry names the one
        /// field whose getter threw, so the read status of every field is preserved
        /// independently and one failed field never erases another field's result.
        /// </summary>
        public IList<string> ReadFailures { get; set; }

        /// <summary>True when at least one required property getter threw.</summary>
        public bool HasReadFailures
        {
            get { return ReadFailures != null && ReadFailures.Count > 0; }
        }

        public InstrumentMetadataObservation()
        {
            FullName = string.Empty;
            ExpiryRaw = string.Empty;
            ExpiryKind = string.Empty;
            ExchangeRaw = string.Empty;
            RootName = string.Empty;
            InstrumentTypeRaw = string.Empty;
            CurrencyRaw = string.Empty;
            ReadFailures = new List<string>();
        }
    }

    /// <summary>
    /// M4 instrument attestation observation. Reproduces the D-078 / D-096 preconditions
    /// from read-only host metadata and records the precise failure causes. The tool never
    /// approves anything: an approval is only ever observed, never created.
    /// </summary>
    public sealed class InstrumentAttestationObservation
    {
        /// <summary>Host metadata snapshot for the requested instrument, or null when unavailable.</summary>
        public InstrumentMetadataObservation Metadata { get; set; }

        /// <summary>Canonical instrument id derived from the host symbol, or null when unparseable.</summary>
        public string CanonicalInstrumentId { get; set; }

        /// <summary>Requested instrument id (already hashed/sanitized where it could be an id).</summary>
        public string RequestedInstrumentId { get; set; }

        /// <summary>Host availability: false means the metadata could not be read at all.</summary>
        public bool HostAvailable { get; set; }

        /// <summary>D-096 configured-route candidate: Globex selected and configured, Rithmic-only.</summary>
        public bool D096RouteCandidateSatisfied { get; set; }

        /// <summary>True when the host metadata matched the approved registry template.</summary>
        public bool RegistryMatched { get; set; }

        /// <summary>True when an operator D-078 field approval was observed for this contract.</summary>
        public bool D078Approved { get; set; }

        /// <summary>Attestation epoch the approval would have to be bound to.</summary>
        public long AttestationEpoch { get; set; }

        /// <summary>Precise failure causes, de-duplicated and in predicate order.</summary>
        public IList<string> FailureReasons { get; set; }

        public InstrumentAttestationObservation()
        {
            RequestedInstrumentId = string.Empty;
            CanonicalInstrumentId = string.Empty;
            FailureReasons = new List<string>();
        }
    }
}
