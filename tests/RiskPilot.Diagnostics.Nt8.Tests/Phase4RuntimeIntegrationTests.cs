using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Evidence;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Phase 4 integration tests over the real runtime: report generation, evidence export
    /// through an in-memory sink, queue overflow handling, and empty/partial data behaviour.
    /// They exercise the real production code paths end to end (the only double is the
    /// deterministic clock and the in-memory data provider).
    /// </summary>
    public class Phase4RuntimeIntegrationTests
    {
        private static DiagnosticRuntime Runtime(FakeDataProvider provider, int queueCapacity, int evidenceCapacity)
        {
            var runtime = new DiagnosticRuntime(provider, new FakeClock(DateTime.UtcNow), queueCapacity, 64, evidenceCapacity);
            runtime.Initialize();
            return runtime;
        }

        private static DiagnosticRunResult RunFull(DiagnosticRuntime runtime)
        {
            var result = new DiagnosticRunResult
            {
                Mode = DiagnosticMode.Full,
                ModeId = "full",
                SessionId = runtime.Session.SessionId,
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow,
                FinalState = RunState.Completed
            };
            result.Report = runtime.RunDiagnosticPass();
            return result;
        }

        [Fact]
        public void ReportGeneration_ProducesAReportWithAllModules()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider, 256, 256);

            var report = runtime.RunDiagnosticPass();
            Assert.NotNull(report);
            Assert.NotEmpty(report.Results);

            var modules = new HashSet<string>();
            foreach (var result in report.Results)
            {
                modules.Add(result.ModuleId);
            }

            Assert.Contains("M1", modules);
            Assert.Contains("M8", modules);
            Assert.Contains("M9", modules);
        }

        [Fact]
        public void EvidenceExport_WritesAll17Artifacts_AndVerifies()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider, 256, 256);
            runtime.Start();
            runtime.Ingest(new OrderRecord
            {
                OrderRef = "ord-1",
                InstrumentId = "ES 03-26",
                State = OrderLifecycleState.Filled,
                Quantity = 1,
                FilledQuantity = 1
            });

            // Give the drain thread a moment to consume the record.
            Thread.Sleep(50);

            var result = RunFull(runtime);
            var sink = new InMemoryEvidenceSink();
            var ok = runtime.WriteEvidencePackage(result, sink, out var directory);

            Assert.True(ok);
            Assert.False(string.IsNullOrEmpty(directory));
            Assert.Equal(17, sink.Files.Count);
            foreach (var name in EvidencePackageCatalog.All)
            {
                Assert.True(sink.Files.ContainsKey(name), "missing artifact: " + name);
            }
        }

        [Fact]
        public void EvidenceExport_WithoutRun_StillWritesACompletePackage()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider, 256, 256);

            var result = new DiagnosticRunResult { SessionId = runtime.Session.SessionId, ModeId = "quick" };
            var sink = new InMemoryEvidenceSink();
            Assert.True(runtime.WriteEvidencePackage(result, sink, out _));
            Assert.Equal(17, sink.Files.Count);
        }

        [Fact]
        public void QueueOverflow_IsReportedAsFail_NotSilentlyDropped()
        {
            // Capacity 1 and no drain thread: the second record must be dropped, and the
            // resulting report must carry a FAIL for event integrity.
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider, 1, 256);

            Assert.True(runtime.Ingest(new OrderRecord { OrderRef = "a", InstrumentId = "ES", State = OrderLifecycleState.Working }));
            runtime.Ingest(new OrderRecord { OrderRef = "b", InstrumentId = "ES", State = OrderLifecycleState.Working });

            Assert.True(runtime.Session.DroppedCount > 0, "the overflow must be counted as dropped");

            var report = runtime.RunDiagnosticPass();
            var integrity = report.Results.FirstOrDefault(r => r.CheckId == "M0.EVENT_INTEGRITY");
            Assert.NotNull(integrity);
            Assert.Equal(Core.Status.DiagnosticStatus.FAIL, integrity.Status);
        }

        [Fact]
        public void EmptyData_ProducesInconclusiveNotPass()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider, 256, 256);

            var report = runtime.RunDiagnosticPass();
            Assert.Equal(Core.Status.DiagnosticStatus.INCONCLUSIVE, report.AggregateStatus());
        }

        [Fact]
        public void EvidenceCollector_IsBounded_AndReportsTruncation()
        {
            var collector = new EvidenceCollector(2);
            for (var i = 0; i < 5; i++)
            {
                collector.Consume(new OrderRecord { OrderRef = "ord-" + i, InstrumentId = "ES", State = OrderLifecycleState.Working });
            }

            Assert.True(collector.Truncated);
            Assert.Equal(3, collector.TruncatedRecordCount);

            var dataset = collector.Snapshot();
            Assert.Equal(2, dataset.Orders.Count);
            Assert.Contains("TRUNCATED", dataset.TruncationReason);
        }

        [Fact]
        public void EvidenceCollector_SeparatesRecordTypes()
        {
            var collector = new EvidenceCollector(16);
            collector.Consume(new OrderRecord { OrderRef = "o", InstrumentId = "ES" });
            collector.Consume(new ExecutionRecord { ExecutionRef = "e", OrderRef = "o", InstrumentId = "ES" });
            collector.Consume(new PositionRecord { InstrumentId = "ES", AccountRef = "A" });

            var dataset = collector.Snapshot();
            Assert.Single(dataset.Orders);
            Assert.Single(dataset.Executions);
            Assert.Single(dataset.Positions);
        }

        [Fact]
        public void PostTradeAuditObservation_IsBuiltFromCollectedRecords()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider, 256, 256);
            runtime.Start();
            runtime.Ingest(new OrderRecord
            {
                OrderRef = "ord-1",
                InstrumentId = "ES 03-26",
                State = OrderLifecycleState.Filled,
                Quantity = 1,
                FilledQuantity = 1
            });
            runtime.Ingest(new ExecutionRecord
            {
                ExecutionRef = "exec-1",
                OrderRef = "ord-1",
                InstrumentId = "ES 03-26",
                Price = 5000,
                Quantity = 1
            });

            Thread.Sleep(50);

            var dataset = runtime.BuildEvidenceDataset();
            Assert.NotNull(dataset.Snapshot);
            Assert.NotNull(dataset.Snapshot.PostTradeAudit);
            Assert.Equal(1, dataset.Snapshot.PostTradeAudit.OrderSnapshotCount);
            Assert.Equal(1, dataset.Snapshot.PostTradeAudit.ExecutionUpdateCount);
        }

        [Fact]
        public void PostTradeAudit_FoldsInHostAccountOrdersSnapshot()
        {
            // The host reads Account.Orders for M3 even when nothing was ingested. The
            // post-trade audit must observe that already-recorded surface rather than
            // reporting INCONCLUSIVE.
            var provider = new FakeDataProvider
            {
                Snapshot = new Core.Models.Observations.DiagnosticSnapshot
                {
                    OrderLifecycle = new Core.Models.Observations.OrderLifecycleObservation
                    {
                        OrderSurfaceObservable = true,
                        AccountCount = 1,
                        OrderSnapshotCount = 2,
                        Events = new List<Core.Models.Observations.OrderLifecycleEvent>
                        {
                            new Core.Models.Observations.OrderLifecycleEvent
                            {
                                OrderRef = "order#abc",
                                OrderIdClass = OrderIdClass.Valid,
                                State = OrderLifecycleState.Working,
                                InstrumentId = "ES 03-26",
                                Source = "Account.Orders",
                                IngestedAtUtc = DateTime.UtcNow
                            },
                            new Core.Models.Observations.OrderLifecycleEvent
                            {
                                OrderRef = "order#def",
                                OrderIdClass = OrderIdClass.Valid,
                                State = OrderLifecycleState.Filled,
                                InstrumentId = "ES 03-26",
                                Source = "Account.Orders",
                                IngestedAtUtc = DateTime.UtcNow
                            }
                        }
                    }
                }
            };

            using var runtime = Runtime(provider, 256, 256);
            var dataset = runtime.BuildEvidenceDataset();
            var audit = dataset.Snapshot.PostTradeAudit;

            Assert.NotNull(audit);
            Assert.True(audit.AuditSurfaceObservable);
            Assert.Equal(2, audit.OrderSnapshotCount);
            Assert.Equal(2, audit.Events.Count);
            Assert.Equal(1, audit.AccountCount);

            var report = runtime.RunDiagnosticPass();
            var surface = report.Results.FirstOrDefault(r => r.CheckId == "M9.AUDIT_SURFACE");
            Assert.NotNull(surface);
            Assert.Equal(Core.Status.DiagnosticStatus.PASS, surface.Status);
        }

        [Fact]
        public void PostTradeAudit_DoesNotDoubleCountRecordsAlreadyIngested()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new Core.Models.Observations.DiagnosticSnapshot
                {
                    OrderLifecycle = new Core.Models.Observations.OrderLifecycleObservation
                    {
                        OrderSurfaceObservable = true,
                        OrderSnapshotCount = 1,
                        Events = new List<Core.Models.Observations.OrderLifecycleEvent>
                        {
                            new Core.Models.Observations.OrderLifecycleEvent
                            {
                                OrderRef = "ord-1",
                                OrderIdClass = OrderIdClass.Valid,
                                State = OrderLifecycleState.Filled,
                                InstrumentId = "ES",
                                Source = "Account.Orders",
                                IngestedAtUtc = DateTime.UtcNow
                            }
                        }
                    }
                }
            };

            using var runtime = Runtime(provider, 256, 256);
            runtime.Start();
            runtime.Ingest(new OrderRecord
            {
                OrderRef = "ord-1",
                InstrumentId = "ES",
                State = OrderLifecycleState.Filled,
                Quantity = 1,
                FilledQuantity = 1
            });
            Thread.Sleep(50);

            var dataset = runtime.BuildEvidenceDataset();
            var audit = dataset.Snapshot.PostTradeAudit;

            // The same order ref appears in the collector and in the host snapshot; it must
            // be present exactly once in the audit event list.
            Assert.Equal(1, audit.Events.Count(e => e.OrderRef == "ord-1"));
        }

        [Fact]
        public void EvidenceDirectorySink_WritesEachRunToItsOwnDirectory()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rp-diag-" + Guid.NewGuid().ToString("N"));
            try
            {
                var sink = new EvidenceDirectorySink(root);
                var result1 = new DiagnosticRunResult { SessionId = "diag_20260101_000000_" + new string('a', 32) };
                var result2 = new DiagnosticRunResult { SessionId = "diag_20260101_000000_" + new string('b', 32) };

                var dir1 = sink.Begin(result1);
                sink.Write("summary.html", "<html></html>");
                var dir2 = sink.Begin(result2);
                sink.Write("summary.html", "<html></html>");

                Assert.NotEqual(dir1, dir2);
                Assert.True(System.IO.Directory.Exists(dir1));
                Assert.True(System.IO.Directory.Exists(dir2));
                Assert.True(System.IO.File.Exists(System.IO.Path.Combine(dir1, "summary.html")));
            }
            finally
            {
                if (System.IO.Directory.Exists(root))
                {
                    System.IO.Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void EvidenceDirectorySink_RejectsPathTraversalInArtifactName()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rp-diag-" + Guid.NewGuid().ToString("N"));
            try
            {
                var sink = new EvidenceDirectorySink(root);
                var directory = sink.Begin(new DiagnosticRunResult { SessionId = "diag_20260101_000000_" + new string('c', 32) });
                sink.Write("..\\..\\evil.html", "<html></html>");

                // The file must land inside the package directory, never above it.
                var escaped = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(directory), "evil.html");
                Assert.False(System.IO.File.Exists(escaped));
                Assert.True(System.IO.File.Exists(System.IO.Path.Combine(directory, ".._.._evil.html")));
            }
            finally
            {
                if (System.IO.Directory.Exists(root))
                {
                    System.IO.Directory.Delete(root, true);
                }
            }
        }
    }
}
