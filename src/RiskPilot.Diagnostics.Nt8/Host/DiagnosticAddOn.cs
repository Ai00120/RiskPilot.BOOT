using System;
using NinjaTrader.NinjaScript;
using RiskPilot.Diagnostics.Nt8.Core.Clock;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Logging;
using RiskPilot.Diagnostics.Nt8.Core.Run;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// NinjaTrader 8 AddOn entry point for the RiskPilot NT8 Diagnostic Center.
    /// <para>
    /// This is a read-only, non-trading AddOn. It never submits, changes or cancels an
    /// order, never mutates an account, connection, position or RiskPilot setting, and
    /// never uses reflection. Trading remains disabled by design; there is no code path
    /// that enables it.
    /// </para>
    /// Lifecycle: SetDefaults -> Active (Initialize + Start) -> Terminated (Stop + Dispose).
    /// Every transition is guarded exactly-once so a repeated state change is harmless.
    /// </summary>
    public sealed partial class DiagnosticAddOn : AddOnBase
    {
        private readonly object _gate = new object();
        private readonly System.Collections.Generic.List<string> _lifecycleStates = new System.Collections.Generic.List<string>();
        private readonly SwitchableUiDispatcher _uiDispatcher = new SwitchableUiDispatcher();
        private DiagnosticRuntime _runtime;
        private DiagnosticViewState _viewState;
        private DiagnosticRunCoordinator _coordinator;
        private bool _started;
        private bool _disposed;

        /// <summary>Always true: this tool never enables trading.</summary>
        public const bool TradingDisabled = true;

        protected override void OnStateChange()
        {
            lock (_gate)
            {
                _lifecycleStates.Add(State.ToString());
            }

            switch (State)
            {
                case State.SetDefaults:
                    Name = BuildIdentity.ProductName;
                    Description = "Read-only diagnostics and host acceptance for NinjaTrader 8. Trading=Disabled.";
                    break;

                case State.Active:
                    SafeInitialize();
                    SafeStart();
                    break;

                case State.Terminated:
                    SafeStop();
                    SafeDispose();
                    break;
            }
        }

        /// <summary>Lifecycle states observed so far, in order. Used by M1.</summary>
        public System.Collections.Generic.IReadOnlyList<string> LifecycleStates
        {
            get { lock (_gate) { return new System.Collections.Generic.List<string>(_lifecycleStates); } }
        }

        /// <summary>
        /// Root directory for evidence packages. It defaults to a folder under the operator's
        /// Documents directory, which is outside the Git working tree (per governance). A host
        /// or a test may override it before the AddOn initialises.
        /// </summary>
        public string EvidenceOutputRoot { get; set; }

        /// <summary>
        /// Live readiness probe for the host data provider. True once the AddOn has advanced past
        /// <c>State.Active</c> into a state in which instrument master metadata is readable. It is
        /// evaluated on each read, so a run started after the host reaches a data state observes the
        /// metadata; a run started earlier stays INCONCLUSIVE (fail closed) rather than guessing.
        /// </summary>
        private bool HostIsLive
        {
            get
            {
                var state = State;
                return state == State.DataLoaded
                    || state == State.Historical
                    || state == State.Transition
                    || state == State.Realtime;
            }
        }

        /// <summary>
        /// Creates the runtime and registers probes. Guarded so repeated calls are no-ops.
        /// </summary>
        public void SafeInitialize()
        {
            lock (_gate)
            {
                if (_disposed || _runtime != null)
                {
                    return;
                }

                try
                {
                    // Readiness is supplied as a live probe, not a captured value: the provider is
                    // constructed here at State.Active, but MasterInstrument.* is only safe to read
                    // once the host has advanced to a live/data state. A captured value would stay
                    // false for the whole session and leave M4 permanently INCONCLUSIVE.
                    var provider = new Nt8HostDataProvider(() => HostIsLive);
                    _runtime = new DiagnosticRuntime(provider, SystemClock.Instance, 4096, 8192);
                    _runtime.Initialize();

                    // The run coordinator owns the single-run guarantee and marshals its UI
                    // notifications through the switchable dispatcher. The dispatcher has no
                    // target until a host window exists; until then it applies notifications
                    // inline to the view state, so a run's progress and result are never lost.
                    _viewState = new DiagnosticViewState();
                    var runner = new RuntimeDiagnosticRunner(_runtime, BuildEvidenceSink());
                    _coordinator = new DiagnosticRunCoordinator(runner, _uiDispatcher, _runtime.Log);
                    WireViewState();

                    Log("RiskPilot Diagnostics initialized. Trading=Disabled.", NinjaTrader.Cbi.LogLevel.Information);
                }
                catch (Exception ex)
                {
                    _runtime = null;
                    _coordinator = null;
                    _viewState = null;
                    Log("RiskPilot Diagnostics initialization failed: " + Core.Errors.ErrorClassifier.Describe(ex),
                        NinjaTrader.Cbi.LogLevel.Error);
                }
            }
        }

        /// <summary>
        /// Connects the run coordinator's progress and completion notifications to the view
        /// state. Without this the window would never see a run's module grid, progress or
        /// result. The view state raises its own <c>Changed</c> event, which the window
        /// marshals onto the UI thread for rendering.
        /// </summary>
        private void WireViewState()
        {
            var viewState = _viewState;
            var coordinator = _coordinator;
            if (viewState == null || coordinator == null)
            {
                return;
            }

            coordinator.ProgressChanged += viewState.ApplyProgress;
            coordinator.RunCompleted += result => viewState.ApplyResult(result, Core.Identity.BuildIdentity.IsStubBuild);
        }

        private IEvidenceSink BuildEvidenceSink()
        {
            var root = EvidenceOutputRoot;
            if (string.IsNullOrEmpty(root))
            {
                root = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "RiskPilot.Diagnostics.Evidence");
            }

            return new EvidenceDirectorySink(root);
        }

        /// <summary>Starts the background drain worker. Guarded exactly-once.</summary>
        public void SafeStart()
        {
            lock (_gate)
            {
                if (_disposed || _runtime == null || _started)
                {
                    return;
                }

                try
                {
                    _runtime.Start();
                    _started = true;
                    Log("RiskPilot Diagnostics started.", NinjaTrader.Cbi.LogLevel.Information);
                }
                catch (Exception ex)
                {
                    Log("RiskPilot Diagnostics start failed: " + Core.Errors.ErrorClassifier.Describe(ex),
                        NinjaTrader.Cbi.LogLevel.Error);
                }
            }
        }

        /// <summary>Stops the drain worker. Safe to call when not started.</summary>
        public void SafeStop()
        {
            DiagnosticRuntime runtime;
            lock (_gate)
            {
                runtime = _runtime;
                _started = false;
            }

            if (runtime == null)
            {
                return;
            }

            try
            {
                runtime.Stop();
            }
            catch (Exception ex)
            {
                Log("RiskPilot Diagnostics stop failed: " + Core.Errors.ErrorClassifier.Describe(ex),
                    NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        /// <summary>Releases the runtime. Guarded exactly-once and safe after a failed init.</summary>
        public void SafeDispose()
        {
            DiagnosticRuntime runtime;
            DiagnosticRunCoordinator coordinator;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                runtime = _runtime;
                coordinator = _coordinator;
                _runtime = null;
                _coordinator = null;
            }

            // Cancel and join any active run before tearing down the runtime, so a run in
            // flight cannot observe a disposed runtime.
            if (coordinator != null)
            {
                try
                {
                    coordinator.Dispose();
                }
                catch (Exception ex)
                {
                    Log("RiskPilot Diagnostics coordinator dispose failed: " + Core.Errors.ErrorClassifier.Describe(ex),
                        NinjaTrader.Cbi.LogLevel.Error);
                }
            }

            // Detach and close the UI layer (implemented only in the real-host build). This
            // runs after the coordinator is stopped, so no notification can race the teardown.
            try
            {
                ShutdownUiLayer();
            }
            catch (Exception ex)
            {
                Log("RiskPilot Diagnostics UI shutdown failed: " + Core.Errors.ErrorClassifier.Describe(ex),
                    NinjaTrader.Cbi.LogLevel.Error);
            }

            if (runtime == null)
            {
                return;
            }

            try
            {
                runtime.Dispose();
            }
            catch (Exception ex)
            {
                Log("RiskPilot Diagnostics dispose failed: " + Core.Errors.ErrorClassifier.Describe(ex),
                    NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        /// <summary>
        /// Enqueues a normalized record from a host callback. Non-blocking by design so
        /// market-data callbacks never stall. Returns false when dropped or suppressed.
        /// </summary>
        public bool Ingest(Core.Models.NormalizedRecord record)
        {
            DiagnosticRuntime runtime;
            lock (_gate)
            {
                runtime = _runtime;
            }

            if (runtime == null)
            {
                return false;
            }

            return runtime.Ingest(record);
        }

        /// <summary>Runs a diagnostic pass and writes the JSON report plus manifest.</summary>
        public string RunDiagnosticPass(IReportSink sink)
        {
            DiagnosticRuntime runtime;
            lock (_gate)
            {
                runtime = _runtime;
            }

            if (runtime == null)
            {
                return null;
            }

            return runtime.WriteReport(sink);
        }

        public bool IsDisposed
        {
            get { lock (_gate) { return _disposed; } }
        }

        public bool IsStarted
        {
            get { lock (_gate) { return _started; } }
        }

        /// <summary>The runtime, or null before initialization. Exposed for the window layer.</summary>
        public DiagnosticRuntime Runtime
        {
            get { lock (_gate) { return _runtime; } }
        }

        /// <summary>The observable window state, or null before initialization.</summary>
        public DiagnosticViewState ViewState
        {
            get { lock (_gate) { return _viewState; } }
        }

        /// <summary>The run coordinator, or null before initialization.</summary>
        public DiagnosticRunCoordinator Coordinator
        {
            get { lock (_gate) { return _coordinator; } }
        }

        /// <summary>The evidence sink used for exported packages.</summary>
        public IEvidenceSink EvidenceSink
        {
            get { return BuildEvidenceSink(); }
        }

        /// <summary>
        /// Shuts the window/menu layer down. Declared here as a no-op so the stub build (which
        /// has no WPF) compiles; the real-host partial overrides it with the actual teardown.
        /// </summary>
        partial void ShutdownUiLayer();
    }
}
