using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// M3 active-order counting tests. They assert the corrected contract: the active-order
    /// count is derived from real host order states, a terminal (completed / cancelled /
    /// rejected) order is never counted as active, and an unobservable order collection is
    /// reported INCONCLUSIVE rather than as a misleading zero. The host-adapter cases run over
    /// the OFFLINE NinjaTrader stub surface; a green stub run is NOT proof of real host behaviour.
    /// </summary>
    public class M3ActiveOrderTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

        private static ProbeContext Context(FakeDataProvider provider)
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            return new ProbeContext(provider, session, new FakeClock(Now), "corr00000000");
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

        private static DiagnosticResult ActiveCheck(OrderLifecycleObservation observation)
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { OrderLifecycle = observation }
            };

            return Find(new M3AccountOrderProbe().Evaluate(Context(provider)), "M3.ACTIVE_ORDERS");
        }

        // ---------------- Classifier: single source of truth ----------------

        [Theory]
        [InlineData(OrderLifecycleState.Initialized, OrderActivityClass.Active)]
        [InlineData(OrderLifecycleState.Submitted, OrderActivityClass.Active)]
        [InlineData(OrderLifecycleState.Accepted, OrderActivityClass.Active)]
        [InlineData(OrderLifecycleState.Working, OrderActivityClass.Active)]
        [InlineData(OrderLifecycleState.PartFilled, OrderActivityClass.Active)]
        [InlineData(OrderLifecycleState.ChangeSubmitted, OrderActivityClass.Active)]
        [InlineData(OrderLifecycleState.Filled, OrderActivityClass.Completed)]
        [InlineData(OrderLifecycleState.Cancelled, OrderActivityClass.Cancelled)]
        [InlineData(OrderLifecycleState.Rejected, OrderActivityClass.Rejected)]
        [InlineData(OrderLifecycleState.Unknown, OrderActivityClass.Unknown)]
        public void Classifier_MapsEveryState(OrderLifecycleState state, OrderActivityClass expected)
        {
            Assert.Equal(expected, OrderStateClassifier.Classify(state));
        }

        [Fact]
        public void Classifier_TerminalStatesAreNeverActive()
        {
            foreach (OrderLifecycleState state in Enum.GetValues(typeof(OrderLifecycleState)))
            {
                var isActive = OrderStateClassifier.IsActive(state);
                var isTerminal = OrderStateClassifier.IsTerminal(state);
                Assert.False(isActive && isTerminal, state + " cannot be both active and terminal");

                if (state == OrderLifecycleState.Filled
                    || state == OrderLifecycleState.Cancelled
                    || state == OrderLifecycleState.Rejected)
                {
                    Assert.True(isTerminal, state + " must be terminal");
                    Assert.False(isActive, state + " must not be active");
                }
            }
        }

        // ---------------- Probe: no orders ----------------

        [Fact]
        public void NoOrders_ReadableEmptyCollection_IsZeroNotInconclusive()
        {
            var result = ActiveCheck(new OrderLifecycleObservation
            {
                OrderCollectionObservable = true,
                AccountCount = 1,
                OrderSnapshotCount = 0
            });

            Assert.Equal(DiagnosticStatus.PASS, result.Status);
            Assert.Contains("ACTIVE=0", result.ReasonCodes);
            Assert.Contains("activeOrders=0", result.Observation);
        }

        // ---------------- Probe: one active order ----------------

        [Fact]
        public void OneActiveOrder_ReportsOneActive()
        {
            var result = ActiveCheck(new OrderLifecycleObservation
            {
                OrderCollectionObservable = true,
                AccountCount = 1,
                OrderSnapshotCount = 1,
                ActiveOrderCount = 1
            });

            Assert.Equal(DiagnosticStatus.PASS, result.Status);
            Assert.Contains("ACTIVE=1", result.ReasonCodes);
        }

        // ---------------- Probe: multiple active orders ----------------

        [Fact]
        public void MultipleActiveOrders_ReportsExactCount()
        {
            var result = ActiveCheck(new OrderLifecycleObservation
            {
                OrderCollectionObservable = true,
                AccountCount = 2,
                OrderSnapshotCount = 4,
                ActiveOrderCount = 4
            });

            Assert.Equal(DiagnosticStatus.PASS, result.Status);
            Assert.Contains("ACTIVE=4", result.ReasonCodes);
        }

        // ---------------- Probe: completed orders are not active ----------------

        [Fact]
        public void CompletedOrders_AreNotCountedAsActive()
        {
            var result = ActiveCheck(new OrderLifecycleObservation
            {
                OrderCollectionObservable = true,
                AccountCount = 1,
                OrderSnapshotCount = 3,
                ActiveOrderCount = 0,
                CompletedOrderCount = 3
            });

            Assert.Equal(DiagnosticStatus.PASS, result.Status);
            Assert.Contains("ACTIVE=0", result.ReasonCodes);
            Assert.Contains("COMPLETED=3", result.ReasonCodes);
        }

        // ---------------- Probe: cancelled orders are not active ----------------

        [Fact]
        public void CancelledOrders_AreNotCountedAsActive()
        {
            var result = ActiveCheck(new OrderLifecycleObservation
            {
                OrderCollectionObservable = true,
                AccountCount = 1,
                OrderSnapshotCount = 2,
                ActiveOrderCount = 0,
                CancelledOrderCount = 2
            });

            Assert.Equal(DiagnosticStatus.PASS, result.Status);
            Assert.Contains("ACTIVE=0", result.ReasonCodes);
            Assert.Contains("CANCELLED=2", result.ReasonCodes);
        }

        // ---------------- Probe: rejected orders are not active ----------------

        [Fact]
        public void RejectedOrders_AreNotCountedAsActive()
        {
            var result = ActiveCheck(new OrderLifecycleObservation
            {
                OrderCollectionObservable = true,
                AccountCount = 1,
                OrderSnapshotCount = 1,
                ActiveOrderCount = 0,
                RejectedOrderCount = 1
            });

            Assert.Equal(DiagnosticStatus.PASS, result.Status);
            Assert.Contains("ACTIVE=0", result.ReasonCodes);
            Assert.Contains("REJECTED=1", result.ReasonCodes);
        }

        // ---------------- Probe: mixed order states ----------------

        [Fact]
        public void MixedOrderStates_ActiveIsExactAndTerminalIsSeparate()
        {
            var result = ActiveCheck(new OrderLifecycleObservation
            {
                OrderCollectionObservable = true,
                AccountCount = 1,
                OrderSnapshotCount = 6,
                ActiveOrderCount = 2,
                CompletedOrderCount = 1,
                CancelledOrderCount = 2,
                RejectedOrderCount = 1
            });

            Assert.Equal(DiagnosticStatus.PASS, result.Status);
            Assert.Contains("ACTIVE=2", result.ReasonCodes);
            Assert.Contains("COMPLETED=1", result.ReasonCodes);
            Assert.Contains("CANCELLED=2", result.ReasonCodes);
            Assert.Contains("REJECTED=1", result.ReasonCodes);
            Assert.Contains("TERMINAL=4", result.ReasonCodes);
        }

        // ---------------- Probe: unavailable order data ----------------

        [Fact]
        public void UnavailableOrderData_IsInconclusiveNotZero()
        {
            var result = ActiveCheck(new OrderLifecycleObservation
            {
                OrderCollectionObservable = false,
                AccountCount = 1
            });

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, result.Status);
            Assert.Contains("ORDER_COLLECTION_UNOBSERVABLE", result.ReasonCodes);
        }

        [Fact]
        public void UnreadableOrderCollection_IsInconclusiveNotZero()
        {
            var result = ActiveCheck(new OrderLifecycleObservation
            {
                OrderCollectionObservable = true,
                AccountCount = 1,
                OrderSnapshotCount = 1,
                ActiveOrderCount = 1,
                OrderReadFailureCount = 1
            });

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, result.Status);
            Assert.Contains("ACTIVE_ORDER_COUNT_UNRELIABLE", result.ReasonCodes);
        }

        [Fact]
        public void UnclassifiedOrderState_IsInconclusiveNotZero()
        {
            var result = ActiveCheck(new OrderLifecycleObservation
            {
                OrderCollectionObservable = true,
                AccountCount = 1,
                OrderSnapshotCount = 1,
                UnclassifiedOrderCount = 1
            });

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, result.Status);
            Assert.Contains("ACTIVE_ORDER_COUNT_UNRELIABLE", result.ReasonCodes);
        }

        // ---------------- Consumer + assembler: ingested orders drive the count ----------------

        [Fact]
        public void Consumer_CountsActivityClassesFromIngestedRecords()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M3OrderLifecycleConsumer(session);

            consumer.Consume(new OrderRecord { OrderRef = "a", InstrumentId = "ES", State = OrderLifecycleState.Working });
            consumer.Consume(new OrderRecord { OrderRef = "b", InstrumentId = "ES", State = OrderLifecycleState.PartFilled });
            consumer.Consume(new OrderRecord { OrderRef = "c", InstrumentId = "ES", State = OrderLifecycleState.Filled });
            consumer.Consume(new OrderRecord { OrderRef = "d", InstrumentId = "ES", State = OrderLifecycleState.Cancelled });
            consumer.Consume(new OrderRecord { OrderRef = "e", InstrumentId = "ES", State = OrderLifecycleState.Rejected });
            consumer.Consume(new OrderRecord { OrderRef = "f", InstrumentId = "ES", State = OrderLifecycleState.Unknown });

            Assert.Equal(2, session.Counters.Get(RuntimeCounterKeys.OrderActive));
            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.OrderCompleted));
            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.OrderCancelled));
            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.OrderRejected));
            Assert.Equal(1, session.Counters.Get(RuntimeCounterKeys.OrderUnclassified));
        }

        [Fact]
        public void Assembler_FoldsActivityCountersIntoObservation()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            session.Counters.Increment(RuntimeCounterKeys.OrderActive, 2);
            session.Counters.Increment(RuntimeCounterKeys.OrderCompleted, 3);
            session.Counters.Increment(RuntimeCounterKeys.OrderCancelled, 1);
            session.Counters.Increment(RuntimeCounterKeys.OrderRejected, 1);

            var merged = SnapshotAssembler.Assemble(
                new DiagnosticSnapshot { OrderLifecycle = new OrderLifecycleObservation() },
                session);

            Assert.Equal(2, merged.OrderLifecycle.ActiveOrderCount);
            Assert.Equal(3, merged.OrderLifecycle.CompletedOrderCount);
            Assert.Equal(1, merged.OrderLifecycle.CancelledOrderCount);
            Assert.Equal(1, merged.OrderLifecycle.RejectedOrderCount);
        }

        [Fact]
        public void EndToEnd_IngestedOrders_DrivePassingActiveCheck()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M3OrderLifecycleConsumer(session);
            consumer.Consume(new OrderRecord { OrderRef = "a", InstrumentId = "ES", State = OrderLifecycleState.Working });
            consumer.Consume(new OrderRecord { OrderRef = "b", InstrumentId = "ES", State = OrderLifecycleState.Filled });

            var host = new DiagnosticSnapshot
            {
                OrderLifecycle = new OrderLifecycleObservation { OrderCollectionObservable = true, AccountCount = 1 }
            };
            var merged = SnapshotAssembler.Assemble(host, session);

            var provider = new FakeDataProvider { Snapshot = merged };
            var result = Find(new M3AccountOrderProbe().Evaluate(Context(provider)), "M3.ACTIVE_ORDERS");

            Assert.Equal(DiagnosticStatus.PASS, result.Status);
            Assert.Contains("ACTIVE=1", result.ReasonCodes);
            Assert.Contains("COMPLETED=1", result.ReasonCodes);
        }

        // ---------------- Host adapter over the offline stub surface ----------------

        [Collection("StubHostState")]
        public class HostAdapter
        {
            private static void Reset()
            {
                Account.All.Clear();
                Connection.Connections.Clear();
            }

            private static Account AccountWithOrders(params OrderState[] states)
            {
                var account = new Account
                {
                    Name = "SimTest",
                    Provider = Provider.Rithmic,
                    Denomination = Currency.UsDollar
                };

                var index = 0;
                foreach (var state in states)
                {
                    account.Orders.Add(new Order
                    {
                        OrderId = "order-" + index++,
                        OrderState = state,
                        Quantity = 1
                    });
                }

                return account;
            }

            [Fact]
            public void Host_NoOrders_ReportsZeroActiveWithObservableCollection()
            {
                Reset();
                try
                {
                    Account.All.Add(AccountWithOrders());

                    var observation = new Nt8HostDataProvider(masterMetadataReady: false).GetSnapshot().OrderLifecycle;

                    Assert.True(observation.OrderCollectionObservable);
                    Assert.Equal(0, observation.ActiveOrderCount);
                    Assert.Equal(0, observation.OrderSnapshotCount);
                    Assert.Equal(0, observation.OrderReadFailureCount);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void Host_OneActiveOrder_ReportsOneActive()
            {
                Reset();
                try
                {
                    Account.All.Add(AccountWithOrders(OrderState.Working));

                    var observation = new Nt8HostDataProvider(masterMetadataReady: false).GetSnapshot().OrderLifecycle;

                    Assert.True(observation.OrderCollectionObservable);
                    Assert.Equal(1, observation.ActiveOrderCount);
                    Assert.Equal(0, observation.CompletedOrderCount);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void Host_MultipleActiveOrders_ReportsExactCount()
            {
                Reset();
                try
                {
                    Account.All.Add(AccountWithOrders(
                        OrderState.Working,
                        OrderState.Accepted,
                        OrderState.PartFilled,
                        OrderState.Submitted));

                    var observation = new Nt8HostDataProvider(masterMetadataReady: false).GetSnapshot().OrderLifecycle;

                    Assert.Equal(4, observation.ActiveOrderCount);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void Host_TerminalOrders_AreNeverCountedAsActive()
            {
                Reset();
                try
                {
                    Account.All.Add(AccountWithOrders(
                        OrderState.Filled,
                        OrderState.Cancelled,
                        OrderState.Rejected));

                    var observation = new Nt8HostDataProvider(masterMetadataReady: false).GetSnapshot().OrderLifecycle;

                    Assert.Equal(0, observation.ActiveOrderCount);
                    Assert.Equal(1, observation.CompletedOrderCount);
                    Assert.Equal(1, observation.CancelledOrderCount);
                    Assert.Equal(1, observation.RejectedOrderCount);
                    Assert.Equal(3, observation.TerminalStateRetainedCount);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void Host_MixedOrderStates_ActiveIsExact()
            {
                Reset();
                try
                {
                    Account.All.Add(AccountWithOrders(
                        OrderState.Working,
                        OrderState.PartFilled,
                        OrderState.Filled,
                        OrderState.Cancelled,
                        OrderState.Rejected));

                    var observation = new Nt8HostDataProvider(masterMetadataReady: false).GetSnapshot().OrderLifecycle;

                    Assert.Equal(2, observation.ActiveOrderCount);
                    Assert.Equal(1, observation.CompletedOrderCount);
                    Assert.Equal(1, observation.CancelledOrderCount);
                    Assert.Equal(1, observation.RejectedOrderCount);
                    Assert.Equal(0, observation.UnclassifiedOrderCount);
                    Assert.Equal(5, observation.OrderSnapshotCount);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void Host_UnreadableOrderEntry_IsRecordedAsFailureAndInconclusive()
            {
                Reset();
                try
                {
                    var account = AccountWithOrders(OrderState.Working);
                    account.Orders.Add(null);
                    Account.All.Add(account);

                    var provider = new Nt8HostDataProvider(masterMetadataReady: false);
                    var observation = provider.GetSnapshot().OrderLifecycle;

                    Assert.Equal(1, observation.OrderReadFailureCount);

                    var provider2 = new FakeDataProvider
                    {
                        Snapshot = new DiagnosticSnapshot { OrderLifecycle = observation }
                    };
                    var result = Find(new M3AccountOrderProbe().Evaluate(Context(provider2)), "M3.ACTIVE_ORDERS");

                    Assert.Equal(DiagnosticStatus.INCONCLUSIVE, result.Status);
                    Assert.Contains("ACTIVE_ORDER_COUNT_UNRELIABLE", result.ReasonCodes);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void Host_GetAccounts_WorkingOrderCount_ReflectsActiveOrders()
            {
                Reset();
                try
                {
                    Account.All.Add(AccountWithOrders(
                        OrderState.Working,
                        OrderState.PartFilled,
                        OrderState.Filled,
                        OrderState.Cancelled));

                    var accounts = new Nt8HostDataProvider(masterMetadataReady: false).GetAccounts();

                    Assert.Single(accounts);
                    Assert.Equal(2, accounts[0].WorkingOrderCount);
                }
                finally
                {
                    Reset();
                }
            }

            [Fact]
            public void Host_GetAccounts_NoOrders_WorkingOrderCountIsZero()
            {
                Reset();
                try
                {
                    Account.All.Add(AccountWithOrders());

                    var accounts = new Nt8HostDataProvider(masterMetadataReady: false).GetAccounts();

                    Assert.Single(accounts);
                    Assert.Equal(0, accounts[0].WorkingOrderCount);
                }
                finally
                {
                    Reset();
                }
            }
        }
    }
}
