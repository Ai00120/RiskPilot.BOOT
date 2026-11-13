using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Regression tests for the M4 canonical ES economic specification (D-095 §4). They drive the
    /// real verifier and the real M4 probe and assert the fail-closed rules: a wrong point value,
    /// tick size, derived tick value or currency is a FAIL; a missing field is INCONCLUSIVE (never
    /// a PASS); a stub/mock value is INCONCLUSIVE; and the check is emitted even when no
    /// observation is supplied.
    /// </summary>
    public class EsProductSpecificationTests
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

        /// <summary>A fully observed, fully matching ES economics observation.</summary>
        private static EsProductSpecificationObservation ExactEconomics()
        {
            return new EsProductSpecificationObservation
            {
                InstrumentSurfaceObservable = true,
                InstrumentObserved = true,
                InstrumentRef = "instrument#abc1234567",
                PointValue = 50.0d,
                PointValueObservable = true,
                TickSize = 0.25d,
                TickSizeObservable = true,
                TickValue = 12.5d,
                TickValueObservable = true,
                Currency = "USD",
                CurrencyObservable = true
            };
        }

        private static FakeDataProvider ProviderWithSnapshot(EsProductSpecificationObservation observation)
        {
            return new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { EsProductSpecification = observation }
            };
        }

        [Fact]
        public void ExactEsEconomics_IsPass()
        {
            Assert.Equal(DiagnosticStatus.PASS, EsProductSpecificationVerifier.Evaluate(ExactEconomics()).Status);
        }

        [Fact]
        public void MissingObservation_IsInconclusive()
        {
            var verdict = EsProductSpecificationVerifier.Evaluate(null);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(EsProductSpecificationReasonCodes.Unobservable, verdict.ReasonCodes);
        }

        [Fact]
        public void StubOrMockValue_IsInconclusive()
        {
            var observation = ExactEconomics();
            observation.IsStubOrMockValue = true;

            var verdict = EsProductSpecificationVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(EsProductSpecificationReasonCodes.StubOrMock, verdict.ReasonCodes);
        }

        [Fact]
        public void NoInstrumentObserved_IsInconclusive()
        {
            var observation = new EsProductSpecificationObservation { InstrumentSurfaceObservable = true };
            var verdict = EsProductSpecificationVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(EsProductSpecificationReasonCodes.NoInstrumentObserved, verdict.ReasonCodes);
        }

        [Fact]
        public void WrongPointValue_IsFail()
        {
            var observation = ExactEconomics();
            observation.PointValue = 20.0d; // MES point value, not ES.

            var verdict = EsProductSpecificationVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(EsProductSpecificationReasonCodes.PointValueMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void WrongTickSize_IsFail()
        {
            var observation = ExactEconomics();
            observation.TickSize = 0.5d;

            var verdict = EsProductSpecificationVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(EsProductSpecificationReasonCodes.TickSizeMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void WrongDerivedTickValue_IsFail()
        {
            var observation = ExactEconomics();
            observation.TickValue = 25.0d;

            var verdict = EsProductSpecificationVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(EsProductSpecificationReasonCodes.TickValueMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void WrongCurrency_IsFail()
        {
            var observation = ExactEconomics();
            observation.Currency = "EUR";

            var verdict = EsProductSpecificationVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(EsProductSpecificationReasonCodes.CurrencyMismatch, verdict.ReasonCodes);
        }

        [Fact]
        public void MismatchOutranksUnobservableField()
        {
            // A definite contradiction (wrong point value) outranks an unobservable field
            // (currency): the specification is proven wrong, not merely unproven.
            var observation = ExactEconomics();
            observation.PointValue = 20.0d;
            observation.CurrencyObservable = false;

            var verdict = EsProductSpecificationVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
        }

        [Fact]
        public void UnobservableField_IsInconclusive_NeverPass()
        {
            var observation = ExactEconomics();
            observation.TickSizeObservable = false;

            var verdict = EsProductSpecificationVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(EsProductSpecificationReasonCodes.IncompleteMetadata, verdict.ReasonCodes);
        }

        [Fact]
        public void TickValueAbsent_IsInconclusive()
        {
            // A host that exposes no tick value cannot prove the derived tick value.
            var observation = ExactEconomics();
            observation.TickValueObservable = false;

            var verdict = EsProductSpecificationVerifier.Evaluate(observation);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(EsProductSpecificationReasonCodes.TickValueUnobservable, verdict.ReasonCodes);
        }

        [Fact]
        public void Probe_ExactEconomics_ReportsPass()
        {
            var results = new M4InstrumentAttestationProbe().Evaluate(Context(ProviderWithSnapshot(ExactEconomics())));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M4.ES_PRODUCT_SPEC").Status);
        }

        [Fact]
        public void Probe_WrongEconomics_ReportsFail()
        {
            var observation = ExactEconomics();
            observation.PointValue = 20.0d;

            var results = new M4InstrumentAttestationProbe().Evaluate(Context(ProviderWithSnapshot(observation)));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M4.ES_PRODUCT_SPEC").Status);
        }

        [Fact]
        public void Probe_NoObservation_StillEmitsCheckAsInconclusive()
        {
            // The ES product-spec check is emitted even when no observation is supplied, so the
            // module never silently omits it and never reports it as PASS.
            var provider = new FakeDataProvider { Snapshot = new DiagnosticSnapshot() };
            var results = new M4InstrumentAttestationProbe().Evaluate(Context(provider));

            var check = Find(results, "M4.ES_PRODUCT_SPEC");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, check.Status);
            Assert.Contains(EsProductSpecificationReasonCodes.Unobservable, check.ReasonCodes);
        }

        [Fact]
        public void Probe_CarriesNoRawSymbol()
        {
            var results = new M4InstrumentAttestationProbe().Evaluate(Context(ProviderWithSnapshot(ExactEconomics())));
            var check = Find(results, "M4.ES_PRODUCT_SPEC");
            Assert.DoesNotContain("ES 12-26", check.Observation ?? string.Empty);
        }

        [Fact]
        public void ExpectedSpecification_FactsAreCanonical()
        {
            Assert.Equal(50.0d, ExpectedEsProductSpecification.PointValue);
            Assert.Equal(0.25d, ExpectedEsProductSpecification.TickSize);
            Assert.Equal(12.50d, ExpectedEsProductSpecification.TickValue);
            Assert.Equal("USD", ExpectedEsProductSpecification.Currency);

            // The derived tick value is consistent with point value × tick size.
            Assert.True(ExpectedEsProductSpecification.TickValueMatches(
                ExpectedEsProductSpecification.PointValue * ExpectedEsProductSpecification.TickSize));
        }
    }
}
