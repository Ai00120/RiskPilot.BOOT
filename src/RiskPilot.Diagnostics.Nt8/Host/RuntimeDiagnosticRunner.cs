using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
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
    /// <para>
    /// A Full pass waits (bounded) for the host readiness facts to settle before it evaluates, so a
    /// pass started immediately after <c>Active</c> does not finish before the selected instrument
    /// and the market-data streams are ready. Quick keeps its previous immediate semantics, and
    /// Post-Trade Audit reads only already-recorded data, so neither waits. Waiting never changes a
    /// verdict: it only delays the pass, and a FAIL observed before or after the wait stays FAIL.
    /// </para>
    /// </summary>
    public sealed class RuntimeDiagnosticRunner : IDiagnosticRunner
    {
        /// <summary>Default upper bound on the Full-mode readiness wait (~20 s).</summary>
        public const int DefaultReadinessTimeoutMs = 20000;

        /// <summary>Default readiness poll interval for the Full-mode wait.</summary>
        public const int DefaultReadinessPollIntervalMs = 100;

        private readonly DiagnosticRuntime _runtime;
        private readonly IEvidenceSink _evidenceSink;
        private readonly Func<bool> _ensureMarketDataCapture;
        private readonly Func<int> _captureStrategyObservations;
        private readonly Func<HostReadinessObservation> _readinessProbe;
        private readonly int _readinessTimeoutMs;
        private readonly int _readinessPollIntervalMs;
        private readonly Action<int> _sleep;

        public RuntimeDiagnosticRunner(DiagnosticRuntime runtime, IEvidenceSink evidenceSink)
            : this(runtime, evidenceSink, null, null)
        {
        }

        /// <summary>
        /// Creates the runner. <paramref name="ensureMarketDataCapture"/>, when supplied, is
        /// invoked (best-effort) just before a pass runs so the host Level I/II capture is
        /// subscribed to the currently-loaded instrument; a failure never aborts the run.
        /// <para>
        /// <paramref name="captureStrategyObservations"/>, when supplied, is invoked
        /// (best-effort) just before a pass runs so the read-only strategy observations are
        /// ingested before M7 evaluates. It returns the number ingested. A failure never aborts
        /// the run and leaves M7 INCONCLUSIVE.
        /// </para>
        /// </summary>
        public RuntimeDiagnosticRunner(DiagnosticRuntime runtime, IEvidenceSink evidenceSink, Func<bool> ensureMarketDataCapture)
            : this(runtime, evidenceSink, ensureMarketDataCapture, null)
        {
        }

        /// <summary>
        /// Creates the runner with both optional host captures. See
        /// <see cref="RuntimeDiagnosticRunner(DiagnosticRuntime, IEvidenceSink, Func{bool})"/>.
        /// <paramref name="readinessProbe"/> is evaluated on each poll of the bounded Full-mode
        /// readiness wait; a null probe skips the wait, and a throwing probe stops it (the pass
        /// then runs immediately and the probes fail closed as usual).
        /// </summary>
        public RuntimeDiagnosticRunner(
            DiagnosticRuntime runtime,
            IEvidenceSink evidenceSink,
            Func<bool> ensureMarketDataCapture,
            Func<int> captureStrategyObservations,
            Func<HostReadinessObservation> readinessProbe = null,
            int readinessTimeoutMs = DefaultReadinessTimeoutMs,
            int readinessPollIntervalMs = DefaultReadinessPollIntervalMs,
            Action<int> sleep = null)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _evidenceSink = evidenceSink;
            _ensureMarketDataCapture = ensureMarketDataCapture;
            _captureStrategyObservations = captureStrategyObservations;
            _readinessProbe = readinessProbe;
            _readinessTimeoutMs = readinessTimeoutMs;
            _readinessPollIntervalMs = readinessPollIntervalMs;
            _sleep = sleep;
        }

        public DiagnosticRunResult Run(DiagnosticRunContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // Subscribe the market-data capture to the currently-loaded instrument before the
            // pass, so the counters M5 reads reflect real host data. A failure here is not fatal:
            // the capture simply stays unsubscribed and M5 reports the surface as unobserved.
            if (_ensureMarketDataCapture != null)
            {
                try
                {
                    _ensureMarketDataCapture();
                }
                catch (Exception)
                {
                    // Best effort only; the run proceeds and M5 fails closed to INCONCLUSIVE.
                }
            }

            // A Full pass waits (bounded) for the readiness facts to settle, so it does not finish
            // before the selected instrument and the market-data streams are ready. Quick keeps its
            // immediate semantics; Post-Trade Audit reads only recorded data and does not wait.
            if (context.Mode == DiagnosticMode.Full)
            {
                WaitForFullReadiness(context.IsCancelled);

                // Re-attempt the (idempotent) subscription after the wait, so a stream that became
                // available during the wait is attached before the modules evaluate.
                if (_ensureMarketDataCapture != null)
                {
                    try
                    {
                        _ensureMarketDataCapture();
                    }
                    catch (Exception)
                    {
                        // Best effort only; M5 fails closed to INCONCLUSIVE.
                    }
                }
            }

            // Pull the read-only strategy observations before the pass, so M7 reflects real
            // host/diagnostic-contract strategy state. A failure is not fatal: the capture simply
            // yields nothing and M7 reports INCONCLUSIVE rather than a fabricated state.
            if (_captureStrategyObservations != null)
            {
                try
                {
                    var captured = _captureStrategyObservations();
                    if (captured > 0)
                    {
                        // The capture enqueues records; the drain thread is what folds them into the
                        // M7 observation. Wait (bounded) so M7 evaluates the state those records
                        // produced, not a stale or empty one.
                        _runtime.WaitForIngestDrain(500);
                    }
                }
                catch (Exception)
                {
                    // Best effort only; M7 fails closed to INCONCLUSIVE.
                }
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

        /// <summary>
        /// Polls the supplied readiness probe until the host is fully ready, the AddOn is shutting
        /// down, the run is cancelled, or the bounded timeout elapses. It never throws and never
        /// changes a diagnostic result: when the budget is exhausted the pass runs immediately with
        /// whatever the probes observe, so an unready host still fails closed.
        /// </summary>
        private void WaitForFullReadiness(Func<bool> isCancelled)
        {
            if (_readinessProbe == null || _readinessTimeoutMs <= 0)
            {
                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                if (isCancelled != null && isCancelled())
                {
                    return;
                }

                HostReadinessObservation observation;
                try
                {
                    observation = _readinessProbe();
                }
                catch (Exception)
                {
                    // An unreadable readiness surface is not a reason to block the pass; the
                    // modules still evaluate and report INCONCLUSIVE/FAIL as their evidence dictates.
                    return;
                }

                if (observation == null || observation.FullyReady || observation.ShuttingDown)
                {
                    return;
                }

                var remaining = _readinessTimeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remaining <= 0)
                {
                    return;
                }

                var delay = _readinessPollIntervalMs > 0
                    ? Math.Min(_readinessPollIntervalMs, remaining)
                    : remaining;

                try
                {
                    var sleep = _sleep;
                    if (sleep != null)
                    {
                        sleep(delay);
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(delay);
                    }
                }
                catch (Exception)
                {
                    return;
                }
            }
        }
    }
}
