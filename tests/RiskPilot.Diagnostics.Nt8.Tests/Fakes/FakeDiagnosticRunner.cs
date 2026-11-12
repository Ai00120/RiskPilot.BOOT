using System;
using System.Collections.Generic;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Tests.Fakes
{
    /// <summary>
    /// Deterministic <see cref="IDiagnosticRunner"/> for coordinator tests. It emits one
    /// module at a time, honours the cancellation probe between modules and can be told to
    /// block on a gate so a test can observe an in-flight run. It uses only real production
    /// types (no mock framework).
    /// </summary>
    public sealed class FakeDiagnosticRunner : IDiagnosticRunner
    {
        /// <summary>Set to make the runner wait on this gate before finishing each module.</summary>
        public ManualResetEventSlim ModuleGate { get; set; }

        /// <summary>Set to make the runner throw from Run (to exercise the failure path).</summary>
        public bool ThrowOnRun { get; set; }

        /// <summary>Modules the runner actually executed, in order.</summary>
        public List<string> ExecutedModules { get; private set; }

        /// <summary>Number of concurrent Run calls observed (a run-again-while-running bug would exceed 1).</summary>
        public int MaxConcurrentRuns { get; private set; }

        private int _active;

        public FakeDiagnosticRunner()
        {
            ExecutedModules = new List<string>();
        }

        public DiagnosticRunResult Run(DiagnosticRunContext context)
        {
            var active = Interlocked.Increment(ref _active);
            if (active > MaxConcurrentRuns)
            {
                MaxConcurrentRuns = active;
            }

            try
            {
                if (ThrowOnRun)
                {
                    throw new InvalidOperationException("runner failure (test)");
                }

                var report = new DiagnosticReport
                {
                    SessionId = "test-session",
                    CorrelationId = "test-correlation",
                    GeneratedAtUtc = DateTime.UtcNow
                };

                foreach (var moduleId in context.ModuleIds)
                {
                    if (context.IsCancelled())
                    {
                        break;
                    }

                    context.ModuleStarted(moduleId);
                    var result = DiagnosticResult
                        .Pass(moduleId, moduleId + ".CHECK", "test-correlation", DateTime.UtcNow)
                        .WithFindingId(moduleId + ".CHECK");
                    report.Add(result);
                    report.AddFinding(new DiagnosticFinding
                    {
                        CheckId = result.CheckId,
                        ModuleId = moduleId,
                        FindingId = result.FindingId,
                        Status = DiagnosticStatus.PASS,
                        Severity = DiagnosticSeverity.Info,
                        Observation = "ok",
                        Source = "test",
                        Method = "test",
                        Limitation = "none",
                        NextAction = "none"
                    });

                    lock (ExecutedModules)
                    {
                        ExecutedModules.Add(moduleId);
                    }

                    context.ModuleCompleted(new ModuleStatusView
                    {
                        ModuleId = moduleId,
                        Title = moduleId,
                        Status = DiagnosticStatus.PASS,
                        CheckCount = 1,
                        Executed = true
                    });

                    if (ModuleGate != null)
                    {
                        ModuleGate.Wait(TimeSpan.FromSeconds(5));
                    }
                }

                return new DiagnosticRunResult
                {
                    Mode = context.Mode,
                    ModeId = DiagnosticModeCatalog.For(context.Mode).Id,
                    SessionId = "test-session",
                    CorrelationId = "test-correlation",
                    StartedAtUtc = DateTime.UtcNow,
                    FinishedAtUtc = DateTime.UtcNow,
                    FinalState = context.IsCancelled() ? RunState.Cancelled : RunState.Completed,
                    Report = report,
                    Modules = new List<ModuleStatusView>()
                };
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }
}
