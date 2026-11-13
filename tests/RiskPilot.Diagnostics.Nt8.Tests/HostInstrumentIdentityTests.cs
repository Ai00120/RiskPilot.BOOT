using System.Collections.Generic;
using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Tests for the host-layer instrument-identity adapter over the OFFLINE NinjaTrader stub
    /// surface. They exercise the real <see cref="Nt8HostDataProvider"/> discovery and mapping
    /// code (loaded-instrument enumeration, exact root comparison, instrument type/currency
    /// mapping and the D-096 configured-route venue confirmation) so a regression in the adapter is
    /// caught offline. This validates the adapter against the stub surface only; it is NOT proof of
    /// real host behaviour.
    /// </summary>
    [Collection("StubHostState")]
    public class HostInstrumentIdentityTests
    {
        private static void Reset()
        {
            Instrument.All.Clear();
            Account.All.Clear();
            Connection.Connections.Clear();
        }

        private static Connection ConnectedRithmic()
        {
            return new Connection
            {
                Status = ConnectionStatus.Connected,
                PriceStatus = ConnectionStatus.Connected,
                Options = new ConnectOptions
                {
                    Name = "My Rithmic",
                    Provider = Provider.Rithmic,
                    Mode = Mode.Simulation
                }
            };
        }

        private static Instrument EsInstrument(Exchange selected = Exchange.Globex, IList<Exchange> configured = null)
        {
            return new Instrument
            {
                FullName = "ES 12-26",
                Exchange = selected,
                MasterInstrument = new MasterInstrument
                {
                    Name = "ES",
                    InstrumentType = InstrumentType.Future,
                    Currency = Currency.UsDollar,
                    TickSize = 0.25,
                    PointValue = 50,
                    Exchanges = new List<Exchange>(configured ?? new List<Exchange> { Exchange.Globex })
                }
            };
        }

        [Fact]
        public void Host_ExactEsCme_ProducesPassingObservation()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.True(observation.InstrumentSurfaceObservable);
                Assert.Equal(1, observation.ObservedInstrumentCount);
                Assert.Equal(1, observation.MatchingInstrumentCount);
                Assert.True(observation.RootSymbolMatches);
                Assert.True(observation.InstrumentTypeMatches);
                Assert.True(observation.CurrencyMatches);
                Assert.True(observation.ExchangeMetadataObservable);
                Assert.True(observation.ExchangeConfirmedCme);
                Assert.True(observation.CanonicalIdMatches);
                Assert.Equal(DiagnosticStatus.PASS, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_WrongInstrumentRoot_IsFail()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var nq = EsInstrument();
                nq.FullName = "NQ 12-26";
                nq.MasterInstrument.Name = "NQ";
                Instrument.All.Add(nq);

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.Equal(0, observation.MatchingInstrumentCount);
                Assert.Equal(DiagnosticStatus.FAIL, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_MissingInstrument_IsInconclusive()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.True(observation.InstrumentSurfaceObservable);
                Assert.Equal(0, observation.ObservedInstrumentCount);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_AmbiguousInstrument_IsInconclusive()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());
                Instrument.All.Add(EsInstrument());

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.Equal(2, observation.MatchingInstrumentCount);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_MasterMetadataNotReady_IsInconclusive_NeverFail()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());

                var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.True(observation.InstrumentSurfaceObservable);
                Assert.Equal(1, observation.ObservedInstrumentCount);
                Assert.False(observation.RootMetadataObservable);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_MissingExchangeMetadata_IsInconclusive_NeverPass()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var instrument = EsInstrument();
                instrument.MasterInstrument.Exchanges = null;
                Instrument.All.Add(instrument);

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.False(observation.ExchangeMetadataObservable);
                Assert.False(observation.ExchangeConfirmedCme);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_UnsupportedRoute_IsFail()
        {
            Reset();
            try
            {
                // The instrument is selected on a non-Globex route, so the venue was observed and
                // does not confirm CME: a definite FAIL.
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument(Exchange.CBOT, new List<Exchange> { Exchange.CBOT }));

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.True(observation.ExchangeMetadataObservable);
                Assert.False(observation.ExchangeConfirmedCme);
                Assert.Equal(DiagnosticStatus.FAIL, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_NonRithmicProvider_IsFail()
        {
            Reset();
            try
            {
                // The route is Globex but the sole connected provider is not Rithmic, so the
                // configured route does not confirm the ES/CME evidence path: a definite FAIL.
                Connection.Connections.Add(new Connection
                {
                    Status = ConnectionStatus.Connected,
                    PriceStatus = ConnectionStatus.Connected,
                    Options = new ConnectOptions { Name = "Sim", Provider = Provider.Simulator, Mode = Mode.Simulation }
                });
                Instrument.All.Add(EsInstrument());

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.True(observation.ExchangeMetadataObservable);
                Assert.False(observation.ExchangeConfirmedCme);
                Assert.Equal(DiagnosticStatus.FAIL, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_MultipleConfiguredExchanges_IsFail()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument(Exchange.Globex, new List<Exchange> { Exchange.Globex, Exchange.CBOT }));

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.False(observation.ConfiguredExchangesAreGlobexOnly);
                Assert.Equal(DiagnosticStatus.FAIL, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_Observation_CarriesNoRawSymbol()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.StartsWith("instrument#", observation.InstrumentRef);
                Assert.DoesNotContain("ES 12-26", observation.InstrumentRef);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_ReadinessProbe_IsEvaluatedLive_NotCaptured()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());

                // A live probe that starts not-ready and flips later, mirroring an AddOn constructed
                // at State.Active that only reaches a data state afterwards.
                var ready = false;
                var provider = new Nt8HostDataProvider(() => ready);

                Assert.False(provider.GetSnapshot().InstrumentIdentity.RootMetadataObservable);

                ready = true;
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.True(observation.RootMetadataObservable);
                Assert.Equal(DiagnosticStatus.PASS, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_ThrowingReadinessProbe_FailsClosed()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());

                var provider = new Nt8HostDataProvider(() => throw new System.InvalidOperationException("boom"));
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.False(observation.RootMetadataObservable);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Attestation_ResolvesActualLoadedEsInstrument_WhenNoIdConfigured()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());

                // No configured id: the host must attest the instrument it actually has loaded,
                // resolved by the instrument's own FullName (never a configured string).
                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().Attestation;

                Assert.True(observation.HostAvailable);
                Assert.Equal("ES 12-26", observation.RequestedInstrumentId);
                Assert.NotNull(observation.Metadata);
                Assert.Equal("ES", observation.Metadata.RootName);
                Assert.True(observation.D096RouteCandidateSatisfied);
                Assert.Equal("ES_FUTURES_CME_DEC26", observation.CanonicalInstrumentId);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Attestation_NoLoadedEsInstrument_IsHostUnavailable()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var nq = EsInstrument();
                nq.FullName = "NQ 12-26";
                nq.MasterInstrument.Name = "NQ";
                Instrument.All.Add(nq);

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().Attestation;

                Assert.False(observation.HostAvailable);
                Assert.Contains(AttestationReasonCodes.HostUnavailable, observation.FailureReasons);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Attestation_AmbiguousLoadedEsInstruments_IsHostUnavailable()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var front = EsInstrument();
                var back = EsInstrument();
                back.FullName = "ES 03-27";
                Instrument.All.Add(front);
                Instrument.All.Add(back);

                var provider = new Nt8HostDataProvider(masterMetadataReady: true);
                var observation = provider.GetSnapshot().Attestation;

                // Two loaded ES roots cannot be disambiguated, so the target stays unresolved.
                Assert.False(observation.HostAvailable);
                Assert.Contains(AttestationReasonCodes.HostUnavailable, observation.FailureReasons);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Attestation_ConfiguredIdWins_OverLoadedSurface()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());

                var provider = new Nt8HostDataProvider(masterMetadataReady: true, requestedInstrumentId: "ES 12-26");
                var observation = provider.GetSnapshot().Attestation;

                Assert.True(observation.HostAvailable);
                Assert.Equal("ES 12-26", observation.RequestedInstrumentId);
                Assert.Equal("ES", observation.Metadata.RootName);
            }
            finally
            {
                Reset();
            }
        }
    }
}
