using System;
using System.Collections.Generic;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Contract;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// D-097 M7 acceptance: the AddOn must execute the Stage 3/4 strategy verification when a
    /// read-only diagnostic source is supplied, and stay INCONCLUSIVE (never a fabricated PASS)
    /// when no source is available. The source is pulled before a pass; it is never mutated.
    /// </summary>
    [Collection("StubHostState")]
    public class D097M7IntegrationTests
    {
        [Fact]
        public void AddOn_NoStrategySource_LeavesM7Inconclusive()
        {
            var addOn = new DiagnosticAddOn { ReadinessRetryEnabled = false };
            try
            {
                addOn.ApplyLifecycleState("Active");

                Assert.Equal(0, addOn.CaptureStrategyObservations());
                var surface = Find(RunM7(addOn), "M7.STRATEGY_SURFACE");
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, surface.Status);
            }
            finally
            {
                addOn.SafeDispose();
            }
        }

        [Fact]
        public void AddOn_WithStrategySource_ExecutesM7Verification()
        {
            var addOn = new DiagnosticAddOn { ReadinessRetryEnabled = false };
            try
            {
                addOn.ApplyLifecycleState("Active");

                // A host component may expose its diagnostic source after the AddOn initialises.
                addOn.StrategyDiagnosticSource = new FakeStrategySource(new[]
                {
                    new StrategyRecord
                    {
                        StrategyId = "STRAT_SCALPING_CANDIDATE_01",
                        AvailableObservable = true,
                        Available = true,
                        ReasonCodes = new[] { "ALL_CANDIDATES_INVALID" }
                    }
                });

                Assert.Equal(1, addOn.CaptureStrategyObservations());
                WaitProcessed(addOn, 1);

                var report = RunM7(addOn);
                var surface = Find(report, "M7.STRATEGY_SURFACE");
                Assert.Equal(DiagnosticStatus.PASS, surface.Status);

                // The same evidence must reach the exported snapshot, not only the live probe.
                var observation = addOn.Runtime.BuildEvidenceDataset().Snapshot.StrategyRiskGate;
                Assert.True(observation.StrategySurfaceObservable);
                Assert.Equal(1, observation.ObservedRecordCount);
            }
            finally
            {
                addOn.SafeDispose();
            }
        }

        private static DiagnosticReport RunM7(DiagnosticAddOn addOn)
        {
            return addOn.Runtime.RunDiagnosticPass(new[] { "M7" }, null, null);
        }

        private static void WaitProcessed(DiagnosticAddOn addOn, long expected)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (addOn.Runtime.Session.ProcessedCount < expected && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }

            Assert.True(addOn.Runtime.Session.ProcessedCount >= expected);
        }

        private static DiagnosticResult Find(DiagnosticReport report, string checkId)
        {
            foreach (var result in report.Results)
            {
                if (result.CheckId == checkId)
                {
                    return result;
                }
            }

            throw new InvalidOperationException("check not found: " + checkId);
        }

        private sealed class FakeStrategySource : IReadOnlyDiagnosticSource
        {
            private readonly IReadOnlyList<StrategyRecord> _strategies;

            public FakeStrategySource(IReadOnlyList<StrategyRecord> strategies)
            {
                _strategies = strategies;
            }

            public string SourceId { get { return "D097TestSource"; } }

            public IReadOnlyList<DiagnosticResult> ReadResults() { return new List<DiagnosticResult>(); }

            public IReadOnlyList<StrategyRecord> ReadStrategies() { return _strategies; }
        }
    }
}
