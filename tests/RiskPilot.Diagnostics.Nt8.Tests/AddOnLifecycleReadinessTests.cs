using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Host;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Drives the real <see cref="DiagnosticAddOn"/> lifecycle and readiness model over the OFFLINE
    /// NinjaTrader stub surface. These tests exercise the production readiness gate end-to-end (the
    /// AddOn reaction, the readiness tracker, the market-data capture and the provider probe) so a
    /// regression in the AddOn readiness fix is caught offline. This is NOT proof of real host
    /// behaviour: the real <c>OnStateChange</c> timing and host object model are only provable inside
    /// NinjaTrader 8.
    /// </summary>
    [Collection("StubHostState")]
    public class AddOnLifecycleReadinessTests
    {
        private const string ExpectedAccount = "SimRP.BOT";
        private const string ExpectedConnection = "Rithmic";

        private static void Reset()
        {
            Instrument.All.Clear();
            Account.All.Clear();
            Connection.Connections.Clear();
        }

        private static Instrument EsInstrument(bool faultMetadata = false)
        {
            return new Instrument
            {
                FullName = "ES 12-26",
                Exchange = Exchange.Globex,
                MasterInstrument = new MasterInstrument
                {
                    Name = faultMetadata ? null : "ES",
                    InstrumentType = InstrumentType.Future,
                    Currency = Currency.UsDollar,
                    PointValue = 50d,
                    TickSize = 0.25d,
                    ThrowOnNameRead = faultMetadata
                }
            };
        }

        /// <summary>
        /// A verified target account bound to a connected required connection. The verified identity
        /// and association are what make the accountVerified/connectionVerified facts satisfiable.
        /// </summary>
        private static Account TargetAccount(bool connected = true)
        {
            var connection = new Connection
            {
                Status = connected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected,
                PriceStatus = connected ? ConnectionStatus.Connected : ConnectionStatus.Disconnected,
                Options = new ConnectOptions
                {
                    Name = ExpectedConnection,
                    Provider = Provider.Provider14,
                    Mode = Mode.Simulation
                }
            };
            Connection.Connections.Add(connection);

            return new Account
            {
                Name = ExpectedAccount,
                Provider = Provider.Provider14,
                Denomination = Currency.UsDollar,
                Connection = connection
            };
        }

        private static DiagnosticAddOn NewAddOn()
        {
            // Disable the background retry timer so the test drives every probe deterministically;
            // the first probe still runs synchronously inside ApplyLifecycleState("Active").
            // The target instrument is set explicitly (the only supported selection mechanism).
            // The reviewed owner-authorized slot mapping mirrors production's compiled D-097
            // Provider14 -> Rithmic mapping; the AddOn also exposes the fail-closed empty mapping.
            return new DiagnosticAddOn
            {
                ReadinessRetryEnabled = false,
                RequestedInstrumentId = "ES 12-26",
                OwnerAuthorizedProviderSlots = TestHost.RithmicSlotMapping
            };
        }

        private static void RaiseLastTrade(Instrument instrument)
        {
            instrument.MarketData.RaiseUpdate(new MarketDataEventArgs
            {
                Instrument = instrument,
                MarketDataType = MarketDataType.Last,
                Price = 5000.25,
                Time = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc)
            });
        }

        [Fact]
        public void Lifecycle_ReachingActive_MakesHostOperational()
        {
            Reset();
            try
            {
                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("SetDefaults");
                addOn.ApplyLifecycleState("Configure");

                Assert.Equal(HostReadinessState.NotOperational, addOn.Readiness.State);

                addOn.ApplyLifecycleState("Active");

                Assert.True(addOn.Readiness.LifecycleOperational);
                Assert.Equal("Active", addOn.Readiness.AddOnState);
                Assert.Contains("SetDefaults", addOn.LifecycleStates);
                Assert.Contains("Configure", addOn.LifecycleStates);
                Assert.Contains("Active", addOn.LifecycleStates);

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_ActiveButNoInstrument_IsAwaitingInstrument_NotReady()
        {
            Reset();
            try
            {
                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");

                // The core regression guard: reaching Active alone is never "ready".
                var readiness = addOn.Readiness;
                Assert.True(readiness.HostAvailable.IsSatisfied());
                Assert.True(readiness.MetadataReadAllowed.IsSatisfied());
                Assert.False(readiness.InstrumentResolved.IsSatisfied());
                Assert.False(readiness.FullyReady);
                Assert.Equal(HostReadinessState.AwaitingInstrument, readiness.State);

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_ActiveWithNoMetadataReadAllowed_StaysAwaitingMetadataRead()
        {
            Reset();
            try
            {
                // The AddOn's metadata-read gate is the operational lifecycle predicate, which is
                // satisfied at Active. Drive the tracker directly to prove the two facts are distinct:
                // host operational + host reachable, but metadataReadAllowed unknown.
                var tracker = new Core.Run.HostReadinessTracker();
                tracker.RecordAddOnState("Active");
                tracker.RecordHostAvailable(ReadinessFactState.Satisfied);

                var observation = tracker.Observe();

                Assert.False(observation.FullyReady);
                Assert.Equal(HostReadinessState.AwaitingMetadataRead, observation.State);
                Assert.Equal(ReadinessFactState.Unknown, observation.MetadataObserved);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_InstrumentBecomingAvailableLate_ObservesMetadataButNotReadyUntilData()
        {
            Reset();
            try
            {
                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");

                // The host loads the instrument after Active (asynchronously in the real host).
                Instrument.All.Add(EsInstrument());

                var readiness = addOn.PollReadinessOnce();

                Assert.True(readiness.InstrumentResolved.IsSatisfied());
                Assert.True(readiness.MetadataReadAllowed.IsSatisfied());
                Assert.True(readiness.MetadataObserved.IsSatisfied());
                Assert.True(readiness.MarketDataSubscribed.IsSatisfied());
                // No account/connection and no market data yet, so still not ready.
                Assert.False(readiness.FullyReady);

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_MetadataReadFails_IsNotReady()
        {
            Reset();
            try
            {
                // The instrument resolves but its metadata getter throws, so metadataObserved must
                // stay unsatisfied even though the read was permitted.
                Instrument.All.Add(EsInstrument(faultMetadata: true));

                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");

                var readiness = addOn.Readiness;
                Assert.True(readiness.InstrumentResolved.IsSatisfied());
                Assert.False(readiness.MetadataObserved.IsSatisfied());
                Assert.False(readiness.FullyReady);
                Assert.Equal(HostReadinessState.AwaitingMetadata, readiness.State);

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_HostMetadataRemainingUnavailable_IsNotReady()
        {
            Reset();
            try
            {
                Instrument.All.Add(EsInstrument());
                var tracker = new Core.Run.HostReadinessTracker();
                tracker.RecordAddOnState("Active");
                tracker.RecordHostAvailable(ReadinessFactState.Satisfied);
                tracker.RecordMetadataReadAllowed(ReadinessFactState.Satisfied);
                tracker.RecordInstrumentResolved(ReadinessFactState.Satisfied);
                tracker.RecordMetadataObserved(ReadinessFactState.Unsatisfied);

                var observation = tracker.Observe();
                Assert.False(observation.FullyReady);
                Assert.Equal(HostReadinessState.AwaitingMetadata, observation.State);
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_NoInstrumentEverLoaded_StaysInconclusive()
        {
            Reset();
            try
            {
                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");

                for (var i = 0; i < 5; i++)
                {
                    var readiness = addOn.PollReadinessOnce();
                    Assert.False(readiness.FullyReady);
                }

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void MarketDataSubscription_RepeatedProbes_DoNotDuplicateSubscription()
        {
            Reset();
            try
            {
                var instrument = EsInstrument();
                Instrument.All.Add(instrument);

                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");
                Assert.True(addOn.Readiness.MarketDataSubscribed.IsSatisfied());

                addOn.PollReadinessOnce();
                addOn.PollReadinessOnce();

                RaiseLastTrade(instrument);

                Assert.True(addOn.Readiness.MarketDataObserved.IsSatisfied());

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void MarketDataSubscription_FailsClosed_WhenInstrumentHasNoStreams()
        {
            Reset();
            try
            {
                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");

                Assert.False(addOn.Readiness.MarketDataSubscribed.IsSatisfied());
                Assert.False(addOn.Readiness.FullyReady);

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_FullyVerifiedHostWithMarketData_IsReady()
        {
            Reset();
            try
            {
                var instrument = EsInstrument();
                Instrument.All.Add(instrument);
                Account.All.Add(TargetAccount());

                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");
                addOn.PollReadinessOnce();

                // The subscription is established, but no event has been observed yet.
                Assert.False(addOn.Readiness.FullyReady);
                Assert.Equal(HostReadinessState.AwaitingMarketData, addOn.Readiness.State);

                RaiseLastTrade(instrument);

                var readiness = addOn.Readiness;
                Assert.True(readiness.AccountVerified.IsSatisfied());
                Assert.True(readiness.ConnectionVerified.IsSatisfied());
                Assert.True(readiness.MarketDataObserved.IsSatisfied());
                Assert.True(readiness.FullyReady);
                Assert.Equal(HostReadinessState.Ready, readiness.State);

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_MarketDataObservedButAccountUnresolved_IsNotReady()
        {
            Reset();
            try
            {
                var instrument = EsInstrument();
                Instrument.All.Add(instrument);

                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");
                RaiseLastTrade(instrument);

                var readiness = addOn.Readiness;
                Assert.True(readiness.MarketDataObserved.IsSatisfied());
                Assert.False(readiness.AccountVerified.IsSatisfied());
                Assert.False(readiness.FullyReady);
                Assert.Equal(HostReadinessState.AwaitingAccount, readiness.State);

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_VerifiedAccountWithDisconnectedConnection_IsNotReady()
        {
            Reset();
            try
            {
                var instrument = EsInstrument();
                Instrument.All.Add(instrument);
                Account.All.Add(TargetAccount(connected: false));

                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");
                addOn.PollReadinessOnce();
                RaiseLastTrade(instrument);

                var readiness = addOn.Readiness;
                Assert.False(readiness.ConnectionVerified.IsSatisfied());
                Assert.False(readiness.FullyReady);

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_Terminated_DetachesAndReportsShutdown()
        {
            Reset();
            try
            {
                var instrument = EsInstrument();
                Instrument.All.Add(instrument);

                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");
                Assert.True(addOn.Readiness.MarketDataSubscribed.IsSatisfied());

                addOn.ApplyLifecycleState("Terminated");

                var readiness = addOn.Readiness;
                Assert.True(readiness.ShuttingDown);
                Assert.False(readiness.FullyReady);
                Assert.Equal(HostReadinessState.ShuttingDown, readiness.State);
                Assert.False(readiness.MarketDataSubscribed.IsSatisfied());
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_ShutdownAndRestart_DiscardsStaleFacts()
        {
            Reset();
            try
            {
                var instrument = EsInstrument();
                Instrument.All.Add(instrument);
                Account.All.Add(TargetAccount());

                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");
                addOn.PollReadinessOnce();
                RaiseLastTrade(instrument);
                Assert.True(addOn.Readiness.FullyReady);

                // Shut down, then restart. A restart must not inherit the previous epoch's readiness.
                addOn.ApplyLifecycleState("Terminated");
                addOn.ApplyLifecycleState("Active");

                var readiness = addOn.Readiness;
                Assert.False(readiness.FullyReady);
                // A same-instance restart begins a new epoch: the facts observed before termination
                // are discarded, so the restarted AddOn is never reported ready on stale evidence.
                Assert.True(readiness.Epoch >= 1);
                Assert.False(readiness.MarketDataObserved.IsSatisfied());
                Assert.False(readiness.MarketDataSubscribed.IsSatisfied());
                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_ReconnectInvalidatesReadiness()
        {
            Reset();
            try
            {
                var instrument = EsInstrument();
                Instrument.All.Add(instrument);
                var account = TargetAccount();
                Account.All.Add(account);

                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");
                addOn.PollReadinessOnce();
                RaiseLastTrade(instrument);
                Assert.True(addOn.Readiness.FullyReady);

                // The provider drops the connection, then the tool is told to invalidate the
                // stream-dependent facts. Readiness can never remain satisfied on a connection or a
                // subscription that no longer holds.
                account.Connection.Status = ConnectionStatus.Disconnected;
                account.Connection.PriceStatus = ConnectionStatus.Disconnected;
                addOn.InvalidateReadinessForReconnect();

                var readiness = addOn.Readiness;
                Assert.False(readiness.FullyReady);
                Assert.Equal(ReadinessFactState.Unsatisfied, readiness.ConnectionVerified);
                Assert.False(readiness.MarketDataObserved.IsSatisfied());

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Lifecycle_RepeatedActiveTransition_IsIdempotent()
        {
            Reset();
            try
            {
                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");
                addOn.ApplyLifecycleState("Active");
                addOn.ApplyLifecycleState("Active");

                Assert.True(addOn.Readiness.LifecycleOperational);
                Assert.False(addOn.Readiness.FullyReady);

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Readiness_ConcurrentProbes_AreThreadSafe()
        {
            Reset();
            try
            {
                var instrument = EsInstrument();
                Instrument.All.Add(instrument);

                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");

                var errors = new List<Exception>();
                var threads = new System.Threading.Thread[4];
                for (var i = 0; i < threads.Length; i++)
                {
                    threads[i] = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            for (var j = 0; j < 200; j++)
                            {
                                var readiness = addOn.Readiness;
                                Assert.NotNull(readiness);
                                addOn.PollReadinessOnce();
                            }
                        }
                        catch (Exception ex)
                        {
                            lock (errors) { errors.Add(ex); }
                        }
                    });
                    threads[i].Start();
                }

                foreach (var thread in threads)
                {
                    thread.Join();
                }

                Assert.Empty(errors);
                Assert.True(addOn.Readiness.MarketDataSubscribed.IsSatisfied());

                addOn.SafeDispose();
            }
            finally
            {
                Reset();
            }
        }

        [Fact]
        public void Disposal_AfterTerminate_IsSafeAndIdempotent()
        {
            Reset();
            try
            {
                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");
                addOn.ApplyLifecycleState("Terminated");

                addOn.SafeDispose();
                addOn.SafeDispose();

                var readiness = addOn.PollReadinessOnce();
                Assert.NotNull(readiness);
            }
            finally
            {
                Reset();
            }
        }
    }
}
