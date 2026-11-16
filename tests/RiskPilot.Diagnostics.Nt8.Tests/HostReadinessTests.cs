using System;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Unit tests for the host-free readiness model that replaces the single, permanently-false
    /// <c>HostIsLive</c> Boolean. An AddOn uses the NinjaTrader "Active state" system and never
    /// reaches <c>DataLoaded</c>/<c>Historical</c>/<c>Transition</c>/<c>Realtime</c>, so readiness
    /// must key on <c>Active</c> and report the five concepts separately.
    /// </summary>
    public class HostReadinessTests
    {
        [Theory]
        [InlineData("Active", true)]
        [InlineData("DataLoaded", true)]
        [InlineData("Historical", true)]
        [InlineData("Transition", true)]
        [InlineData("Realtime", true)]
        [InlineData("SetDefaults", false)]
        [InlineData("Configure", false)]
        [InlineData("Terminated", false)]
        [InlineData("Unknown", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void AddOnLifecycle_IsOperational_AcceptsActiveAndDataStates(string state, bool expected)
        {
            Assert.Equal(expected, AddOnLifecycle.IsOperational(state));
        }

        [Fact]
        public void AddOnLifecycle_ClassifiesTheRealAddOnSequence()
        {
            // The documented AddOn sequence: SetDefaults -> Configure -> Active -> Terminated.
            Assert.False(AddOnLifecycle.IsOperational("SetDefaults"));
            Assert.False(AddOnLifecycle.IsOperational("Configure"));
            Assert.True(AddOnLifecycle.IsOperational("Active"));
            Assert.True(AddOnLifecycle.IsShutdown("Terminated"));
        }

        [Fact]
        public void Evaluator_ActiveAlone_IsNotFullyReady()
        {
            // The core regression: reaching Active must NOT be reported as ready. Only the host
            // object model being reachable is proven by Active.
            var observation = HostReadinessEvaluator.Evaluate(new HostReadinessFacts { AddOnState = "Active", HostAvailable = true });

            Assert.True(observation.LifecycleOperational);
            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.AwaitingInstrument, observation.State);
            Assert.Contains(HostReadinessReasonCodes.InstrumentUnavailable, observation.ReasonCodes);
        }

        [Fact]
        public void Evaluator_NotOperational_IsReportedBeforeHostFacts()
        {
            var observation = HostReadinessEvaluator.Evaluate(new HostReadinessFacts
            {
                AddOnState = "Configure",
                HostAvailable = true,
                InstrumentResolved = true,
                MetadataReady = true,
                MarketDataSubscribed = true
            });

            Assert.False(observation.LifecycleOperational);
            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.NotOperational, observation.State);
        }

        [Fact]
        public void Evaluator_ShutdownOutranksEverything()
        {
            var observation = HostReadinessEvaluator.Evaluate(new HostReadinessFacts
            {
                AddOnState = "Terminated",
                HostAvailable = true,
                InstrumentResolved = true,
                MetadataReady = true,
                MarketDataSubscribed = true
            });

            Assert.True(observation.ShuttingDown);
            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.ShuttingDown, observation.State);
        }

        [Fact]
        public void Evaluator_AllFiveFacts_IsReady()
        {
            var observation = HostReadinessEvaluator.Evaluate(new HostReadinessFacts
            {
                AddOnState = "Active",
                HostAvailable = true,
                InstrumentResolved = true,
                MetadataReady = true,
                MarketDataSubscribed = true
            });

            Assert.True(observation.FullyReady);
            Assert.Equal(HostReadinessState.Ready, observation.State);
            Assert.Contains(HostReadinessReasonCodes.Ready, observation.ReasonCodes);
        }

        [Theory]
        [InlineData(false, true, true, true, HostReadinessState.AwaitingHost)]
        [InlineData(true, false, true, true, HostReadinessState.AwaitingInstrument)]
        [InlineData(true, true, false, true, HostReadinessState.AwaitingMetadata)]
        [InlineData(true, true, true, false, HostReadinessState.AwaitingSubscription)]
        public void Evaluator_ReportsTheExactOutstandingFact(
            bool host, bool instrument, bool metadata, bool subscribed, string expectedState)
        {
            var observation = HostReadinessEvaluator.Evaluate(new HostReadinessFacts
            {
                AddOnState = "Active",
                HostAvailable = host,
                InstrumentResolved = instrument,
                MetadataReady = metadata,
                MarketDataSubscribed = subscribed
            });

            Assert.False(observation.FullyReady);
            Assert.Equal(expectedState, observation.State);
        }

        [Fact]
        public void Evaluator_NotObserved_IsNeverReady()
        {
            var observation = HostReadinessEvaluator.NotObserved();
            Assert.False(observation.SurfaceObservable);
            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.NotObserved, observation.State);
        }

        [Fact]
        public void Tracker_RecordsEachFactIndependently()
        {
            var tracker = new HostReadinessTracker();
            tracker.RecordAddOnState("Active");
            tracker.RecordHostAvailable(true);

            var first = tracker.Observe();
            Assert.Equal(HostReadinessState.AwaitingInstrument, first.State);

            tracker.RecordInstrumentResolved(true);
            tracker.RecordMetadataReady(true);
            tracker.RecordMarketDataSubscribed(true);

            var ready = tracker.Observe();
            Assert.True(ready.FullyReady);
            Assert.Equal(HostReadinessState.Ready, ready.State);
        }

        [Fact]
        public void Tracker_ConcurrentUpdates_ProduceConsistentObservation()
        {
            var tracker = new HostReadinessTracker();
            tracker.RecordAddOnState("Active");

            using var start = new ManualResetEventSlim(false);
            var threads = new Thread[4];
            for (var i = 0; i < threads.Length; i++)
            {
                var index = i;
                threads[i] = new Thread(() =>
                {
                    start.Wait();
                    for (var j = 0; j < 2000; j++)
                    {
                        switch (index)
                        {
                            case 0: tracker.RecordHostAvailable(true); break;
                            case 1: tracker.RecordInstrumentResolved(true); break;
                            case 2: tracker.RecordMetadataReady(true); break;
                            default: tracker.RecordMarketDataSubscribed(true); break;
                        }
                    }
                });
                threads[i].Start();
            }

            start.Set();
            foreach (var thread in threads)
            {
                thread.Join();
            }

            var observation = tracker.Observe();
            Assert.True(observation.FullyReady);
            Assert.Equal(HostReadinessState.Ready, observation.State);
        }

        [Fact]
        public void RetryPolicy_BackoffGrowsAndIsBounded()
        {
            var policy = new HostReadinessRetryPolicy(5, 100, 800);

            Assert.Equal(100, policy.NextDelayMs(0));
            Assert.Equal(200, policy.NextDelayMs(1));
            Assert.Equal(400, policy.NextDelayMs(2));
            Assert.Equal(800, policy.NextDelayMs(3));
            Assert.Equal(800, policy.NextDelayMs(100));
        }

        [Fact]
        public void RetryPolicy_Terminates()
        {
            var policy = new HostReadinessRetryPolicy(3, 1, 2);
            Assert.True(policy.ShouldRetry(0));
            Assert.True(policy.ShouldRetry(2));
            Assert.False(policy.ShouldRetry(3));
        }

        [Fact]
        public void RetryPolicy_RejectsNonPositiveInputs()
        {
            var policy = new HostReadinessRetryPolicy(0, 0, -5);
            Assert.Equal(1, policy.MaxAttempts);
            Assert.Equal(1, policy.InitialDelayMs);
            Assert.True(policy.MaxDelayMs >= policy.InitialDelayMs);
        }
    }
}
