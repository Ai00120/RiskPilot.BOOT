using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Instruments;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Regression tests for the deterministic, explicit target-instrument selection that replaces
    /// the previous ES-root auto-discovery. They prove the host never picks an arbitrary ES contract
    /// from a collection of loaded contracts: the target is only ever the explicitly selected
    /// instrument, and an unset, unloaded or ambiguous selection fails closed.
    /// <para>
    /// They drive the real <see cref="InstrumentSelector"/>, the real
    /// <see cref="Nt8HostDataProvider"/> resolution and the real <see cref="DiagnosticAddOn"/>
    /// readiness over the OFFLINE NinjaTrader stub surface. This is NOT proof of real host
    /// behaviour.
    /// </para>
    /// </summary>
    [Collection("StubHostState")]
    public class InstrumentSelectionTests
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
                    Provider = Provider.Provider14,
                    Mode = Mode.Simulation
                }
            };
        }

        private static Instrument EsInstrument(string fullName = "ES 12-26", string root = "ES",
            Exchange selected = Exchange.Globex, IList<Exchange> configured = null)
        {
            return new Instrument
            {
                FullName = fullName,
                Exchange = selected,
                MasterInstrument = new MasterInstrument
                {
                    Name = root,
                    InstrumentType = InstrumentType.Future,
                    Currency = Currency.UsDollar,
                    TickSize = 0.25,
                    PointValue = 50,
                    Exchanges = new List<Exchange>(configured ?? new List<Exchange> { Exchange.Globex })
                }
            };
        }

        private static LoadedInstrumentCandidate Candidate(string fullName, string root, bool rootObservable = true)
        {
            return new LoadedInstrumentCandidate { FullName = fullName, RootName = root, RootObservable = rootObservable };
        }

        // ---------------- pure selector rule ----------------

        [Fact]
        public void Selector_ExplicitUniqueFullName_Resolves()
        {
            var loaded = new List<LoadedInstrumentCandidate>
            {
                Candidate("ES 12-26", "ES"),
                Candidate("ES 03-27", "ES")
            };

            var result = InstrumentSelector.Select("ES 12-26", true, loaded, false);

            Assert.True(result.IsResolved);
            Assert.Equal(0, result.SelectedIndex);
            Assert.Equal(1, result.CandidateCount);
            Assert.Equal(InstrumentSelectionStatus.Resolved, result.Status);
        }

        [Fact]
        public void Selector_NoSelection_IsNotConfigured_NeverPicksFirst()
        {
            var loaded = new List<LoadedInstrumentCandidate> { Candidate("ES 12-26", "ES") };

            var result = InstrumentSelector.Select(null, true, loaded, false);

            Assert.False(result.IsResolved);
            Assert.Equal(InstrumentSelectionStatus.NotConfigured, result.Status);
            Assert.Equal(-1, result.SelectedIndex);
        }

        [Fact]
        public void Selector_SelectionNotOnSurface_IsNotLoaded()
        {
            var loaded = new List<LoadedInstrumentCandidate> { Candidate("ES 12-26", "ES") };

            var result = InstrumentSelector.Select("ES 03-27", true, loaded, false);

            Assert.False(result.IsResolved);
            Assert.Equal(InstrumentSelectionStatus.NotLoaded, result.Status);
        }

        [Fact]
        public void Selector_InsufficientlySpecificRoot_IsMultipleCandidates()
        {
            var loaded = new List<LoadedInstrumentCandidate>
            {
                Candidate("ES 12-26", "ES"),
                Candidate("ES 03-27", "ES")
            };

            var result = InstrumentSelector.Select("ES", true, loaded, false);

            Assert.False(result.IsResolved);
            Assert.Equal(InstrumentSelectionStatus.MultipleCandidates, result.Status);
            Assert.Equal(2, result.CandidateCount);
        }

        [Fact]
        public void Selector_UnreadableRoot_DoesNotMatchByRoot()
        {
            // A candidate whose root could not be read must not be matched by the root rule; only
            // an exact full-name match can resolve it.
            var loaded = new List<LoadedInstrumentCandidate> { Candidate("ES 12-26", "ES", rootObservable: false) };

            var byRoot = InstrumentSelector.Select("ES", true, loaded, false);
            var byName = InstrumentSelector.Select("ES 12-26", true, loaded, false);

            Assert.Equal(InstrumentSelectionStatus.NotLoaded, byRoot.Status);
            Assert.True(byName.IsResolved);
        }

        [Fact]
        public void Selector_UnavailableSurface_IsSurfaceUnavailable()
        {
            var result = InstrumentSelector.Select("ES 12-26", false, new List<LoadedInstrumentCandidate>(), false);

            Assert.Equal(InstrumentSelectionStatus.SurfaceUnavailable, result.Status);
        }

        [Fact]
        public void Selector_StubOrMock_NeverResolves()
        {
            var loaded = new List<LoadedInstrumentCandidate> { Candidate("ES 12-26", "ES") };

            var result = InstrumentSelector.Select("ES 12-26", true, loaded, true);

            Assert.False(result.IsResolved);
            Assert.Equal(InstrumentSelectionStatus.StubOrMock, result.Status);
        }

        // ---------------- host adapter: exactly one valid selected ES contract ----------------

        [Fact]
        public void Host_ExactlyOneSelectedEsContract_IsPass()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());

                var provider = TestHost.Provider(true, "ES 12-26");
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.Equal(InstrumentSelectionStatus.Resolved, observation.SelectionStatus);
                Assert.Equal(1, observation.MatchingInstrumentCount);
                Assert.Equal(DiagnosticStatus.PASS, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_NoSelection_IsTargetNotConfigured_NeverPicksAnEsContract()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());

                // No explicit target: even though a single ES contract is loaded, the host must NOT
                // auto-select it. The identity stays unproven (INCONCLUSIVE, not configured).
                var provider = TestHost.Provider(true);
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.Equal(InstrumentSelectionStatus.NotConfigured, observation.SelectionStatus);
                var verdict = InstrumentIdentityVerifier.Evaluate(observation);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
                Assert.Contains(InstrumentIdentityReasonCodes.TargetNotConfigured, verdict.ReasonCodes);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_SelectedContractAbsentFromHost_IsTargetNotLoaded()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument("ES 03-27"));

                var provider = TestHost.Provider(true, "ES 12-26");
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.Equal(InstrumentSelectionStatus.NotLoaded, observation.SelectionStatus);
                var verdict = InstrumentIdentityVerifier.Evaluate(observation);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
                Assert.Contains(InstrumentIdentityReasonCodes.TargetNotLoaded, verdict.ReasonCodes);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_MultipleEsContracts_BareRootSelection_IsAmbiguous_NeverPass()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument("ES 12-26"));
                Instrument.All.Add(EsInstrument("ES 03-27"));

                // The bare root is insufficiently specific: two loaded contracts match, so the
                // target is ambiguous and never PASS.
                var provider = TestHost.Provider(true, "ES");
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.Equal(InstrumentSelectionStatus.MultipleCandidates, observation.SelectionStatus);
                Assert.Equal(2, observation.MatchingInstrumentCount);
                var verdict = InstrumentIdentityVerifier.Evaluate(observation);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
                Assert.Contains(InstrumentIdentityReasonCodes.TargetMultipleCandidates, verdict.ReasonCodes);
                Assert.NotEqual(DiagnosticStatus.PASS, verdict.Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_SelectedContractIdentityMismatch_IsFail()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                // The explicitly selected contract resolves, but its root is not ES: a proven wrong
                // instrument, so a definite FAIL (not "not loaded").
                Instrument.All.Add(EsInstrument("ES 12-26", "NQ"));

                var provider = TestHost.Provider(true, "ES 12-26");
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.Equal(InstrumentSelectionStatus.Resolved, observation.SelectionStatus);
                Assert.Equal(DiagnosticStatus.FAIL, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_SelectedContractVenueMismatch_IsFail()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                // The selected contract resolves with the ES root, but it is routed on a non-Globex
                // venue, so the ES/CME venue cannot be confirmed: a definite FAIL.
                Instrument.All.Add(EsInstrument("ES 12-26", "ES", Exchange.CBOT, new List<Exchange> { Exchange.CBOT }));

                var provider = TestHost.Provider(true, "ES 12-26");
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
        public void Host_MissingMasterInstrumentMetadata_IsInconclusive_NeverPass()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var instrument = EsInstrument();
                instrument.MasterInstrument = null;
                Instrument.All.Add(instrument);

                var provider = TestHost.Provider(true, "ES 12-26");
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.Equal(InstrumentSelectionStatus.Resolved, observation.SelectionStatus);
                Assert.False(observation.RootMetadataObservable);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Host_ReadinessNotReady_SelectionUnproven_IsInconclusive()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                Instrument.All.Add(EsInstrument());

                // The metadata gate is not ready, so the selected contract's metadata is unobservable.
                var provider = TestHost.Provider(false, "ES 12-26");
                var observation = provider.GetSnapshot().InstrumentIdentity;

                Assert.False(observation.RootMetadataObservable);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, InstrumentIdentityVerifier.Evaluate(observation).Status);
            }
            finally
            {
                Reset();
            }
        }

        // ---------------- one selected identity across metadata inspection AND market-data capture ----------------

        [Fact]
        public void Host_MetadataInspectionAndMarketDataCapture_UseTheSameSelectedInstrument()
        {
            Reset();
            try
            {
                Connection.Connections.Add(ConnectedRithmic());
                var front = EsInstrument("ES 12-26");
                var back = EsInstrument("ES 03-27");
                Instrument.All.Add(front);
                Instrument.All.Add(back);

                // Both components are given the SAME explicit selection.
                var provider = TestHost.Provider(true, "ES 03-27");
                var capture = new MarketDataCapture(r => true, () => true, "ES 03-27");

                Assert.True(capture.TrySubscribe());

                // The capture subscribed to the selected (back) contract, not the first loaded one.
                var captured = new List<NormalizedRecord>();
                var capturingCapture = new MarketDataCapture(
                    r => { captured.Add(r); return true; }, () => true, "ES 03-27");
                Assert.True(capturingCapture.TrySubscribe());

                back.MarketData.RaiseUpdate(new MarketDataEventArgs
                {
                    Instrument = back,
                    MarketDataType = MarketDataType.Last,
                    Price = 5100.0,
                    Time = new System.DateTime(2026, 11, 12, 8, 0, 0, System.DateTimeKind.Utc)
                });
                front.MarketData.RaiseUpdate(new MarketDataEventArgs
                {
                    Instrument = front,
                    MarketDataType = MarketDataType.Last,
                    Price = 5000.0,
                    Time = new System.DateTime(2026, 11, 12, 8, 0, 0, System.DateTimeKind.Utc)
                });

                // Only the selected contract's stream was captured.
                Assert.Single(captured);
                Assert.Equal("ES 03-27", captured.OfType<MarketDataRecord>().Single().InstrumentId);

                // The metadata inspection resolved the same selected contract.
                var observation = provider.GetSnapshot().InstrumentIdentity;
                Assert.Equal(InstrumentSelectionStatus.Resolved, observation.SelectionStatus);
                Assert.Equal(1, observation.MatchingInstrumentCount);

                capturingCapture.Detach();
                capture.Detach();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void MarketDataCapture_NoSelection_DoesNotSubscribe()
        {
            Reset();
            try
            {
                Instrument.All.Add(EsInstrument());

                // No explicit target: the capture must not auto-subscribe to the loaded ES contract.
                var capture = new MarketDataCapture(r => true, () => true);

                Assert.False(capture.TrySubscribe());
                Assert.False(capture.IsSubscribed);
            }
            finally
            {
                Reset();
            }
        }

        // ---------------- AddOn readiness: retry after the selected contract becomes available ----------------

        [Fact]
        public void AddOn_SelectedContractBecomesAvailableLater_BecomesReadyOnRetry()
        {
            Reset();
            try
            {
                var addOn = new DiagnosticAddOn { ReadinessRetryEnabled = false, RequestedInstrumentId = "ES 12-26" };
                addOn.ApplyLifecycleState("Active");

                // The selected contract is not loaded yet: readiness must not be ready.
                Assert.Equal(HostReadinessState.AwaitingInstrument, addOn.Readiness.State);

                // The host loads the selected contract after Active.
                Instrument.All.Add(EsInstrument("ES 12-26"));

                var readiness = addOn.PollReadinessOnce();
                Assert.True(readiness.InstrumentReady);
                Assert.True(readiness.FullyReady);

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void AddOn_OnlyADifferentContractLoaded_IsNotReady()
        {
            Reset();
            try
            {
                // A different ES contract is loaded, but not the selected one, so the tool must not
                // become ready on an arbitrary instrument.
                Instrument.All.Add(EsInstrument("ES 03-27"));

                var addOn = new DiagnosticAddOn { ReadinessRetryEnabled = false, RequestedInstrumentId = "ES 12-26" };
                addOn.ApplyLifecycleState("Active");

                Assert.False(addOn.Readiness.FullyReady);
                Assert.Equal(HostReadinessState.AwaitingInstrument, addOn.Readiness.State);

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void AddOn_NoSelection_NeverBecomesReady()
        {
            Reset();
            try
            {
                Instrument.All.Add(EsInstrument("ES 12-26"));

                var addOn = new DiagnosticAddOn { ReadinessRetryEnabled = false };
                addOn.ApplyLifecycleState("Active");

                Assert.False(addOn.Readiness.FullyReady);
                Assert.Equal(HostReadinessState.AwaitingInstrument, addOn.Readiness.State);

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }
    }
}
