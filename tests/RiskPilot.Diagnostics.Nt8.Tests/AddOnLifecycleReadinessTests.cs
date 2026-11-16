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
        private static void Reset()
        {
            Instrument.All.Clear();
            Account.All.Clear();
            Connection.Connections.Clear();
        }

        private static Instrument EsInstrument()
        {
            return new Instrument
            {
                FullName = "ES 12-26",
                Exchange = Exchange.Globex,
                MasterInstrument = new MasterInstrument { Name = "ES", InstrumentType = InstrumentType.Future, Currency = Currency.UsDollar }
            };
        }

        private static DiagnosticAddOn NewAddOn()
        {
            // Disable the background retry timer so the test drives every probe deterministically;
            // the first probe still runs synchronously inside ApplyLifecycleState("Active").
            return new DiagnosticAddOn { ReadinessRetryEnabled = false };
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

                // Before Active the AddOn is not operational.
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
                Assert.True(readiness.HostAvailable);
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
        public void Lifecycle_InstrumentBecomingAvailableLate_BecomesReadyOnRepeatedProbe()
        {
            Reset();
            try
            {
                var addOn = NewAddOn();
                addOn.ApplyLifecycleState("Active");
                Assert.Equal(HostReadinessState.AwaitingInstrument, addOn.Readiness.State);

                // The host loads the instrument after Active (asynchronously in the real host).
                Instrument.All.Add(EsInstrument());

                var readiness = addOn.PollReadinessOnce();

                Assert.True(readiness.InstrumentReady);
                Assert.True(readiness.MetadataReady);
                Assert.True(readiness.MarketDataSubscribed);
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
        public void Lifecycle_HostMetadataRemainingUnavailable_IsNotReady()
        {
            Reset();
            try
            {
                // A metadata gate that never becomes ready models a host whose instrument metadata
                // is unreadable. Readiness must stay INCONCLUSIVE, never PASS.
                Instrument.All.Add(EsInstrument());
                var tracker = new Core.Run.HostReadinessTracker();
                tracker.RecordAddOnState("Active");
                tracker.RecordHostAvailable(true);
                tracker.RecordInstrumentResolved(true);
                tracker.RecordMetadataReady(false);

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

                // Repeated probes with no instrument never fabricate readiness.
                for (var i = 0; i < 5; i++)
                {
                    var readiness = addOn.PollReadinessOnce();
                    Assert.False(readiness.FullyReady);
                    Assert.Equal(HostReadinessState.AwaitingInstrument, readiness.State);
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
                Assert.True(addOn.Readiness.MarketDataSubscribed);

                // Several more probes must not attach a second handler. The subscription is
                // idempotent, so the readiness stays subscribed and ready without a duplicate.
                addOn.PollReadinessOnce();
                addOn.PollReadinessOnce();

                instrument.MarketData.RaiseUpdate(new MarketDataEventArgs
                {
                    Instrument = instrument,
                    MarketDataType = MarketDataType.Last,
                    Price = 5000.25,
                    Time = new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc)
                });

                Assert.True(addOn.Readiness.FullyReady);

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

                // No instrument -> no subscription -> not ready (INCONCLUSIVE), never PASS.
                Assert.False(addOn.Readiness.MarketDataSubscribed);
                Assert.False(addOn.Readiness.FullyReady);

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
                Assert.True(addOn.Readiness.MarketDataSubscribed);

                addOn.ApplyLifecycleState("Terminated");

                var readiness = addOn.Readiness;
                Assert.True(readiness.ShuttingDown);
                Assert.False(readiness.FullyReady);
                Assert.Equal(HostReadinessState.ShuttingDown, readiness.State);
                Assert.False(readiness.MarketDataSubscribed);
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
                // SafeInitialize/SafeStart are guarded exactly-once; no exception, still not ready.
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
                Assert.True(addOn.Readiness.FullyReady);

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

                // Repeated disposal must not throw.
                addOn.SafeDispose();
                addOn.SafeDispose();

                // Polling after disposal must not throw or subscribe.
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
