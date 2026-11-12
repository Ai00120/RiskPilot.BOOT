using System;
using System.Collections.Generic;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class EventConsumerTests
    {
        private sealed class RecordingConsumer : INormalizedEventConsumer
        {
            public readonly List<NormalizedRecord> Received = new List<NormalizedRecord>();

            public string Name { get { return "recording"; } }

            public void Consume(NormalizedRecord record)
            {
                lock (Received)
                {
                    Received.Add(record);
                }
            }
        }

        private sealed class ThrowingConsumer : INormalizedEventConsumer
        {
            public string Name { get { return "throwing"; } }

            public void Consume(NormalizedRecord record)
            {
                throw new InvalidOperationException("consumer failure");
            }
        }

        private static DiagnosticRuntime NewRuntime()
        {
            return new DiagnosticRuntime(new FakeDataProvider(), new FakeClock(new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc)), 128, 128);
        }

        [Fact]
        public void Dispatcher_DeliversToAllConsumers()
        {
            var dispatcher = new NormalizedEventDispatcher();
            var first = new RecordingConsumer();
            var second = new RecordingConsumer();
            dispatcher.Register(first);
            dispatcher.Register(second);

            var delivered = dispatcher.Dispatch(new MarketDataRecord { InstrumentId = "ES 12-26" });

            Assert.Equal(2, delivered);
            Assert.Single(first.Received);
            Assert.Single(second.Received);
            Assert.Equal(1, dispatcher.ConsumedCount);
        }

        [Fact]
        public void Dispatcher_ContainsConsumerException_AndStillDeliversToOthers()
        {
            var dispatcher = new NormalizedEventDispatcher();
            var good = new RecordingConsumer();
            dispatcher.Register(new ThrowingConsumer());
            dispatcher.Register(good);

            var delivered = dispatcher.Dispatch(new MarketDataRecord { InstrumentId = "ES 12-26" });

            Assert.Equal(1, delivered);
            Assert.Single(good.Received);
            Assert.Equal(1, dispatcher.ConsumerErrorCount);
        }

        [Fact]
        public void Dispatcher_IgnoresDuplicateRegistration()
        {
            var dispatcher = new NormalizedEventDispatcher();
            var consumer = new RecordingConsumer();
            dispatcher.Register(consumer);
            dispatcher.Register(consumer);

            dispatcher.Dispatch(new MarketDataRecord { InstrumentId = "ES 12-26" });

            Assert.Single(consumer.Received);
        }

        [Fact]
        public void Dispatcher_NullRecord_IsNoOp()
        {
            var dispatcher = new NormalizedEventDispatcher();
            var consumer = new RecordingConsumer();
            dispatcher.Register(consumer);

            Assert.Equal(0, dispatcher.Dispatch(null));
            Assert.Empty(consumer.Received);
        }

        [Fact]
        public void Runtime_RegisterBeforeInitialize_Throws()
        {
            using var runtime = NewRuntime();
            Assert.Throws<InvalidOperationException>(() => runtime.RegisterEventConsumer(new RecordingConsumer()));
        }

        [Fact]
        public void Runtime_ForwardsIngestedEventsToConsumer()
        {
            using var runtime = NewRuntime();
            runtime.Initialize();
            var consumer = new RecordingConsumer();
            runtime.RegisterEventConsumer(consumer);
            runtime.Start();

            for (var i = 0; i < 25; i++)
            {
                runtime.Ingest(new MarketDataRecord
                {
                    InstrumentId = "ES 12-26",
                    Kind = MarketDataKind.Trade,
                    Volume = i + 1,
                    SourceTimestampUtc = DateTime.UtcNow.AddTicks(i)
                });
            }

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (runtime.ConsumedEventCount < 25 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }

            Assert.Equal(25, runtime.ConsumedEventCount);
            Assert.Equal(25, consumer.Received.Count);
        }

        [Fact]
        public void Runtime_ConsumerFailure_IsContainedAndCounted()
        {
            using var runtime = NewRuntime();
            runtime.Initialize();
            runtime.RegisterEventConsumer(new ThrowingConsumer());
            runtime.Start();

            runtime.Ingest(new MarketDataRecord { InstrumentId = "ES 12-26", SourceTimestampUtc = DateTime.UtcNow });

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (runtime.ConsumerErrorCount < 1 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }

            Assert.Equal(1, runtime.ConsumerErrorCount);
            Assert.Equal(1, runtime.Session.ProcessedCount);
        }
    }
}
