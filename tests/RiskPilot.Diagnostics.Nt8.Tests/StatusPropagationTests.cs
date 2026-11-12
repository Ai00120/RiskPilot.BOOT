using System;
using System.Collections.Generic;
using System.Text.Json;
using RiskPilot.Diagnostics.Nt8.Core.Clock;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Logging;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>A probe that always throws, to prove the engine contains failures.</summary>
    internal sealed class ThrowingProbe : IDiagnosticProbe
    {
        public string ModuleId { get { return "MX"; } }
        public string Title { get { return "Throwing"; } }
        public IReadOnlyList<DiagnosticResult> Evaluate(ProbeContext context)
        {
            throw new InvalidOperationException("synthetic probe failure password=hunter2");
        }
    }

    /// <summary>A probe that returns a fixed status.</summary>
    internal sealed class FixedStatusProbe : IDiagnosticProbe
    {
        private readonly DiagnosticStatus _status;
        public FixedStatusProbe(string moduleId, DiagnosticStatus status)
        {
            ModuleId = moduleId;
            _status = status;
        }

        public string ModuleId { get; private set; }
        public string Title { get { return "Fixed"; } }
        public IReadOnlyList<DiagnosticResult> Evaluate(ProbeContext context)
        {
            return new List<DiagnosticResult>
            {
                new DiagnosticResult
                {
                    ModuleId = ModuleId,
                    CheckId = ModuleId + ".CHECK",
                    Status = _status,
                    EvaluatedAtUtc = context.Clock.UtcNow,
                    CorrelationId = context.CorrelationId
                }
            };
        }
    }

    public class StatusPropagationTests
    {
        private static SessionContext NewSession()
        {
            return new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
        }

        [Theory]
        [InlineData(DiagnosticStatus.PASS, DiagnosticStatus.PASS, DiagnosticStatus.PASS)]
        [InlineData(DiagnosticStatus.PASS, DiagnosticStatus.FAIL, DiagnosticStatus.FAIL)]
        [InlineData(DiagnosticStatus.FAIL, DiagnosticStatus.PASS, DiagnosticStatus.FAIL)]
        [InlineData(DiagnosticStatus.PASS, DiagnosticStatus.INCONCLUSIVE, DiagnosticStatus.INCONCLUSIVE)]
        [InlineData(DiagnosticStatus.INCONCLUSIVE, DiagnosticStatus.PASS, DiagnosticStatus.INCONCLUSIVE)]
        [InlineData(DiagnosticStatus.INCONCLUSIVE, DiagnosticStatus.FAIL, DiagnosticStatus.FAIL)]
        [InlineData(DiagnosticStatus.PASS, DiagnosticStatus.NOT_APPLICABLE, DiagnosticStatus.PASS)]
        public void Worst_UsesFailClosedPrecedence(DiagnosticStatus left, DiagnosticStatus right, DiagnosticStatus expected)
        {
            Assert.Equal(expected, left.Worst(right));
        }

        [Fact]
        public void AggregateStatus_IsWorstAcrossResults()
        {
            var engine = new DiagnosticEngine(new FakeDataProvider(), new DiagnosticLog(64), new FakeClock(DateTime.UtcNow));
            engine.Register(new FixedStatusProbe("MA", DiagnosticStatus.PASS));
            engine.Register(new FixedStatusProbe("MB", DiagnosticStatus.INCONCLUSIVE));
            engine.Register(new FixedStatusProbe("MC", DiagnosticStatus.PASS));

            var report = engine.Run(NewSession());
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, report.AggregateStatus());
        }

        [Fact]
        public void AggregateStatus_FailsWhenAnyResultFails()
        {
            var engine = new DiagnosticEngine(new FakeDataProvider(), new DiagnosticLog(64), new FakeClock(DateTime.UtcNow));
            engine.Register(new FixedStatusProbe("MA", DiagnosticStatus.PASS));
            engine.Register(new FixedStatusProbe("MB", DiagnosticStatus.FAIL));

            var report = engine.Run(NewSession());
            Assert.Equal(DiagnosticStatus.FAIL, report.AggregateStatus());
        }

        [Fact]
        public void Engine_ContainsProbeExceptionAsFail()
        {
            var log = new DiagnosticLog(64);
            var engine = new DiagnosticEngine(new FakeDataProvider(), log, new FakeClock(DateTime.UtcNow));
            engine.Register(new ThrowingProbe());
            engine.Register(new FixedStatusProbe("MA", DiagnosticStatus.PASS));

            var report = engine.Run(NewSession());

            Assert.Equal(DiagnosticStatus.FAIL, report.AggregateStatus());
            Assert.Contains(report.Results, r => r.CheckId == "MX.PROBE_EXCEPTION");
            // The other probe still ran.
            Assert.Contains(report.Results, r => r.ModuleId == "MA" && r.Status == DiagnosticStatus.PASS);
        }

        [Fact]
        public void Engine_ProbeExceptionMessageIsRedacted()
        {
            var engine = new DiagnosticEngine(new FakeDataProvider(), new DiagnosticLog(64), new FakeClock(DateTime.UtcNow));
            engine.Register(new ThrowingProbe());
            var report = engine.Run(NewSession());
            var json = ReportSerializer.Serialize(report);
            Assert.DoesNotContain("hunter2", json);
        }

        [Fact]
        public void Engine_SurfacesDataLossAsFail()
        {
            var engine = new DiagnosticEngine(new FakeDataProvider(), new DiagnosticLog(64), new FakeClock(DateTime.UtcNow));
            engine.Register(new FixedStatusProbe("MA", DiagnosticStatus.PASS));
            var session = NewSession();
            session.RecordDropped(5);

            var report = engine.Run(session);

            Assert.Equal(DiagnosticStatus.FAIL, report.AggregateStatus());
            Assert.Contains(report.Results, r => r.CheckId == "M0.EVENT_INTEGRITY");
        }

        [Fact]
        public void Engine_RecordsUnobservableSection()
        {
            var engine = new DiagnosticEngine(new FakeDataProvider(), new DiagnosticLog(64), new FakeClock(DateTime.UtcNow));
            var report = engine.Run(NewSession());
            Assert.NotEmpty(report.Unobservable);
        }
    }
}
