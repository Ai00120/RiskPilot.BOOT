using System;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Unit tests for the host-free readiness model. Each readiness fact is independent and
    /// three-valued, so no fact is inferred from another: reaching <c>Active</c> never implies the
    /// metadata was read, a resolved instrument never implies market data arrived, a subscription
    /// never implies events, and an account in <c>Account.All</c> never implies verification.
    /// </summary>
    public class HostReadinessTests
    {
        private static HostReadinessFacts AllFacts()
        {
            return new HostReadinessFacts
            {
                AddOnState = "Active",
                HostAvailable = ReadinessFactState.Satisfied,
                MetadataReadAllowed = ReadinessFactState.Satisfied,
                InstrumentResolved = ReadinessFactState.Satisfied,
                MetadataObserved = ReadinessFactState.Satisfied,
                AccountVerified = ReadinessFactState.Satisfied,
                ConnectionVerified = ReadinessFactState.Satisfied,
                MarketDataSubscribed = ReadinessFactState.Satisfied,
                MarketDataObserved = ReadinessFactState.Satisfied
            };
        }

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
            Assert.False(AddOnLifecycle.IsOperational("SetDefaults"));
            Assert.False(AddOnLifecycle.IsOperational("Configure"));
            Assert.True(AddOnLifecycle.IsOperational("Active"));
            Assert.True(AddOnLifecycle.IsShutdown("Terminated"));
        }

        [Fact]
        public void Evaluator_ActiveAlone_IsNotFullyReady()
        {
            // The core regression: reaching Active must NOT be reported as ready, and must NOT imply
            // the metadata was read. Only the host object model being reachable is proven by Active.
            var observation = HostReadinessEvaluator.Evaluate(new HostReadinessFacts
            {
                AddOnState = "Active",
                HostAvailable = ReadinessFactState.Satisfied
            });

            Assert.True(observation.LifecycleOperational);
            Assert.False(observation.FullyReady);
            Assert.Equal(ReadinessFactState.Satisfied, observation.HostOperational);
            Assert.Equal(ReadinessFactState.Unknown, observation.MetadataObserved);
            Assert.Equal(HostReadinessState.AwaitingMetadataRead, observation.State);
            Assert.Contains(HostReadinessReasonCodes.MetadataReadNotAllowed, observation.ReasonCodes);
        }

        [Fact]
        public void Evaluator_ActiveLifecycleWithMissingMetadata_IsNotReady()
        {
            // Active lifecycle, host reachable, metadata read permitted, instrument resolved — but the
            // metadata was never observed. Readiness must stay AWAITING_METADATA, never READY.
            var facts = AllFacts();
            facts.MetadataObserved = ReadinessFactState.Unknown;

            var observation = HostReadinessEvaluator.Evaluate(facts);

            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.AwaitingMetadata, observation.State);
            Assert.Contains(HostReadinessReasonCodes.MetadataUnavailable, observation.ReasonCodes);
        }

        [Fact]
        public void Evaluator_MetadataReadAllowed_DoesNotImplyMetadataObserved()
        {
            // The precise defect: metadataReadAllowed was conflated with metadataReady. They are now
            // separate facts; permitted-but-unread must not be reported as observed.
            var observation = HostReadinessEvaluator.Evaluate(new HostReadinessFacts
            {
                AddOnState = "Active",
                HostAvailable = ReadinessFactState.Satisfied,
                MetadataReadAllowed = ReadinessFactState.Satisfied,
                InstrumentResolved = ReadinessFactState.Satisfied,
                MetadataObserved = ReadinessFactState.Unknown
            });

            Assert.Equal(ReadinessFactState.Satisfied, observation.MetadataReadAllowed);
            Assert.Equal(ReadinessFactState.Unknown, observation.MetadataObserved);
            Assert.False(observation.FullyReady);
        }

        [Fact]
        public void Evaluator_MetadataReadFails_IsNotReady()
        {
            // The read was permitted and attempted, but the required metadata could not be read.
            var facts = AllFacts();
            facts.MetadataObserved = ReadinessFactState.Unsatisfied;

            var observation = HostReadinessEvaluator.Evaluate(facts);

            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.AwaitingMetadata, observation.State);
        }

        [Fact]
        public void Evaluator_InstrumentResolvedButNoSubscription_IsNotReady()
        {
            var facts = AllFacts();
            facts.MarketDataSubscribed = ReadinessFactState.Unknown;
            facts.MarketDataObserved = ReadinessFactState.Unknown;

            var observation = HostReadinessEvaluator.Evaluate(facts);

            Assert.True(observation.InstrumentResolved.IsSatisfied());
            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.AwaitingSubscription, observation.State);
        }

        [Fact]
        public void Evaluator_SubscribedButNoMarketDataEvents_IsNotReady()
        {
            // A successful subscription must not imply data availability: marketDataObserved is a
            // separate fact.
            var facts = AllFacts();
            facts.MarketDataObserved = ReadinessFactState.Unknown;

            var observation = HostReadinessEvaluator.Evaluate(facts);

            Assert.True(observation.MarketDataSubscribed.IsSatisfied());
            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.AwaitingMarketData, observation.State);
            Assert.Contains(HostReadinessReasonCodes.MarketDataNotObserved, observation.ReasonCodes);
        }

        [Fact]
        public void Evaluator_MarketDataObservedButAccountUnverified_IsNotReady()
        {
            var facts = AllFacts();
            facts.AccountVerified = ReadinessFactState.Unknown;

            var observation = HostReadinessEvaluator.Evaluate(facts);

            Assert.True(observation.MarketDataObserved.IsSatisfied());
            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.AwaitingAccount, observation.State);
        }

        [Fact]
        public void Evaluator_AccountVerifiedButConnectionDisconnected_IsNotReady()
        {
            var facts = AllFacts();
            facts.ConnectionVerified = ReadinessFactState.Unsatisfied;

            var observation = HostReadinessEvaluator.Evaluate(facts);

            Assert.True(observation.AccountVerified.IsSatisfied());
            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.AwaitingConnection, observation.State);
            Assert.Contains(HostReadinessReasonCodes.ConnectionUnverified, observation.ReasonCodes);
        }

        [Fact]
        public void Evaluator_AllFacts_IsReady()
        {
            var observation = HostReadinessEvaluator.Evaluate(AllFacts());

            Assert.True(observation.FullyReady);
            Assert.Equal(HostReadinessState.Ready, observation.State);
            Assert.Contains(HostReadinessReasonCodes.Ready, observation.ReasonCodes);
        }

        [Fact]
        public void Evaluator_NotOperational_IsReportedBeforeHostFacts()
        {
            var facts = AllFacts();
            facts.AddOnState = "Configure";

            var observation = HostReadinessEvaluator.Evaluate(facts);

            Assert.False(observation.LifecycleOperational);
            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.NotOperational, observation.State);
        }

        [Fact]
        public void Evaluator_ShutdownOutranksEverything()
        {
            var facts = AllFacts();
            facts.AddOnState = "Terminated";

            var observation = HostReadinessEvaluator.Evaluate(facts);

            Assert.True(observation.ShuttingDown);
            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.ShuttingDown, observation.State);
        }

        [Fact]
        public void Evaluator_UnknownLifecycleState_IsNotOperational()
        {
            var observation = HostReadinessEvaluator.Evaluate(new HostReadinessFacts
            {
                AddOnState = string.Empty,
                HostAvailable = ReadinessFactState.Satisfied
            });

            Assert.Equal(ReadinessFactState.Unknown, observation.HostOperational);
            Assert.Equal(HostReadinessState.NotOperational, observation.State);
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
            tracker.RecordHostAvailable(ReadinessFactState.Satisfied);
            tracker.RecordMetadataReadAllowed(ReadinessFactState.Satisfied);

            var first = tracker.Observe();
            Assert.Equal(HostReadinessState.AwaitingInstrument, first.State);

            tracker.RecordInstrumentResolved(ReadinessFactState.Satisfied);
            tracker.RecordMetadataObserved(ReadinessFactState.Satisfied);
            tracker.RecordAccountVerified(ReadinessFactState.Satisfied);
            tracker.RecordConnectionVerified(ReadinessFactState.Satisfied);
            tracker.RecordMarketDataSubscribed(ReadinessFactState.Satisfied);
            tracker.RecordMarketDataObserved(ReadinessFactState.Satisfied);

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
                            case 0: tracker.RecordHostAvailable(ReadinessFactState.Satisfied); break;
                            case 1: tracker.RecordInstrumentResolved(ReadinessFactState.Satisfied); break;
                            case 2: tracker.RecordMetadataReadAllowed(ReadinessFactState.Satisfied); break;
                            default: tracker.RecordMarketDataSubscribed(ReadinessFactState.Satisfied); break;
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
            Assert.False(observation.FullyReady);
        }

        [Fact]
        public void Tracker_InvalidateStreamFacts_ClearsConnectionAndStreamFacts()
        {
            var tracker = new HostReadinessTracker();
            tracker.RecordAddOnState("Active");
            tracker.RecordHostAvailable(ReadinessFactState.Satisfied);
            tracker.RecordMetadataReadAllowed(ReadinessFactState.Satisfied);
            tracker.RecordInstrumentResolved(ReadinessFactState.Satisfied);
            tracker.RecordMetadataObserved(ReadinessFactState.Satisfied);
            tracker.RecordAccountVerified(ReadinessFactState.Satisfied);
            tracker.RecordConnectionVerified(ReadinessFactState.Satisfied);
            tracker.RecordMarketDataSubscribed(ReadinessFactState.Satisfied);
            tracker.RecordMarketDataObserved(ReadinessFactState.Satisfied);
            Assert.True(tracker.Observe().FullyReady);

            // A reconnect invalidates the connection- and stream-dependent facts, so readiness can
            // never remain satisfied on evidence that no longer holds.
            tracker.InvalidateStreamFacts();

            var observation = tracker.Observe();
            Assert.False(observation.FullyReady);
            Assert.Equal(ReadinessFactState.Unknown, observation.ConnectionVerified);
            Assert.Equal(ReadinessFactState.Unknown, observation.MarketDataSubscribed);
            Assert.Equal(ReadinessFactState.Unknown, observation.MarketDataObserved);
            // The lifecycle/metadata/instrument facts are unaffected.
            Assert.True(observation.MetadataObserved.IsSatisfied());
            Assert.True(observation.InstrumentResolved.IsSatisfied());
        }

        [Fact]
        public void Tracker_BeginEpoch_DiscardsStaleFacts()
        {
            var tracker = new HostReadinessTracker();
            tracker.RecordAddOnState("Active");
            tracker.RecordHostAvailable(ReadinessFactState.Satisfied);
            tracker.RecordMetadataReadAllowed(ReadinessFactState.Satisfied);
            tracker.RecordInstrumentResolved(ReadinessFactState.Satisfied);
            tracker.RecordMetadataObserved(ReadinessFactState.Satisfied);
            tracker.RecordAccountVerified(ReadinessFactState.Satisfied);
            tracker.RecordConnectionVerified(ReadinessFactState.Satisfied);
            tracker.RecordMarketDataSubscribed(ReadinessFactState.Satisfied);
            tracker.RecordMarketDataObserved(ReadinessFactState.Satisfied);
            Assert.True(tracker.Observe().FullyReady);

            // A restart begins a new epoch: no fact from the previous epoch may keep the tool ready.
            tracker.BeginEpoch();
            tracker.RecordAddOnState("Active");

            var observation = tracker.Observe();
            Assert.False(observation.FullyReady);
            Assert.Equal(HostReadinessState.AwaitingHost, observation.State);
            Assert.Equal(1, observation.Epoch);
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

        [Fact]
        public void Derivation_AccountVerified_IsUnknownWithNoSurface()
        {
            Assert.Equal(ReadinessFactState.Unknown, HostReadinessDerivation.AccountVerified(null));
            Assert.Equal(ReadinessFactState.Unknown,
                HostReadinessDerivation.AccountVerified(new AccountIdentityObservation()));
        }

        [Fact]
        public void Derivation_ConnectionVerified_IsUnsatisfiedWhenDisconnected()
        {
            var connection = new TargetAccountConnectionObservation
            {
                AccountSurfaceObservable = true,
                ObservedAccountCount = 1,
                MatchingAccountCount = 1
            };
            var account = new AccountIdentityObservation
            {
                AccountSurfaceObservable = true,
                ConnectionStatusObservable = true,
                ConnectionConnected = false
            };

            Assert.Equal(ReadinessFactState.Unsatisfied,
                HostReadinessDerivation.ConnectionVerified(connection, account));
        }
    }
}
