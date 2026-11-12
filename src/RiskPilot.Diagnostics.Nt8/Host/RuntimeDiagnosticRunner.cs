using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Runs a diagnostic mode over the <see cref="DiagnosticRuntime"/> and, when a sink is
    /// supplied, writes the complete evidence package for the run. It is the bridge between
    /// the host-free run coordinator and the runtime.
    /// <para>
    /// The runner runs on the coordinator's worker thread. It reports per-module progress
    /// through the context callbacks (which the coordinator marshals to the UI) and, on
    /// completion, produces a <see cref="DiagnosticRunResult"/> carrying the report and the
    /// per-module status grid. It never throws to signal a normal cancellation.
    /// </para>
    /// </summary>
    public sealed class RuntimeDiagnosticRunner : IDiagnosticRunner
    {
        private readonly DiagnosticRuntime _runtime;
        private readonly IEvidenceSink _evidenceSink;

        public RuntimeDiagnosticRunner(DiagnosticRuntime runtime, IEvidenceSink evidenceSink)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _evidenceSink = evidenceSink;
        }

        public DiagnosticRunResult Run(DiagnosticRunContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var result = new DiagnosticRunResult
            {
                Mode = context.Mode,
                ModeId = DiagnosticModeCatalog.For(context.Mode).Id,
                StartedAtUtc = DateTime.UtcNow
            };

            // Pre-seed the grid so the UI shows every module the mode selected, even before
            // it runs, and so a cancelled run still reports the modules it reached.
            var grid = new Dictionary<string, ModuleStatusView>(StringComparer.OrdinalIgnoreCase);
            foreach (var moduleId in context.ModuleIds)
            {
                grid[moduleId] = new ModuleStatusView
                {
                    ModuleId = moduleId,
                    Title = DiagnosticModuleCatalog.TitleFor(moduleId),
                    Status = DiagnosticStatus.NOT_APPLICABLE,
                    Executed = false
                };
            }

            var cancelled = false;
            var report = _runtime.RunDiagnosticPass(
                context.ModuleIds,
                moduleId => context.ModuleStarted(moduleId),
                (moduleId, results) =>
                {
                    if (!grid.TryGetValue(moduleId, out var view))
                    {
                        view = new ModuleStatusView { ModuleId = moduleId, Title = DiagnosticModuleCatalog.TitleFor(moduleId) };
                        grid[moduleId] = view;
                    }

                    var status = DiagnosticStatus.NOT_APPLICABLE;
                    var checkCount = 0;
                    if (results != null)
                    {
                        foreach (var r in results)
                        {
                            status = status.Worst(r.Status);
                            checkCount++;
                        }
                    }

                    view.Status = status;
                    view.CheckCount = checkCount;
                    view.Executed = checkCount > 0;
                    context.ModuleCompleted(view);
                },
                context.IsCancelled);

            result.Report = report;
            result.SessionId = report != null ? report.SessionId : string.Empty;
            result.CorrelationId = report != null ? report.CorrelationId : string.Empty;

            // If the coordinator asked to cancel, stop before exporting evidence: a partial
            // package must not be presented as a complete run.
            cancelled = context.IsCancelled();
            result.Modules = new List<ModuleStatusView>(grid.Values);
            result.FinishedAtUtc = DateTime.UtcNow;
            result.FinalState = cancelled ? RunState.Cancelled : RunState.Completed;

            if (!cancelled && _evidenceSink != null)
            {
                _runtime.WriteEvidencePackage(result, _evidenceSink, out var directory);
                result.EvidenceDirectory = directory;
            }

            return result;
        }
    }
}
