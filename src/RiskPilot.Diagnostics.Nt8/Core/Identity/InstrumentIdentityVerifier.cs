using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Identity
{
    /// <summary>
    /// The permanent instrument identity RiskPilot.BOT requires for its CME ES evidence contract:
    /// the CME E-mini S&amp;P 500 Futures root instrument. These are the only instrument identity
    /// literals in production source; everything the verifier reports is a reason code, a count or
    /// a boolean, so the required identity is never emitted to a finding, a log or an artifact.
    /// <para>
    /// The canonical id is the single source of truth already carried by
    /// <see cref="Instruments.CanonicalInstrumentId.EvidenceIdentity"/>; it is referenced, never
    /// duplicated here.
    /// </para>
    /// </summary>
    public static class ExpectedInstrumentIdentity
    {
        /// <summary>Exact instrument root symbol (ordinal match; no partial or similar-root match).</summary>
        public const string RootSymbol = "ES";

        /// <summary>Required instrument type, as the host renders the NT8 <c>InstrumentType</c> member.</summary>
        public const string InstrumentType = "Future";

        /// <summary>Required listing venue, expressed as the ISO-style venue code.</summary>
        public const string Exchange = "CME";

        /// <summary>Required product currency.</summary>
        public const string Currency = "USD";

        /// <summary>
        /// The canonical identity prefix that the selected instrument must reduce to: the ES root
        /// on the CME venue. The expiry is deliberately NOT pinned here — the permanent identity is
        /// the ES root (D-095 §5), and the active contract rolls manually — so only the root/venue
        /// canonical form is required to match.
        /// </summary>
        public static string CanonicalRootVenuePrefix
        {
            get { return RootSymbol + "_FUTURES_" + Exchange + "_"; }
        }

        /// <summary>True when a derived canonical id is the ES/CME canonical form (any expiry).</summary>
        public static bool CanonicalRootVenueMatches(string canonicalInstrumentId)
        {
            return !string.IsNullOrEmpty(canonicalInstrumentId)
                && canonicalInstrumentId.StartsWith(CanonicalRootVenuePrefix, StringComparison.Ordinal);
        }

        /// <summary>The recorded canonical evidence identity (ES December 2026 on CME).</summary>
        public static string CanonicalInstrumentId
        {
            get { return Instruments.CanonicalInstrumentId.EvidenceIdentity; }
        }

        /// <summary>Root match is exact and case-sensitive.</summary>
        public static bool RootMatches(string observed)
        {
            return string.Equals(observed, RootSymbol, StringComparison.Ordinal);
        }

        /// <summary>Instrument type match is exact and case-sensitive.</summary>
        public static bool InstrumentTypeMatches(string observed)
        {
            return string.Equals(observed, InstrumentType, StringComparison.Ordinal);
        }

        /// <summary>Currency match is exact and case-sensitive (an ISO code, never a member name).</summary>
        public static bool CurrencyMatches(string observed)
        {
            return string.Equals(observed, Currency, StringComparison.Ordinal);
        }
    }

    /// <summary>Reason codes emitted by the instrument identity verifier. Stable wire values.</summary>
    public static class InstrumentIdentityReasonCodes
    {
        public const string Match = "INSTRUMENT_IDENTITY_MATCH";
        public const string Unobservable = "INSTRUMENT_IDENTITY_UNOBSERVABLE";
        public const string StubOrMock = "INSTRUMENT_IDENTITY_VALUE_FROM_STUB_OR_MOCK";
        public const string NoInstrumentObserved = "INSTRUMENT_IDENTITY_NO_INSTRUMENT_OBSERVED";
        public const string RootUnobservable = "INSTRUMENT_ROOT_UNOBSERVABLE";
        public const string Ambiguous = "INSTRUMENT_IDENTITY_AMBIGUOUS";
        public const string RootMismatch = "INSTRUMENT_ROOT_MISMATCH";
        public const string InstrumentTypeUnobservable = "INSTRUMENT_TYPE_UNOBSERVABLE";
        public const string InstrumentTypeMismatch = "INSTRUMENT_TYPE_MISMATCH";
        public const string CurrencyUnobservable = "CURRENCY_UNOBSERVABLE";
        public const string CurrencyMismatch = "CURRENCY_MISMATCH";
        public const string ExchangeMetadataUnobservable = "EXCHANGE_METADATA_UNOBSERVABLE";
        public const string ExchangeMismatch = "EXCHANGE_MISMATCH";
        public const string CanonicalIdUnobservable = "CANONICAL_ID_UNOBSERVABLE";
        public const string CanonicalIdMismatch = "CANONICAL_ID_MISMATCH";
        public const string IncompleteMetadata = "INSTRUMENT_IDENTITY_INCOMPLETE_METADATA";
    }

    /// <summary>
    /// Outcome of the exact instrument identity comparison. It carries no raw symbol: only the
    /// status, the reason codes, the field-level detail and the counts a reader needs to judge the
    /// result.
    /// </summary>
    public sealed class InstrumentIdentityVerdict
    {
        public DiagnosticStatus Status { get; private set; }

        /// <summary>Reason codes, most significant first (for example the primary mismatch).</summary>
        public IList<string> ReasonCodes { get; private set; }

        /// <summary>Fields that were observed and disagreed with the required identity.</summary>
        public IList<string> MismatchedFields { get; private set; }

        /// <summary>Required fields that could not be observed, so identity cannot be proven.</summary>
        public IList<string> UnobservableFields { get; private set; }

        public int ObservedInstrumentCount { get; private set; }

        public int MatchingInstrumentCount { get; private set; }

        /// <summary>Synthetic instrument reference, or empty when no instrument was observable.</summary>
        public string InstrumentRef { get; private set; }

        private InstrumentIdentityVerdict()
        {
            ReasonCodes = new List<string>();
            MismatchedFields = new List<string>();
            UnobservableFields = new List<string>();
            InstrumentRef = string.Empty;
        }

        internal static InstrumentIdentityVerdict Create(
            DiagnosticStatus status,
            InstrumentIdentityObservation observation,
            IList<string> reasonCodes,
            IList<string> mismatchedFields,
            IList<string> unobservableFields)
        {
            return new InstrumentIdentityVerdict
            {
                Status = status,
                ReasonCodes = new List<string>(reasonCodes ?? new List<string>()),
                MismatchedFields = new List<string>(mismatchedFields ?? new List<string>()),
                UnobservableFields = new List<string>(unobservableFields ?? new List<string>()),
                ObservedInstrumentCount = observation != null ? observation.ObservedInstrumentCount : 0,
                MatchingInstrumentCount = observation != null ? observation.MatchingInstrumentCount : 0,
                InstrumentRef = observation != null ? (observation.InstrumentRef ?? string.Empty) : string.Empty
            };
        }
    }

    /// <summary>
    /// Compares the instrument NinjaTrader actually has selected/loaded against the permanent
    /// ES/CME identity. The comparison is fail-closed and never accepts a partial match:
    /// <list type="bullet">
    /// <item>a missing or unreadable instrument surface is INCONCLUSIVE;</item>
    /// <item>no instrument observed at all is INCONCLUSIVE (missing instrument);</item>
    /// <item>an observed instrument whose root symbol is not exactly the required root is FAIL
    /// (a similar or differently-cased root is not a match);</item>
    /// <item>more than one instrument matching the required root is INCONCLUSIVE (ambiguous);</item>
    /// <item>an observed field that contradicts the required identity is FAIL;</item>
    /// <item>a required field that could not be observed — including unconfirmed exchange/route
    /// metadata — is INCONCLUSIVE, never PASS;</item>
    /// <item>only a fully observed, fully matching identity is PASS.</item>
    /// </list>
    /// The exchange is never inferred from the symbol text: the configured-route metadata must
    /// confirm CME.
    /// </summary>
    public static class InstrumentIdentityVerifier
    {
        public static InstrumentIdentityVerdict Evaluate(InstrumentIdentityObservation observation)
        {
            if (observation == null || !observation.InstrumentSurfaceObservable)
            {
                return Inconclusive(observation, InstrumentIdentityReasonCodes.Unobservable);
            }

            if (observation.IsStubOrMockValue)
            {
                return Inconclusive(observation, InstrumentIdentityReasonCodes.StubOrMock);
            }

            if (observation.ObservedInstrumentCount <= 0)
            {
                return Inconclusive(observation, InstrumentIdentityReasonCodes.NoInstrumentObserved);
            }

            // The surface exists but no root symbol could be read, so a wrong instrument can never
            // be concluded. Stay INCONCLUSIVE rather than reporting a false FAIL.
            if (!observation.RootMetadataObservable)
            {
                return Inconclusive(observation, InstrumentIdentityReasonCodes.RootUnobservable);
            }

            // A wrong instrument: instruments were observed and read, but none carries the
            // required root.
            if (observation.MatchingInstrumentCount == 0)
            {
                return Fail(observation, InstrumentIdentityReasonCodes.RootMismatch);
            }

            // More than one instrument carries the required root, so the selected identity is
            // ambiguous and cannot be proven.
            if (observation.MatchingInstrumentCount > 1)
            {
                return Inconclusive(observation, InstrumentIdentityReasonCodes.Ambiguous);
            }

            var mismatched = new List<string>();
            var unobservable = new List<string>();

            if (!observation.RootSymbolObservable)
            {
                unobservable.Add(InstrumentIdentityReasonCodes.RootUnobservable);
            }
            else if (!observation.RootSymbolMatches)
            {
                mismatched.Add(InstrumentIdentityReasonCodes.RootMismatch);
            }

            if (!observation.InstrumentTypeObservable)
            {
                unobservable.Add(InstrumentIdentityReasonCodes.InstrumentTypeUnobservable);
            }
            else if (!observation.InstrumentTypeMatches)
            {
                mismatched.Add(InstrumentIdentityReasonCodes.InstrumentTypeMismatch);
            }

            if (!observation.CurrencyObservable)
            {
                unobservable.Add(InstrumentIdentityReasonCodes.CurrencyUnobservable);
            }
            else if (!observation.CurrencyMatches)
            {
                mismatched.Add(InstrumentIdentityReasonCodes.CurrencyMismatch);
            }

            if (!observation.ExchangeMetadataObservable)
            {
                unobservable.Add(InstrumentIdentityReasonCodes.ExchangeMetadataUnobservable);
            }
            else if (!observation.ExchangeConfirmedCme)
            {
                mismatched.Add(InstrumentIdentityReasonCodes.ExchangeMismatch);
            }

            // The canonical id is the derived ES/CME identity (root + venue). It must be derivable
            // and must reduce to the ES/CME canonical form; the expiry is not pinned, so a manual
            // contract rollover keeps the root identity valid.
            if (!observation.CanonicalIdObservable)
            {
                unobservable.Add(InstrumentIdentityReasonCodes.CanonicalIdUnobservable);
            }
            else if (!observation.CanonicalIdMatches)
            {
                mismatched.Add(InstrumentIdentityReasonCodes.CanonicalIdMismatch);
            }

            // A definite contradiction outranks an unobservable field: the identity is proven
            // wrong, not merely unproven.
            if (mismatched.Count > 0)
            {
                return InstrumentIdentityVerdict.Create(
                    DiagnosticStatus.FAIL, observation, mismatched, mismatched, unobservable);
            }

            if (unobservable.Count > 0)
            {
                return InstrumentIdentityVerdict.Create(
                    DiagnosticStatus.INCONCLUSIVE, observation,
                    Prefix(InstrumentIdentityReasonCodes.IncompleteMetadata, unobservable),
                    mismatched, unobservable);
            }

            return InstrumentIdentityVerdict.Create(
                DiagnosticStatus.PASS, observation,
                new List<string> { InstrumentIdentityReasonCodes.Match },
                mismatched, unobservable);
        }

        private static InstrumentIdentityVerdict Inconclusive(InstrumentIdentityObservation observation, string reasonCode)
        {
            return InstrumentIdentityVerdict.Create(
                DiagnosticStatus.INCONCLUSIVE, observation,
                new List<string> { reasonCode }, new List<string>(), new List<string>());
        }

        private static InstrumentIdentityVerdict Fail(InstrumentIdentityObservation observation, string reasonCode)
        {
            return InstrumentIdentityVerdict.Create(
                DiagnosticStatus.FAIL, observation,
                new List<string> { reasonCode }, new List<string> { reasonCode }, new List<string>());
        }

        private static IList<string> Prefix(string first, IList<string> rest)
        {
            var list = new List<string> { first };
            if (rest != null)
            {
                list.AddRange(rest);
            }

            return list;
        }
    }
}
