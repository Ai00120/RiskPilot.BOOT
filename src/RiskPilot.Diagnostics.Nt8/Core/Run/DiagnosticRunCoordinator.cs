using System;
using System.Collections.Generic;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Logging;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// Owns the lifecycle of a diagnostic run. It guarantees:
    /// <list type="bullet">
    /// <item>at most one run is active at a time (concurrent starts are rejected);</item>
    /// <item>the run executes on a dedicated background thread, never on the caller's
    /// thread, so a UI thread or a NinjaTrader callback can never be blocked;</item>
    /// <item>a cancel request is honoured between modules and produces a Cancelled run,
    /// never a Completed one;</item>
    /// <item>every UI notification is marshalled through <see cref="IUiDispatcher"/> using
    /// asynchronous posting only.</item>
    /// </list>
    /// The coordinator holds no UI or NinjaTrader type, so its guarantees are unit-testable
    /// with a synchronous dispatcher double.
    /// </summary>
    public sealed class DiagnosticRunCoordinator : IDisposable
    {
        private readonly IDiagnosticRunner _runner;
        private readonly IUiDispatcher _dispatcher;
        private readonly DiagnosticLog _log;
        private readonly RunStateMachine _state = new RunStateMachine();

        private readonly object _gate = new object();
        private Thread _worker;
        private volatile bool _cancelRequested;
        private volatile DiagnosticRunProgress _progress = DiagnosticRunProgress.Idle;
        private volatile DiagnosticRunResult _result;
        private volatile bool _disposed;

        /// <summary>Raised (on the UI dispatcher) whenever progress changes.</summary>
        public event Action<DiagnosticRunProgress> ProgressChanged;

        /// <summary>Raised (on the UI dispatcher) once a run reaches a terminal state.</summary>
        public event Action<DiagnosticRunResult> RunCompleted;

        public DiagnosticRunCoordinator(IDiagnosticRunner runner, IUiDispatcher dispatcher, DiagnosticLog log)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _dispatcher = dispatcher;
            _log = log;
        }

        public RunState State { get { return _state.State; } }

        public bool IsRunning { get { return _state.IsActive; } }

        public DiagnosticRunProgress Progress { get { return _progress; } }

        public DiagnosticRunResult Result { get { return _result; } }

        /// <summary>
        /// Starts a run for <paramref name="mode"/>. Returns false when a run is already
        /// active or the coordinator has been disposed — the caller must treat false as
        /// "a run is already in progress", not as an error to retry.
        /// </summary>
        public bool Start(DiagnosticMode mode)
        {
            if (_disposed)
            {
                return false;
            }

            if (!_state.TryStart())
            {
                Log("Run start rejected: a run is already active.");
                return false;
            }

            _cancelRequested = false;
            _result = null;
            var definition = DiagnosticModeCatalog.For(mode);
            var startedAt = DateTime.UtcNow;
            Publish(new DiagnosticRunProgress(RunState.Running, definition.Id, string.Empty, 0, definition.ModuleIds.Count, startedAt, null));

            var thread = new Thread(() => RunWorker(mode, definition, startedAt))
            {
                IsBackground = true,
                Name = "RiskPilot.Diagnostics.Run"
            };

            lock (_gate)
            {
                _worker = thread;
            }

            thread.Start();
            return true;
        }

        /// <summary>
        /// Requests cancellation of the active run. Returns true when a running run was
        /// signalled; returns false when no run is active. Cancellation never throws and
        /// never blocks the caller.
        /// </summary>
        public bool Cancel()
        {
            if (!_state.TryRequestCancel())
            {
                return false;
            }

            _cancelRequested = true;
            var current = _progress;
            Publish(new DiagnosticRunProgress(RunState.Cancelling, current.ModeId, current.CurrentModuleId,
                current.CompletedModules, current.TotalModules, current.StartedAtUtc, null));
            Log("Run cancellation requested.");
            return true;
        }

        /// <summary>
        /// Waits for the active run to finish, bounded by <paramref name="timeout"/>. Used by
        /// tests and by shutdown; the UI never blocks on this.
        /// </summary>
        public bool WaitForCompletion(TimeSpan timeout)
        {
            Thread worker;
            lock (_gate)
            {
                worker = _worker;
            }

            if (worker == null)
            {
                return true;
            }

            return worker.Join(timeout);
        }

        private void RunWorker(DiagnosticMode mode, DiagnosticModeDefinition definition, DateTime startedAt)
        {
            var completed = 0;
            var modules = new List<ModuleStatusView>();

            DiagnosticRunContext context = null;
            try
            {
                context = new DiagnosticRunContext(
                    mode,
                    definition.ModuleIds,
                    () => _cancelRequested,
                    moduleId =>
                    {
                        var current = _progress;
                        Publish(new DiagnosticRunProgress(_cancelRequested ? RunState.Cancelling : RunState.Running,
                            definition.Id, moduleId, completed, definition.ModuleIds.Count, startedAt, null));
                    },
                    view =>
                    {
                        modules.Add(view);
                        completed++;
                        var current = _progress;
                        Publish(new DiagnosticRunProgress(_cancelRequested ? RunState.Cancelling : RunState.Running,
                            definition.Id, view.ModuleId, completed, definition.ModuleIds.Count, startedAt, null));
                    },
                    (sessionId, correlationId) => { /* captured through the result below */ });

                var result = _runner.Run(context);
                var finishedAt = DateTime.UtcNow;
                result.Mode = mode;
                result.ModeId = definition.Id;
                result.StartedAtUtc = startedAt;
                result.FinishedAtUtc = finishedAt;
                result.Modules = modules;
                result.EvidenceDirectory = result.EvidenceDirectory ?? string.Empty;

                var cancelled = _cancelRequested;
                if (cancelled)
                {
                    _state.TryCompleteCancelled();
                    result.FinalState = RunState.Cancelled;
                }
                else
                {
                    _state.TryComplete();
                    result.FinalState = RunState.Completed;
                }

                _result = result;
                Publish(new DiagnosticRunProgress(result.FinalState, definition.Id, string.Empty,
                    completed, definition.ModuleIds.Count, startedAt, finishedAt));
                Notify(() => RunCompleted?.Invoke(result));
                Log("Run finished. state=" + result.FinalState + " modules=" + completed);
            }
            catch (Exception ex)
            {
                var failedAt = DateTime.UtcNow;
                _state.TryFail();
                var failure = new DiagnosticRunResult
                {
                    Mode = mode,
                    ModeId = definition.Id,
                    StartedAtUtc = startedAt,
                    FinishedAtUtc = failedAt,
                    FinalState = RunState.Failed,
                    Modules = modules
                };

                _result = failure;
                Publish(new DiagnosticRunProgress(RunState.Failed, definition.Id, string.Empty,
                    completed, definition.ModuleIds.Count, startedAt, failedAt));
                Notify(() => RunCompleted?.Invoke(failure));
                Log("Run failed: " + Core.Errors.ErrorClassifier.Describe(ex));
            }
            finally
            {
                lock (_gate)
                {
                    _worker = null;
                }
            }
        }

        private void Publish(DiagnosticRunProgress progress)
        {
            _progress = progress;
            Notify(() => ProgressChanged?.Invoke(progress));
        }

        /// <summary>
        /// Marshals a UI notification onto the dispatcher. When the dispatcher is
        /// unavailable or already shutting down, the notification is dropped rather than
        /// thrown — a shutting-down host must never fault the run worker.
        /// </summary>
        private void Notify(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (_dispatcher == null)
            {
                action();
                return;
            }

            if (_dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (_dispatcher.OnDispatcherThread)
            {
                action();
                return;
            }

            _dispatcher.Post(action);
        }

        private void Log(string message)
        {
            if (_log != null)
            {
                _log.Write(DateTime.UtcNow, LogLevel.Information, string.Empty, string.Empty, "RunCoordinator: " + message);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancelRequested = true;

            Thread worker;
            lock (_gate)
            {
                worker = _worker;
            }

            if (worker != null && worker.IsAlive)
            {
                worker.Join(TimeSpan.FromSeconds(5));
            }

            _state.Reset();
        }
    }
}
