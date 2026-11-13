using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Regression tests for the M4 canonical ES/CME instrument identity. They drive the real
    /// verifier and the real M4 probe and assert the fail-closed rules: a wrong, missing or
    /// ambiguous instrument is never a PASS; missing exchange metadata is INCONCLUSIVE (never a
    /// PASS); an unsupported route is FAIL; and the venue is never inferred from the symbol text.
    /// </summary>
    public class InstrumentIdentityTests
    {
        private static ProbeContext Context(FakeDataProvider provider)
        {
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            return new ProbeContext(provider, session, new FakeClock(DateTime.UtcNow), "corr00000000");
        }

        private static DiagnosticResult Find(IEnumerable<DiagnosticResult> results, string checkId)
        {
            foreach (var result in results)
            {
                if (result.CheckId == checkId)
                {
                    return result;
                }
            }

            throw new InvalidOperationException("check not found: " + checkId);
        }

        /// <summary>
        /// A fully observed, fully matching ES/CME identity. Each test perturbs exactly one field
        /// so the failure mode is unambiguous.
        /// </summary>
        private static InstrumentIdentityObservation ExactIdentity()
        {
            return new InstrumentIdentityObservation
            {
                InstrumentSurfaceObservable = true,
                ObservedInstrumentCount = 1,
                RootMetadataObservable = true,
                MatchingInstrumentCount = 1,
                InstrumentRef = "instrument#abc1234567",
                RootSymbolObservable = true,
                RootSymbolMatches = true,
                InstrumentTypeObservable = true,
                InstrumentTypeMatches = true,
                CurrencyObservable = true,
                CurrencyMatches = true,
                SymbolParsed = true,
                CanonicalInstrumentId = "ES_FUTURES_CME_DEC26",
                CanonicalIdObservable = true,
                CanonicalIdMatches = true,
                ExchangeMetadataObservable = true,
                SelectedRouteIsGlobex = true,
                ConfiguredExchangesAreGlobexOnly = true,
                ProviderRouteIsRithmicOnly = true,
                ExchangeConfirmedCme = true
            };
        }

        private static FakeDataProvider ProviderWithSnapshot(InstrumentIdentityObservation observation)
        {
            return new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { InstrumentIdentity = observation }
            };
        }

        // ---------------- verifier: exact match ----------------

        [Fact]
        public void Verifier_ExactIdentity_IsPass()
        {
            var verdict = InstrumentIdentityVerifier.Evaluate(ExactIdentity());

            Assert.Equal(DiagnosticStatus.PASS, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.Match, verdict.ReasonCodes);
            Assert.Empty(verdict.MismatchedFields);
            Assert.Empty(verdict.UnobservableFields);
        }

        // ---------------- verifier: wrong / missing / ambiguous instrument ----------------

        [Fact]
        public void Verifier_WrongInstrumentRoot_IsFail()
        {
            // A similar root (e.g. "NQ") is not the ES root: it was observed and disagrees.
            var observation = ExactIdentity();
            observation.MatchingInstrumentCount = 0;
            var verdict = InstrumentIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.RootMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_MissingInstrument_IsInconclusive()
        {
            var observation = new InstrumentIdentityObservation
            {
                InstrumentSurfaceObservable = true,
                ObservedInstrumentCount = 0
            };
            var verdict = InstrumentIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.NoInstrumentObserved, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_AmbiguousInstrument_IsInconclusive()
        {
            var observation = ExactIdentity();
            observation.ObservedInstrumentCount = 2;
            observation.MatchingInstrumentCount = 2;
            var verdict = InstrumentIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.Ambiguous, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_UnobservableSurface_IsInconclusive()
        {
            var verdict = InstrumentIdentityVerifier.Evaluate(new InstrumentIdentityObservation());

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.Unobservable, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_RootMetadataUnobservable_IsInconclusive_NeverFail()
        {
            // The surface exists but no root symbol could be read, so a wrong instrument can never
            // be concluded; it must stay INCONCLUSIVE rather than report a false FAIL.
            var observation = new InstrumentIdentityObservation
            {
                InstrumentSurfaceObservable = true,
                ObservedInstrumentCount = 1,
                RootMetadataObservable = false,
                MatchingInstrumentCount = 0
            };
            var verdict = InstrumentIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.RootUnobservable, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_StubOrMockValue_IsInconclusive_NeverPass()
        {
            var observation = ExactIdentity();
            observation.IsStubOrMockValue = true;
            var verdict = InstrumentIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.StubOrMock, verdict.ReasonCodes);
        }

        // ---------------- verifier: field-level mismatches ----------------

        [Fact]
        public void Verifier_WrongInstrumentType_IsFail()
        {
            var observation = ExactIdentity();
            observation.InstrumentTypeMatches = false;
            var verdict = InstrumentIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.InstrumentTypeMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_WrongCurrency_IsFail()
        {
            var observation = ExactIdentity();
            observation.CurrencyMatches = false;
            var verdict = InstrumentIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.CurrencyMismatch, verdict.ReasonCodes);
        }

        // ---------------- verifier: exchange / route ----------------

        [Fact]
        public void Verifier_MissingExchangeMetadata_IsInconclusive_NeverPass()
        {
            // The exchange could not be read: an unavailable exchange confirmation is never a PASS.
            var observation = ExactIdentity();
            observation.ExchangeMetadataObservable = false;
            observation.SelectedRouteIsGlobex = false;
            observation.ConfiguredExchangesAreGlobexOnly = false;
            observation.ExchangeConfirmedCme = false;
            var verdict = InstrumentIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.ExchangeMetadataUnobservable, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_UnsupportedRoute_IsFail()
        {
            // The route metadata was observed and does not confirm CME: it is a definite FAIL.
            var observation = ExactIdentity();
            observation.SelectedRouteIsGlobex = false;
            observation.ExchangeConfirmedCme = false;
            var verdict = InstrumentIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.ExchangeMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_ExchangeNotInferredFromSymbolText()
        {
            // The symbol parsed and the root matched, but the venue was never confirmed from the
            // configured-route metadata. The canonical id must therefore be unobservable and the
            // identity must NOT PASS: "ES" text alone can never establish CME.
            var observation = ExactIdentity();
            observation.ExchangeMetadataObservable = true;
            observation.SelectedRouteIsGlobex = false;
            observation.ConfiguredExchangesAreGlobexOnly = false;
            observation.ProviderRouteIsRithmicOnly = false;
            observation.ExchangeConfirmedCme = false;
            observation.CanonicalIdObservable = false;
            observation.CanonicalIdMatches = false;
            observation.CanonicalInstrumentId = string.Empty;
            var verdict = InstrumentIdentityVerifier.Evaluate(observation);

            Assert.NotEqual(DiagnosticStatus.PASS, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.ExchangeMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_CanonicalIdMismatch_IsFail()
        {
            var observation = ExactIdentity();
            observation.CanonicalInstrumentId = "NQ_FUTURES_CME_DEC26";
            observation.CanonicalIdMatches = false;
            var verdict = InstrumentIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.CanonicalIdMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_MismatchOutranksUnobservableField()
        {
            // A proven-wrong field outranks an unobservable one: the identity is proven wrong.
            var observation = ExactIdentity();
            observation.CurrencyObservable = false;
            observation.CurrencyMatches = false;
            observation.InstrumentTypeMatches = false;
            var verdict = InstrumentIdentityVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.InstrumentTypeMismatch, verdict.ReasonCodes);
        }

        // ---------------- probe wiring ----------------

        [Fact]
        public void Probe_ExactIdentity_ReportsPass()
        {
            var results = new M4InstrumentAttestationProbe().Evaluate(Context(ProviderWithSnapshot(ExactIdentity())));
            var result = Find(results, "M4.INSTRUMENT_IDENTITY");

            Assert.Equal(DiagnosticStatus.PASS, result.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.Match, result.ReasonCodes);
        }

        [Fact]
        public void Probe_WrongInstrument_ReportsFail()
        {
            var observation = ExactIdentity();
            observation.MatchingInstrumentCount = 0;
            var results = new M4InstrumentAttestationProbe().Evaluate(Context(ProviderWithSnapshot(observation)));
            var result = Find(results, "M4.INSTRUMENT_IDENTITY");

            Assert.Equal(DiagnosticStatus.FAIL, result.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.RootMismatch, result.ReasonCodes);
        }

        [Fact]
        public void Probe_NoInstrumentIdentityObservation_ReportsInconclusive()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot()
            };
            var results = new M4InstrumentAttestationProbe().Evaluate(Context(provider));
            var result = Find(results, "M4.INSTRUMENT_IDENTITY");

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, result.Status);
            Assert.Contains(InstrumentIdentityReasonCodes.Unobservable, result.ReasonCodes);
        }

        [Fact]
        public void Probe_NeverEmitsRawSymbol()
        {
            var results = new M4InstrumentAttestationProbe().Evaluate(Context(ProviderWithSnapshot(ExactIdentity())));
            var result = Find(results, "M4.INSTRUMENT_IDENTITY");

            // The observation carries the synthetic reference, never a raw contract symbol.
            Assert.DoesNotContain("ES 12-26", result.Observation ?? string.Empty);
            Assert.DoesNotContain("ES 12-26", string.Join("|", result.ReasonCodes ?? new string[0]));
        }
    }
}
