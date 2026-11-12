using System;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    public class LifecycleTests
    {
        private static DiagnosticRuntime NewRuntime()
        {
            return new DiagnosticRuntime(new FakeDataProvider(), new FakeClock(new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc)), 64, 64);
        }

        [Fact]
        public void Initialize_CreatesSessionAndProbes()
        {
            using var runtime = NewRuntime();
            Assert.False(runtime.IsInitialized);
            runtime.Initialize();
            Assert.True(runtime.IsInitialized);
            Assert.NotNull(runtime.Session);
            Assert.True(SessionId.IsValid(runtime.Session.SessionId));
        }

        [Fact]
        public void Initialize_IsIdempotent()
        {
            using var runtime = NewRuntime();
            runtime.Initialize();
            var first = runtime.Session.SessionId;
            runtime.Initialize();
            Assert.Equal(first, runtime.Session.SessionId);
        }

        [Fact]
        public void Start_BeforeInitialize_Throws()
        {
            using var runtime = NewRuntime();
            Assert.Throws<InvalidOperationException>(() => runtime.Start());
        }

        [Fact]
        public void Start_ThenStop_TransitionsCleanly()
        {
            using var runtime = NewRuntime();
            runtime.Initialize();
            runtime.Start();
            Assert.True(runtime.IsRunning);
            runtime.Stop();
            Assert.False(runtime.IsRunning);
        }

        [Fact]
        public void Start_IsIdempotent()
        {
            using var runtime = NewRuntime();
            runtime.Initialize();
            runtime.Start();
            runtime.Start();
            Assert.True(runtime.IsRunning);
            runtime.Stop();
        }

        [Fact]
        public void Stop_WhenNotStarted_IsNoOp()
        {
            using var runtime = NewRuntime();
            runtime.Initialize();
            runtime.Stop();
            Assert.False(runtime.IsRunning);
        }

        [Fact]
        public void Ingest_AfterStart_IsProcessedByWorker()
        {
            using var runtime = NewRuntime();
            runtime.Initialize();
            runtime.Start();

            for (var i = 0; i < 20; i++)
            {
                runtime.Ingest(new MarketDataRecord
                {
                    InstrumentId = "ES 12-26",
                    Kind = MarketDataKind.Quote,
                    SourceTimestampUtc = DateTime.UtcNow.AddTicks(i)
                });
            }

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (runtime.Session.ProcessedCount < 20 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }

            Assert.Equal(20, runtime.Session.ProcessedCount);
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var runtime = NewRuntime();
            runtime.Initialize();
            runtime.Start();
            runtime.Dispose();
            runtime.Dispose();
            Assert.True(runtime.IsDisposed);
        }

        [Fact]
        public void Dispose_WithoutInitialize_IsSafe()
        {
            var runtime = NewRuntime();
            runtime.Dispose();
            Assert.True(runtime.IsDisposed);
        }

        [Fact]
        public void Initialize_AfterDispose_Throws()
        {
            var runtime = NewRuntime();
            runtime.Dispose();
            Assert.Throws<ObjectDisposedException>(() => runtime.Initialize());
        }

        [Fact]
        public void WriteReport_ProducesReportAndManifest()
        {
            using var runtime = NewRuntime();
            runtime.Initialize();
            var sink = new InMemoryReportSink();
            var json = runtime.WriteReport(sink);

            Assert.NotNull(json);
            Assert.True(sink.Artifacts.ContainsKey("report.json"));
            Assert.True(sink.Artifacts.ContainsKey("manifest.json"));
            Assert.Equal(json, sink.Artifacts["report.json"]);
        }

        [Fact]
        public void Shutdown_DoesNotLoseQueuedEventsSilently()
        {
            var runtime = NewRuntime();
            runtime.Initialize();
            for (var i = 0; i < 50; i++)
            {
                runtime.Ingest(new MarketDataRecord { InstrumentId = "ES 12-26", Kind = MarketDataKind.Trade, SourceTimestampUtc = DateTime.UtcNow.AddTicks(i) });
            }

            // Never started the worker; Dispose must still not silently discard.
            runtime.Dispose();
            Assert.Equal(50, runtime.Session.EnqueuedCount);
        }
    }
}
