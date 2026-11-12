using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// M9 Post-Trade Audit tests. They assert that the audit only observes already-recorded
    /// events, reports missing/unmatched events as INCONCLUSIVE (never PASS), flags malformed
    /// order ids, and never fabricates data when the audit surface is absent.
    /// </summary>
    public class Phase4PostTradeAuditTests
    {
        private static ProbeContext Context(FakeDataProvider provider)
        {
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            return new ProbeContext(provider, session, new FakeClock(DateTime.UtcNow), "corr00000000");
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

        [Fact]
        public void M9_WithoutObservation_IsInconclusive_NotPass()
        {
            var provider = new FakeDataProvider { Snapshot = new DiagnosticSnapshot() };
            var results = new M9PostTradeAuditProbe().Evaluate(Context(provider));

            var surface = Find(results, "M9.AUDIT_SURFACE");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, surface.Status);
        }

        [Fact]
        public void M9_WithObservableSurface_PassesSurfaceCheck()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    PostTradeAudit = new PostTradeAuditObservation
                    {
                        AuditSurfaceObservable = true,
                        AccountCount = 1,
                        OrderSnapshotCount = 2,
                        ExecutionUpdateCount = 2,
                        PositionUpdateCount = 1,
                        ExecutionsInScope = true,
                        OrderIdClassCounts = new Dictionary<OrderIdClass, int> { { OrderIdClass.Valid, 2 } }
                    }
                }
            };

            var results = new M9PostTradeAuditProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M9.AUDIT_SURFACE").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M9.ORDERID_CLASSIFICATION").Status);
        }

        [Fact]
        public void M9_MalformedOrderId_Fails()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    PostTradeAudit = new PostTradeAuditObservation
                    {
                        AuditSurfaceObservable = true,
                        ExecutionsInScope = true,
                        OrderIdClassCounts = new Dictionary<OrderIdClass, int>
                        {
                            { OrderIdClass.Valid, 1 },
                            { OrderIdClass.Null, 1 }
                        }
                    }
                }
            };

            var results = new M9PostTradeAuditProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M9.ORDERID_CLASSIFICATION").Status);
        }

        [Fact]
        public void M9_MissingCorrelation_IsInconclusive_NotPass()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    PostTradeAudit = new PostTradeAuditObservation
                    {
                        AuditSurfaceObservable = true,
                        ExecutionsInScope = true,
                        UnmatchedTerminalOrderCount = 1,
                        UnmatchedExecutionCount = 1,
                        OrderIdClassCounts = new Dictionary<OrderIdClass, int> { { OrderIdClass.Valid, 1 } }
                    }
                }
            };

            var results = new M9PostTradeAuditProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M9.CORRELATION_TIMING").Status);
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M9.MISSING_OR_DUPLICATE").Status);
        }

        [Fact]
        public void M9_DuplicateEvents_AreReportedAsFail()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    PostTradeAudit = new PostTradeAuditObservation
                    {
                        AuditSurfaceObservable = true,
                        ExecutionsInScope = true,
                        DuplicateEventCount = 3,
                        OrderIdClassCounts = new Dictionary<OrderIdClass, int> { { OrderIdClass.Valid, 1 } }
                    }
                }
            };

            var results = new M9PostTradeAuditProbe().Evaluate(Context(provider));
            var result = Find(results, "M9.MISSING_OR_DUPLICATE");
            Assert.Equal(DiagnosticStatus.FAIL, result.Status);
            Assert.Contains("duplicates=3", result.Observation);
        }

        [Fact]
        public void M9_CleanAudit_PassesCorrelationAndIntegrity()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    PostTradeAudit = new PostTradeAuditObservation
                    {
                        AuditSurfaceObservable = true,
                        ExecutionsInScope = true,
                        UnmatchedTerminalOrderCount = 0,
                        UnmatchedExecutionCount = 0,
                        DuplicateEventCount = 0,
                        InvalidTimestampCount = 0,
                        OutOfOrderTimestampCount = 0,
                        OrderIdClassCounts = new Dictionary<OrderIdClass, int> { { OrderIdClass.Valid, 4 } }
                    }
                }
            };

            var results = new M9PostTradeAuditProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M9.CORRELATION_TIMING").Status);
            Assert.Equal(DiagnosticStatus.PASS, Find(results, "M9.MISSING_OR_DUPLICATE").Status);
        }

        [Fact]
        public void M9_ExecutionsOutOfScope_KeepsExecutionChecksInconclusive()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot
                {
                    PostTradeAudit = new PostTradeAuditObservation
                    {
                        AuditSurfaceObservable = true,
                        ExecutionsInScope = false,
                        OrderIdClassCounts = new Dictionary<OrderIdClass, int> { { OrderIdClass.Valid, 1 } }
                    }
                }
            };

            var results = new M9PostTradeAuditProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M9.EXECUTION_LIFECYCLE").Status);
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M9.CORRELATION_TIMING").Status);
        }

        [Fact]
        public void PostTradeAuditMode_RunsOnlyM9()
        {
            var definition = DiagnosticModeCatalog.For(DiagnosticMode.PostTradeAudit);
            Assert.Equal(new[] { "M9" }, definition.ModuleIds);
        }

        [Fact]
        public void QuickMode_SelectsSixReadOnlyModules()
        {
            var definition = DiagnosticModeCatalog.For(DiagnosticMode.Quick);
            Assert.Equal(new[] { "M1", "M2", "M3", "M4", "M5", "M8" }, definition.ModuleIds);
        }

        [Fact]
        public void FullMode_SelectsAllEightModules()
        {
            var definition = DiagnosticModeCatalog.For(DiagnosticMode.Full);
            Assert.Equal(new[] { "M1", "M2", "M3", "M4", "M5", "M6", "M7", "M8" }, definition.ModuleIds);
        }
    }
}
