using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Identity
{
    /// <summary>
    /// The canonical economic specification of the CME E-mini S&amp;P 500 Futures root that the
    /// permanent RiskPilot.BOT identity is scoped to (Owner Decision D-095 §4 / D-096 §3). These
    /// are published CME contract facts used as reference expectations with explicit provenance;
    /// the host layer compares the loaded instrument's own metadata against them and reduces the
    /// result to booleans, counts and a synthetic reference, so no raw symbol is ever emitted.
    /// <para>
    /// This is distinct from <see cref="InstrumentIdentityVerifier"/>: that proves *which*
    /// instrument (root / venue / type / currency), this proves the instrument's *economics*
    /// (point value, tick size, derived tick value), which D-095 §4 makes part of the one
    /// immutable canonical ES specification.
    /// </para>
    /// </summary>
    public static class ExpectedEsProductSpecification
    {
        /// <summary>Canonical product label (registry metadata bound to the confirmed ES/CME identity).</summary>
        public const string ProductName = "E-mini S&P 500 Futures";

        /// <summary>USD value of one full index point (CME Rulebook Ch. 358).</summary>
        public const double PointValue = 50.0d;

        /// <summary>Minimum price increment, in index points (CME Rulebook Ch. 358).</summary>
        public const double TickSize = 0.25d;

        /// <summary>Derived USD value of one tick (point value × tick size). No host field exposes it.</summary>
        public const double TickValue = 12.50d;

        /// <summary>Required product currency (ISO code).</summary>
        public const string Currency = "USD";

        /// <summary>Small absolute tolerance for a host double compared against an exact decimal fact.</summary>
        public const double Tolerance = 1e-9d;

        public static bool PointValueMatches(double observed)
        {
            return Math.Abs(observed - PointValue) <= Tolerance;
        }

        public static bool TickSizeMatches(double observed)
        {
            return Math.Abs(observed - TickSize) <= Tolerance;
        }

        public static bool TickValueMatches(double observed)
        {
            return Math.Abs(observed - TickValue) <= Tolerance;
        }
    }

    /// <summary>Reason codes emitted by the ES product-specification verifier. Stable wire values.</summary>
    public static class EsProductSpecificationReasonCodes
    {
        public const string Match = "ES_PRODUCT_SPEC_MATCH";
        public const string Unobservable = "ES_PRODUCT_SPEC_UNOBSERVABLE";
        public const string StubOrMock = "ES_PRODUCT_SPEC_VALUE_FROM_STUB_OR_MOCK";
        public const string NoInstrumentObserved = "ES_PRODUCT_SPEC_NO_INSTRUMENT_OBSERVED";
        public const string PointValueMismatch = "ES_POINT_VALUE_MISMATCH";
        public const string PointValueUnobservable = "ES_POINT_VALUE_UNOBSERVABLE";
        public const string TickSizeMismatch = "ES_TICK_SIZE_MISMATCH";
        public const string TickSizeUnobservable = "ES_TICK_SIZE_UNOBSERVABLE";
        public const string TickValueMismatch = "ES_TICK_VALUE_MISMATCH";
        public const string TickValueUnobservable = "ES_TICK_VALUE_UNOBSERVABLE";
        public const string CurrencyMismatch = "ES_PRODUCT_CURRENCY_MISMATCH";
        public const string CurrencyUnobservable = "ES_PRODUCT_CURRENCY_UNOBSERVABLE";
        public const string IncompleteMetadata = "ES_PRODUCT_SPEC_INCOMPLETE_METADATA";
    }

    /// <summary>
    /// Outcome of the canonical ES economics comparison. It carries no raw symbol: only the
    /// status, the reason codes, the field-level detail and the counts a reader needs.
    /// </summary>
    public sealed class EsProductSpecificationVerdict
    {
        public DiagnosticStatus Status { get; private set; }

        /// <summary>Reason codes, most significant first (for example the primary mismatch).</summary>
        public IList<string> ReasonCodes { get; private set; }

        /// <summary>Fields that were observed and disagreed with the canonical specification.</summary>
        public IList<string> MismatchedFields { get; private set; }

        /// <summary>Required fields that could not be observed, so the economics cannot be proven.</summary>
        public IList<string> UnobservableFields { get; private set; }

        /// <summary>Synthetic instrument reference, or empty when no instrument was observable.</summary>
        public string InstrumentRef { get; private set; }

        private EsProductSpecificationVerdict()
        {
            ReasonCodes = new List<string>();
            MismatchedFields = new List<string>();
            UnobservableFields = new List<string>();
            InstrumentRef = string.Empty;
        }

        internal static EsProductSpecificationVerdict Create(
            DiagnosticStatus status,
            EsProductSpecificationObservation observation,
            IList<string> reasonCodes,
            IList<string> mismatchedFields,
            IList<string> unobservableFields)
        {
            return new EsProductSpecificationVerdict
            {
                Status = status,
                ReasonCodes = new List<string>(reasonCodes ?? new List<string>()),
                MismatchedFields = new List<string>(mismatchedFields ?? new List<string>()),
                UnobservableFields = new List<string>(unobservableFields ?? new List<string>()),
                InstrumentRef = observation != null ? (observation.InstrumentRef ?? string.Empty) : string.Empty
            };
        }
    }

    /// <summary>
    /// Compares the loaded ES instrument's own economics against the canonical CME ES
    /// specification. The comparison is fail-closed and never accepts a partial match:
    /// <list type="bullet">
    /// <item>a missing or unreadable instrument surface is INCONCLUSIVE;</item>
    /// <item>a stub/mock value is INCONCLUSIVE (it can never establish real economics);</item>
    /// <item>no ES instrument observed is INCONCLUSIVE (missing instrument);</item>
    /// <item>an observed economic field that contradicts the specification is FAIL;</item>
    /// <item>a required field that could not be observed is INCONCLUSIVE, never PASS;</item>
    /// <item>only a fully observed, fully matching specification is PASS.</item>
    /// </list>
    /// </summary>
    public static class EsProductSpecificationVerifier
    {
        public static EsProductSpecificationVerdict Evaluate(EsProductSpecificationObservation observation)
        {
            if (observation == null || !observation.InstrumentSurfaceObservable)
            {
                return Inconclusive(observation, EsProductSpecificationReasonCodes.Unobservable);
            }

            if (observation.IsStubOrMockValue)
            {
                return Inconclusive(observation, EsProductSpecificationReasonCodes.StubOrMock);
            }

            if (!observation.InstrumentObserved)
            {
                return Inconclusive(observation, EsProductSpecificationReasonCodes.NoInstrumentObserved);
            }

            var mismatched = new List<string>();
            var unobservable = new List<string>();

            if (!observation.PointValueObservable)
            {
                unobservable.Add(EsProductSpecificationReasonCodes.PointValueUnobservable);
            }
            else if (!ExpectedEsProductSpecification.PointValueMatches(observation.PointValue))
            {
                mismatched.Add(EsProductSpecificationReasonCodes.PointValueMismatch);
            }

            if (!observation.TickSizeObservable)
            {
                unobservable.Add(EsProductSpecificationReasonCodes.TickSizeUnobservable);
            }
            else if (!ExpectedEsProductSpecification.TickSizeMatches(observation.TickSize))
            {
                mismatched.Add(EsProductSpecificationReasonCodes.TickSizeMismatch);
            }

            // The tick value is derived (point value × tick size); it is only asserted when the
            // host reported a value, so a host that exposes no tick value is not failed for it.
            if (!observation.TickValueObservable)
            {
                unobservable.Add(EsProductSpecificationReasonCodes.TickValueUnobservable);
            }
            else if (!ExpectedEsProductSpecification.TickValueMatches(observation.TickValue))
            {
                mismatched.Add(EsProductSpecificationReasonCodes.TickValueMismatch);
            }

            if (!observation.CurrencyObservable)
            {
                unobservable.Add(EsProductSpecificationReasonCodes.CurrencyUnobservable);
            }
            else if (!ExpectedInstrumentIdentity.CurrencyMatches(observation.Currency))
            {
                mismatched.Add(EsProductSpecificationReasonCodes.CurrencyMismatch);
            }

            // A definite contradiction outranks an unobservable field: the specification is
            // proven wrong, not merely unproven.
            if (mismatched.Count > 0)
            {
                return EsProductSpecificationVerdict.Create(
                    DiagnosticStatus.FAIL, observation, mismatched, mismatched, unobservable);
            }

            if (unobservable.Count > 0)
            {
                return EsProductSpecificationVerdict.Create(
                    DiagnosticStatus.INCONCLUSIVE, observation,
                    Prefix(EsProductSpecificationReasonCodes.IncompleteMetadata, unobservable),
                    mismatched, unobservable);
            }

            return EsProductSpecificationVerdict.Create(
                DiagnosticStatus.PASS, observation,
                new List<string> { EsProductSpecificationReasonCodes.Match },
                mismatched, unobservable);
        }

        private static EsProductSpecificationVerdict Inconclusive(EsProductSpecificationObservation observation, string reasonCode)
        {
            return EsProductSpecificationVerdict.Create(
                DiagnosticStatus.INCONCLUSIVE, observation,
                new List<string> { reasonCode }, new List<string>(), new List<string>());
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
