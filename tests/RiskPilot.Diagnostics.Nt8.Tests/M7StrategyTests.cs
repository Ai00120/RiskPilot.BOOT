using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Contract;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Consumers;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// M7 (Strategies / risk gate) data-reliability tests. They drive the real production
    /// accumulator, consumer, assembler, probe and host capture adapter and assert the
    /// truthfulness contract: a strategy not observed is reported NOT_OBSERVED (never assumed
    /// inactive), availability is only counted when the source reported it, a reason code the
    /// catalog cannot place is reported unattributed (never guessed), and the surface is never
    /// observable without a real observation.
    /// </summary>
    public class M7StrategyTests
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

        private static FakeDataProvider ProviderWith(StrategyRiskGateObservation observation)
        {
            return new FakeDataProvider { Snapshot = new DiagnosticSnapshot { StrategyRiskGate = observation } };
        }

        // ---------------- Accumulator: attribution, dedupe, correlation ----------------

        [Fact]
        public void Accumulator_AttributesReasonCodesToPublishedStages()
        {
            var accumulator = new StrategyEvidenceAccumulator();
            accumulator.Add(new StrategyRecord
            {
                StrategyId = "STRAT_SCALPING_CANDIDATE_01",
                ReasonCodes = new[] { "ALL_CANDIDATES_INVALID", "SIZED_SUCCESSFULLY", "RISK_APPROVED" }
            });

            var observation = accumulator.ToObservation();

            Assert.Equal(1, observation.ObservedRecordCount);
            Assert.Equal(1, observation.AllCandidatesInvalidCount());
            Assert.Equal(1, observation.Stage3OutcomesObserved());
            Assert.Equal(1, observation.Stage4EvaluationsObserved());
            Assert.Equal(0, observation.UnattributedCount());
        }

        [Fact]
        public void Accumulator_UnknownReasonCode_IsUnattributedNotDropped()
        {
            var accumulator = new StrategyEvidenceAccumulator();
            accumulator.Add(new StrategyRecord
            {
                StrategyId = "STRAT_SCALPING_CANDIDATE_01",
                ReasonCodes = new[] { "SOME_FUTURE_CODE" }
            });

            var observation = accumulator.ToObservation();

            Assert.Equal(1, observation.UnattributedCount());
            Assert.Equal(1, observation.UnattributedReasonCodes["SOME_FUTURE_CODE"]);
            Assert.Equal(0, observation.CandidateRejectionsObserved());
        }

        [Fact]
        public void Accumulator_AmbiguousReasonCode_IsUnattributed()
        {
            // FLAT_DIRECTION appears in both the validator and the sizing catalogs, so it is
            // ambiguous by design and must never be attributed to one stage.
            var accumulator = new StrategyEvidenceAccumulator();
            accumulator.Add(new StrategyRecord
            {
                StrategyId = "STRAT_TREND_PULLBACK_01",
                ReasonCodes = new[] { "FLAT_DIRECTION" }
            });

            Assert.Equal(1, accumulator.ToObservation().UnattributedCount());
        }

        [Fact]
        public void Accumulator_RepeatReasonCodesInOneRecord_CountedOncePerReport()
        {
            var accumulator = new StrategyEvidenceAccumulator();
            accumulator.Add(new StrategyRecord
            {
                StrategyId = "STRAT_TREND_PULLBACK_01",
                ReasonCodes = new[] { "ALL_CANDIDATES_INVALID", "ALL_CANDIDATES_INVALID" }
            });

            Assert.Equal(2, accumulator.ToObservation().AllCandidatesInvalidCount());
        }

        [Fact]
        public void Accumulator_ObservabilityFlagsArePerField()
        {
            var accumulator = new StrategyEvidenceAccumulator();
            accumulator.Add(new StrategyRecord
            {
                StrategyId = "STRAT_TREND_PULLBACK_01"
            });

            var strategy = accumulator.ToObservation().Strategies[0];
            Assert.True(strategy.RecordObserved);
            Assert.False(strategy.AvailableObservable);
            Assert.False(strategy.CandidateCountsObservable);
        }

        [Fact]
        public void Accumulator_DerivesCorrelationFromReportedIds()
        {
            var accumulator = new StrategyEvidenceAccumulator();
            accumulator.Add(new StrategyRecord
            {
                StrategyId = "STRAT_TREND_PULLBACK_01",
                CorrelationObservable = true,
                DecisionId = "d1",
                ProposalId = "p1",
                RiskEvaluationId = "r1"
            });

            var correlation = accumulator.ToObservation().Correlation;
            Assert.True(correlation.CorrelationObservable);
            Assert.True(correlation.CorrelationComplete);
        }

        [Fact]
        public void Accumulator_IncompleteCorrelation_ReportsGapReason()
        {
            var accumulator = new StrategyEvidenceAccumulator();
            accumulator.Add(new StrategyRecord
            {
                StrategyId = "STRAT_TREND_PULLBACK_01",
                CorrelationObservable = true,
                DecisionId = "d1",
                ProposalId = string.Empty,
                RiskEvaluationId = "r1"
            });

            var correlation = accumulator.ToObservation().Correlation;
            Assert.True(correlation.CorrelationObservable);
            Assert.False(correlation.CorrelationComplete);
            Assert.Equal("PROPOSAL_ID_MISSING", correlation.CorrelationGapReason);
        }

        [Fact]
        public void Accumulator_NoRecordWithId_StillCountsAsObservedRecord()
        {
            var accumulator = new StrategyEvidenceAccumulator();
            accumulator.Add(new StrategyRecord { StrategyId = string.Empty });

            var observation = accumulator.ToObservation();
            Assert.Equal(1, observation.ObservedRecordCount);
            Assert.Empty(observation.Strategies);
            Assert.True(observation.StrategySurfaceObservable);
        }

        // ---------------- Assembler: counter path and accumulator path agree ----------------

        [Fact]
        public void Assembler_AccumulatorPath_DerivesDistributionFromRealRecords()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var consumer = new M7StrategyConsumer(session);
            consumer.Consume(new StrategyRecord
            {
                StrategyId = "STRAT_ORDER_FLOW_INTRADAY_01",
                AvailableObservable = true,
                Available = false,
                MissingCapabilityObservable = true,
                MissingCapability = true,
                ReasonCodes = new[] { "ALL_CANDIDATES_INVALID", "SOME_FUTURE_CODE" }
            });

            var snapshot = SnapshotAssembler.Assemble(new DiagnosticSnapshot(), session);

            var observation = snapshot.StrategyRiskGate;
            Assert.True(observation.StrategySurfaceObservable);
            Assert.Equal(1, observation.AllCandidatesInvalidCount());
            Assert.Equal(1, observation.UnattributedCount());
            Assert.Single(observation.Strategies);
            Assert.False(observation.Strategies[0].Available);
        }

        [Fact]
        public void Assembler_NoRecords_LeavesObservationUnobserved()
        {
            var session = new SessionContext(SessionId.New(Now), Now);
            var snapshot = SnapshotAssembler.Assemble(new DiagnosticSnapshot(), session);

            Assert.False(snapshot.StrategyRiskGate.StrategySurfaceObservable);
            Assert.Equal(0, snapshot.StrategyRiskGate.ObservedRecordCount);
        }

        // ---------------- Host capture adapter (read-only contract) ----------------

        [Fact]
        public void HostCapture_NoSource_IngestsNothing()
        {
            var captured = new StrategyDiagnosticCapture(record => { }, null);
            Assert.False(captured.HasSource);
            Assert.Equal(0, captured.TryCapture());
        }

        [Fact]
        public void HostCapture_ReadsAndIngestsEveryStrategyOnce()
        {
            var ingested = new List<StrategyRecord>();
            var source = new FakeStrategySource(new[]
            {
                new StrategyRecord { StrategyId = "STRAT_SCALPING_CANDIDATE_01" },
                new StrategyRecord { StrategyId = "STRAT_TREND_PULLBACK_01" }
            });

            var captured = new StrategyDiagnosticCapture(ingested.Add, source);
            Assert.Equal(2, captured.TryCapture());
            Assert.Equal(2, ingested.Count);
        }

        [Fact]
        public void HostCapture_ThrowingSource_IsContained()
        {
            var captured = new StrategyDiagnosticCapture(record => { }, new ThrowingStrategySource());
            Assert.Equal(0, captured.TryCapture());
        }

        // ---------------- Runtime integration: M7 reflects real ingested strategy records ----------------

        [Fact]
        public void RuntimeIntegration_UnobservedStrategy_SurfaceIsInconclusive()
        {
            var provider = new FakeDataProvider();
            using var runtime = new DiagnosticRuntime(provider, new FakeClock(Now), 512, 64, 512);
            runtime.Initialize();
            runtime.Start();

            var report = runtime.RunDiagnosticPass();
            var evaluation = Find(report.Results, "M7.STRATEGY_EVALUATION");
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, evaluation.Status);
            Assert.Contains("STRATEGY_EVALUATION_UNOBSERVABLE", evaluation.ReasonCodes);
        }

        [Fact]
        public void RuntimeIntegration_IngestedStrategyRecord_DrivesM7ObservationAndEvidence()
        {
            var provider = new FakeDataProvider();
            using var runtime = new DiagnosticRuntime(provider, new FakeClock(Now), 512, 64, 512);
            runtime.Initialize();
            runtime.Start();

            Assert.True(runtime.Ingest(new StrategyRecord
            {
                StrategyId = "STRAT_SCALPING_CANDIDATE_01",
                AvailableObservable = true,
                Available = true,
                CandidateCountsObservable = true,
                CandidateCount = 3,
                ValidCandidateCount = 1,
                InvalidCandidateCount = 2,
                ReasonCodes = new[] { "ALL_CANDIDATES_INVALID", "SOME_FUTURE_CODE" }
            }));

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (runtime.Session.ProcessedCount < 1 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }

            var report = runtime.RunDiagnosticPass();
            var surface = Find(report.Results, "M7.STRATEGY_SURFACE");
            Assert.Equal(DiagnosticStatus.PASS, surface.Status);

            var evaluation = Find(report.Results, "M7.STRATEGY_EVALUATION");
            Assert.Contains("AVAILABLE=1", evaluation.ReasonCodes);

            // Both paths must see the same real evidence: the live probe and the exported package.
            var dataset = runtime.BuildEvidenceDataset();
            var observation = dataset.Snapshot.StrategyRiskGate;
            Assert.True(observation.StrategySurfaceObservable);
            Assert.Equal(1, observation.ObservedRecordCount);
            Assert.Equal(1, observation.AllCandidatesInvalidCount());
            Assert.Equal(1, observation.UnattributedCount());
        }

        [Fact]
        public void RuntimeIntegration_RunnerCaptureHook_IngestsBeforeM7Evaluates()
        {
            // The host wires a read-only strategy source into the runner via a capture hook. The
            // hook enqueues records and the runner must wait (bounded) for the drain thread so M7
            // evaluates the state those records produced, not an empty/stale surface.
            var provider = new FakeDataProvider();
            using var runtime = new DiagnosticRuntime(provider, new FakeClock(Now), 512, 64, 512);
            runtime.Initialize();
            runtime.Start();

            var source = new FakeStrategySource(new[]
            {
                new StrategyRecord
                {
                    StrategyId = "STRAT_SCALPING_CANDIDATE_01",
                    AvailableObservable = true,
                    Available = true,
                    ReasonCodes = new[] { "ALL_CANDIDATES_INVALID" }
                }
            });

            var capture = new StrategyDiagnosticCapture(record => runtime.Ingest(record), source);
            var hookInvocations = 0;
            var runner = new RuntimeDiagnosticRunner(
                runtime,
                new InMemoryEvidenceSink(),
                () => true,
                () => { hookInvocations++; return capture.TryCapture(); });

            var context = new DiagnosticRunContext(
                DiagnosticMode.Full,
                new[] { "M7" },
                () => false,
                _ => { },
                _ => { },
                (_, __) => { });

            var result = runner.Run(context);

            Assert.Equal(1, hookInvocations);
            var surface = result.Report.Results.FirstOrDefault(r => r.CheckId == "M7.STRATEGY_SURFACE");
            Assert.NotNull(surface);
            Assert.Equal(DiagnosticStatus.PASS, surface.Status);
        }

        // ---------------- Test doubles ----------------
        private sealed class FakeStrategySource : IReadOnlyDiagnosticSource
        {
            private readonly IReadOnlyList<StrategyRecord> _strategies;

            public FakeStrategySource(IReadOnlyList<StrategyRecord> strategies)
            {
                _strategies = strategies;
            }

            public string SourceId { get { return "TestSource"; } }

            public IReadOnlyList<DiagnosticResult> ReadResults() { return new List<DiagnosticResult>(); }

            public IReadOnlyList<StrategyRecord> ReadStrategies() { return _strategies; }
        }

        private sealed class ThrowingStrategySource : IReadOnlyDiagnosticSource
        {
            public string SourceId { get { return "Throwing"; } }

            public IReadOnlyList<DiagnosticResult> ReadResults() { return new List<DiagnosticResult>(); }

            public IReadOnlyList<StrategyRecord> ReadStrategies()
            {
                throw new InvalidOperationException("source failure");
            }
        }
    }
}
