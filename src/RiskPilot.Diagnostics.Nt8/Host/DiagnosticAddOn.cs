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
    public sealed partial class DiagnosticAddOn : AddOnBase, Core.Run.IInstrumentSelectionController
    {
        private readonly object _gate = new object();
        private readonly System.Collections.Generic.List<string> _lifecycleStates = new System.Collections.Generic.List<string>();
        private readonly SwitchableUiDispatcher _uiDispatcher = new SwitchableUiDispatcher();
        private readonly Core.Run.HostReadinessTracker _readiness = new Core.Run.HostReadinessTracker();
        private readonly Core.Instruments.TargetInstrumentSelection _instrumentSelection = new Core.Instruments.TargetInstrumentSelection();
        private DiagnosticRuntime _runtime;
        private Nt8HostDataProvider _provider;
        private DiagnosticViewState _viewState;
        private DiagnosticRunCoordinator _coordinator;
        private MarketDataCapture _marketDataCapture;
        private StrategyDiagnosticCapture _strategyCapture;
        private System.Threading.Timer _readinessTimer;
        private int _readinessAttempts;
        private volatile bool _hostReachable;
        private bool _started;
        private bool _disposed;

        /// <summary>Always true: this tool never enables trading.</summary>
        public const bool TradingDisabled = true;

        protected override void OnStateChange()
        {
            ApplyLifecycleState(State.ToString());
        }

        /// <summary>
        /// Applies a lifecycle transition. <see cref="OnStateChange"/> delegates here with the raw
        /// <c>State</c> member, and tests drive it directly because the offline stub surface cannot
        /// raise the host's state-change callback. It is the single place that records the state for
        /// the readiness model and reacts to it, so the AddOn lifecycle is exercised over the stub
        /// surface without a host.
        /// <para>
        /// An AddOn uses the NinjaTrader "Active state" system: it reaches <c>Active</c> (operational)
        /// and never the data-processing states. The reaction is guarded exactly-once downstream
        /// (<see cref="SafeInitialize"/>/<see cref="SafeStart"/>/<see cref="SafeDispose"/>), so a
        /// repeated or out-of-order transition is harmless.
        /// </para>
        /// </summary>
        public void ApplyLifecycleState(string state)
        {
            if (string.IsNullOrEmpty(state))
            {
                return;
            }

            lock (_gate)
            {
                _lifecycleStates.Add(state);
            }

            // Record the raw state for the separated readiness model and M1's lifecycle surface.
            _readiness.RecordAddOnState(state);

            switch (state)
            {
                case "SetDefaults":
                    Name = BuildIdentity.ProductName;
                    Description = "Read-only diagnostics and host acceptance for NinjaTrader 8. Trading=Disabled.";
                    break;

                case "Configure":
                    // The AddOn is configured but not yet operational. Readiness stays
                    // ADDON_NOT_OPERATIONAL (never ready) until Active is reached.
                    break;

                case "Active":
                    SafeInitialize();
                    SafeStart();
                    // The host object model is reachable from Active onward. Record it and begin the
                    // bounded readiness loop, because the instrument/market-data surface can appear
                    // after Active (the host loads instruments asynchronously). The loop is
                    // idempotent and stops once fully ready or the budget is exhausted.
                    _readiness.RecordHostAvailable(true);
                    _hostReachable = true;
                    StartReadinessLoop();
                    break;

                case "Terminated":
                    StopReadinessLoop();
                    _readiness.RecordHostAvailable(false);
                    _hostReachable = false;
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
        /// Optional read-only diagnostic source for strategy observations. No public NinjaTrader
        /// API exposes strategy internals, so M7 can only report real strategy state when a host or
        /// RiskPilot component supplies a read-only diagnostic source. A host or test may set this
        /// before the AddOn initialises. When it is null, M7 reports INCONCLUSIVE (never a
        /// fabricated state).
        /// </summary>
        public Core.Contract.IReadOnlyDiagnosticSource StrategyDiagnosticSource { get; set; }

        /// <summary>
        /// The explicit target instrument identity the diagnostic is scoped to (for example
        /// <c>"ES 12-26"</c>). This is a thin proxy over <see cref="InstrumentSelection"/>, the
        /// single source of truth the operator's Diagnostic Center picker writes and the host
        /// provider and market-data capture read, so every component targets the same contract.
        /// <para>
        /// This is the only way the target is chosen: the tool never auto-discovers an instrument
        /// from the loaded surface, never picks the first one and never selects by an ES root/name
        /// substring. When it is null/empty the target stays unresolved and M4 reports
        /// <c>TARGET_INSTRUMENT_NOT_CONFIGURED</c> (INCONCLUSIVE, never PASS). Setting it never
        /// mutates the host selection; it only tells the read-only diagnostic which instrument to
        /// observe.
        /// </para>
        /// </summary>
        public string RequestedInstrumentId
        {
            get { return _instrumentSelection.SelectedInstrumentId; }
            set { _instrumentSelection.Select(value); }
        }

        /// <summary>
        /// The observable operator target-instrument selection model. The Diagnostic Center renders
        /// its available list and current selection, and writes the operator's explicit choice here;
        /// the host provider and the market-data capture read the same model through a live probe,
        /// so there is no second selection source of truth. The model never selects an instrument on
        /// its own (no implicit first/nearest-expiry pick), and a refresh never changes the current
        /// selection.
        /// </summary>
        public Core.Instruments.TargetInstrumentSelection InstrumentSelection
        {
            get { return _instrumentSelection; }
        }

        /// <summary>
        /// Live readiness probe for the host data provider. True once the AddOn has reached an
        /// operational lifecycle state in which the NinjaTrader object model is reachable.
        /// <para>
        /// This is the corrected predicate. An AddOn uses the NinjaTrader <b>"Active state"</b>
        /// management system: it progresses <c>SetDefaults -&gt; Configure -&gt; Active -&gt; Terminated</c>
        /// and never reaches the data-processing states <c>DataLoaded</c>/<c>Historical</c>/
        /// <c>Transition</c>/<c>Realtime</c> (those belong to Indicators/Strategies). The previous
        /// probe accepted only the data-processing states, so it was permanently false for an AddOn
        /// and no host metadata, instrument or market data could ever be observed.
        /// <see cref="Core.Identity.AddOnLifecycle.IsOperational"/> accepts the AddOn's own
        /// <c>Active</c> state (and still accepts the data-processing states defensively).
        /// </para>
        /// Evaluated on each read, so a run started after the host becomes operational observes the
        /// metadata; a run started earlier stays INCONCLUSIVE (fail closed) rather than guessing.
        /// </summary>
        private bool HostIsLive
        {
            // Read the state the lifecycle callback recorded, so the probe and the readiness model
            // share one source of truth and the lifecycle is drivable over the offline stub surface.
            get { return Core.Identity.AddOnLifecycle.IsOperational(_readiness.AddOnState); }
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
                    // Readiness is supplied as a live probe, not a captured value. The provider is
                    // constructed here at State.Active; MasterInstrument.* is only safe to read once
                    // the host object model is reachable (which it is from Active onward). The probe
                    // reads the separated readiness tracker on each call, so the tool reports the
                    // exact outstanding fact rather than a single conflated Boolean.
                    //
                    // The lifecycle transitions are likewise supplied as a live probe so M1 sees the
                    // states the AddOn has reached by the time each pass runs.
                    var provider = new Nt8HostDataProvider(
                        () => HostIsLive,
                        () => LifecycleStates,
                        () => _readiness.Observe(),
                        () => _instrumentSelection.SelectedInstrumentId);
                    _provider = provider;
                    _runtime = new DiagnosticRuntime(provider, SystemClock.Instance, 4096, 8192);
                    _runtime.Initialize();

                    // The market-data capture subscribes to the loaded ES instrument's public
                    // Level I/II streams and feeds normalized records into the runtime, so M5
                    // reports real host data. It is read-only and is (re)subscribed before each
                    // pass; a failure leaves M5 INCONCLUSIVE rather than fabricating data. The
                    // master-metadata readiness gate is the live host probe, so an AddOn that only
                    // reaches Active is no longer blocked from resolving its instrument.
                    _marketDataCapture = new MarketDataCapture(
                        record => _runtime != null && _runtime.Ingest(record),
                        () => HostIsLive,
                        () => _instrumentSelection.SelectedInstrumentId,
                        _readiness);

                    // The strategy capture pulls read-only strategy observations from the optional
                    // diagnostic contract before each pass. No public API exposes strategy internals,
                    // so with no source M7 reports INCONCLUSIVE rather than a fabricated state. It
                    // is strictly read-only: it never mutates the source or the host.
                    _strategyCapture = new StrategyDiagnosticCapture(
                        record =>
                        {
                            var runtime = _runtime;
                            if (runtime != null)
                            {
                                runtime.Ingest(record);
                            }
                        },
                        StrategyDiagnosticSource);

                    // The run coordinator owns the single-run guarantee and marshals its UI
                    // notifications through the switchable dispatcher. The dispatcher has no
                    // target until a host window exists; until then it applies notifications
                    // inline to the view state, so a run's progress and result are never lost.
                    _viewState = new DiagnosticViewState();
                    var runner = new RuntimeDiagnosticRunner(_runtime, BuildEvidenceSink(), EnsureMarketDataCapture, CaptureStrategyObservations);
                    _coordinator = new DiagnosticRunCoordinator(runner, _uiDispatcher, _runtime.Log);
                    WireViewState();

                    Log("RiskPilot Diagnostics initialized. Trading=Disabled.", NinjaTrader.Cbi.LogLevel.Information);
                }
                catch (Exception ex)
                {
                    _runtime = null;
                    _coordinator = null;
                    _viewState = null;
                    _marketDataCapture = null;
                    _strategyCapture = null;
                    Log("RiskPilot Diagnostics initialization failed: " + Core.Errors.ErrorClassifier.Describe(ex),
                        NinjaTrader.Cbi.LogLevel.Error);
                }
            }
        }

        /// <summary>
        /// Best-effort capture of the read-only strategy observations before a pass. Invoked by
        /// the runner so M7 evaluates the real strategy state the diagnostic source reported. It
        /// never throws and never fabricates data: with no source it reports 0 and M7 stays
        /// INCONCLUSIVE.
        /// </summary>
        public int CaptureStrategyObservations()
        {
            StrategyDiagnosticCapture capture;
            lock (_gate)
            {
                capture = _strategyCapture;
            }

            if (capture == null)
            {
                return 0;
            }

            try
            {
                return capture.TryCapture();
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Best-effort (re)subscription of the market-data capture to the loaded instrument.
        /// Invoked before each diagnostic pass so the Level I/II streams are attached once the
        /// host has loaded the ES instrument. It never throws and never fabricates data: when the
        /// instrument is not loaded yet it simply returns false and M5 stays INCONCLUSIVE.
        /// </summary>
        public bool EnsureMarketDataCapture()
        {
            MarketDataCapture capture;
            lock (_gate)
            {
                capture = _marketDataCapture;
            }

            if (capture == null)
            {
                return false;
            }

            try
            {
                return capture.TrySubscribe();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Reloads the operator's available-instrument list from the live host discovery surface and
        /// returns the resulting resolution of the current selection. The current selection is never
        /// changed: a still-present contract stays resolved and a removed one is reported NotLoaded
        /// (never replaced by another contract). Read-only: it enumerates <c>Instrument.FullName</c>
        /// and never mutates the host. Never throws.
        /// </summary>
        public Core.Instruments.InstrumentSelectionResult RefreshAvailableInstruments()
        {
            System.Collections.Generic.IReadOnlyList<string> names;
            var observable = Nt8HostDataProvider.TryEnumerateLoadedInstrumentNames(out names);
            _instrumentSelection.Refresh(names, observable);

            // The available list changed, so re-evaluate the (idempotent) market-data subscription
            // against the current selection: a still-selected contract stays subscribed, a removed
            // one detaches and fails closed.
            if (observable)
            {
                EnsureMarketDataCapture();
            }

            return _instrumentSelection.Resolution;
        }

        /// <summary>
        /// Applies an explicit operator instrument selection (an exact instrument FullName, or null
        /// to clear). This never mutates the host selection; it only tells the read-only diagnostic
        /// which contract to observe. After the change the (idempotent) market-data subscription is
        /// re-evaluated so the capture follows the operator's choice without leaving a stale
        /// subscription behind. Never throws.
        /// </summary>
        public void SelectInstrument(string fullName)
        {
            if (!_instrumentSelection.Select(fullName))
            {
                return;
            }

            EnsureMarketDataCapture();
        }

        /// <summary>The observable operator selection model (see <see cref="InstrumentSelection"/>).</summary>
        Core.Instruments.TargetInstrumentSelection Core.Run.IInstrumentSelectionController.Selection
        {
            get { return _instrumentSelection; }
        }

        /// <summary>Interface form of <see cref="RefreshAvailableInstruments"/>.</summary>
        Core.Instruments.InstrumentSelectionResult Core.Run.IInstrumentSelectionController.RefreshAvailable()
        {
            return RefreshAvailableInstruments();
        }

        /// <summary>Interface form of <see cref="SelectInstrument"/>.</summary>
        void Core.Run.IInstrumentSelectionController.Select(string fullName)
        {
            SelectInstrument(fullName);
        }

        /// <summary>
        /// The live readiness observation for M1. It reflects the separated readiness facts
        /// (AddOn lifecycle, host availability, metadata readiness, instrument readiness and
        /// market-data subscription). Exposed so the window/tests can read the exact state.
        /// </summary>
        public Core.Models.Observations.HostReadinessObservation Readiness
        {
            get { return _readiness.Observe(); }
        }

        /// <summary>
        /// Whether the background retry timer is armed. Defaults to true (production): the AddOn
        /// keeps polling until the host loads the instrument. A deterministic test sets it to false
        /// before driving the lifecycle so no background thread races the test over the shared host
        /// surface; the first, synchronous probe still runs.
        /// </summary>
        public bool ReadinessRetryEnabled { get; set; } = true;

        /// <summary>Number of readiness probes the background loop has scheduled so far.</summary>
        public int ReadinessAttempts { get { lock (_gate) { return _readinessAttempts; } } }

        /// <summary>
        /// Starts the bounded readiness loop. An AddOn reaches <c>Active</c> before the host has
        /// necessarily loaded the target instrument, so the instrument and market-data surface can
        /// appear later. The loop polls the readiness facts with geometric backoff until the tool is
        /// fully ready or the retry budget is exhausted, then stops on its own. It is idempotent:
        /// a repeat call after a loop is already running (or once ready) is a no-op, so the
        /// market-data streams are never subscribed twice. The timer callback runs on a thread-pool
        /// thread and only ever reads host state and (idempotently) attaches the capture; it never
        /// blocks the host UI thread.
        /// </summary>
        public void StartReadinessLoop()
        {
            lock (_gate)
            {
                if (_disposed || _readinessTimer != null)
                {
                    return;
                }

                _readinessAttempts = 0;

                // Run the first probe synchronously on the calling (host state-change) thread so the
                // facts that are already available at Active are captured immediately and
                // deterministically; only the retries are scheduled on the timer.
                PollReadinessOnce();

                // The retry timer is a production convenience for a host that loads the instrument
                // after Active. It is disabled when ReadinessRetryEnabled is false (the deterministic
                // test seam) and once the tool is already fully ready, so a test never has a
                // background thread racing it over the shared host surface.
                if (!ReadinessRetryEnabled || _readiness.Observe().FullyReady || !_hostReachable)
                {
                    return;
                }

                var policy = Core.Run.HostReadinessRetryPolicy.Default();
                _readinessTimer = new System.Threading.Timer(
                    _ => PollReadiness(policy),
                    null,
                    policy.InitialDelayMs,
                    System.Threading.Timeout.Infinite);
            }
        }

        /// <summary>
        /// Runs a single readiness probe synchronously and returns the resulting observation. It is
        /// idempotent (the underlying subscription is guarded) so repeated calls never create a
        /// duplicate subscription. Exposed so the readiness model is exercisable deterministically
        /// without the background timer.
        /// </summary>
        public Core.Models.Observations.HostReadinessObservation PollReadinessOnce()
        {
            EnsureMarketDataCapture();
            return _readiness.Observe();
        }

        /// <summary>Stops the readiness loop. Safe to call when it was never started.</summary>
        public void StopReadinessLoop()
        {
            System.Threading.Timer timer;
            lock (_gate)
            {
                timer = _readinessTimer;
                _readinessTimer = null;
            }

            if (timer != null)
            {
                try
                {
                    timer.Dispose();
                }
                catch (Exception)
                {
                    // A disposed/faulted timer must never fault the lifecycle callback.
                }
            }
        }

        /// <summary>
        /// One readiness probe: attempts the (idempotent) market-data subscription and, when the
        /// tool is not yet fully ready, schedules the next probe with geometric backoff. It stops
        /// once fully ready, once the retry budget is exhausted, or once the AddOn is no longer
        /// operational/shutting down. It never throws into the timer thread and never fabricates
        /// readiness.
        /// </summary>
        private void PollReadiness(Core.Run.HostReadinessRetryPolicy policy)
        {
            System.Threading.Timer timer;
            try
            {
                lock (_gate)
                {
                    if (_disposed || _readinessTimer == null)
                    {
                        return;
                    }

                    timer = _readinessTimer;
                }

                if (!_hostReachable)
                {
                    // The AddOn left its operational state; stop polling.
                    StopReadinessLoop();
                    return;
                }

                // Attempt the subscription (idempotent). It resolves the instrument and records the
                // instrument/metadata/subscription facts on the tracker.
                EnsureMarketDataCapture();

                var observation = _readiness.Observe();
                if (observation.FullyReady || observation.ShuttingDown || !observation.LifecycleOperational)
                {
                    StopReadinessLoop();
                    return;
                }

                _readinessAttempts++;
                if (!policy.ShouldRetry(_readinessAttempts))
                {
                    // The retry budget is exhausted. Stop; the readiness observation keeps
                    // reporting the outstanding fact so a later manual pass still sees the truth.
                    StopReadinessLoop();
                    return;
                }

                var delay = policy.NextDelayMs(_readinessAttempts - 1);
                try
                {
                    timer.Change(delay, System.Threading.Timeout.Infinite);
                }
                catch (Exception)
                {
                    // The timer was disposed concurrently (AddOn terminated); nothing to reschedule.
                }
            }
            catch (Exception ex)
            {
                // A readiness probe must never crash the host; record it and stop retrying.
                try
                {
                    Log("RiskPilot Diagnostics readiness probe failed: " + Core.Errors.ErrorClassifier.Describe(ex),
                        NinjaTrader.Cbi.LogLevel.Warning);
                }
                catch (Exception)
                {
                    // Logging must never throw either.
                }

                StopReadinessLoop();
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
            // Stop the readiness loop first so no timer callback can race the teardown or
            // re-subscribe the capture after it has been detached.
            StopReadinessLoop();
            _hostReachable = false;

            DiagnosticRuntime runtime;
            DiagnosticRunCoordinator coordinator;
            MarketDataCapture capture;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                runtime = _runtime;
                coordinator = _coordinator;
                capture = _marketDataCapture;
                _runtime = null;
                _coordinator = null;
                _marketDataCapture = null;
                _strategyCapture = null;
            }

            // Detach from the host market-data events before tearing anything down, so no
            // callback can race the runtime disposal.
            if (capture != null)
            {
                try
                {
                    capture.Detach();
                }
                catch (Exception ex)
                {
                    Log("RiskPilot Diagnostics market-data detach failed: " + Core.Errors.ErrorClassifier.Describe(ex),
                        NinjaTrader.Cbi.LogLevel.Error);
                }
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

        /// <summary>The host data provider, or null before initialization. Exposed for the window layer.</summary>
        public Nt8HostDataProvider Provider
        {
            get { lock (_gate) { return _provider; } }
        }

        /// <summary>The market-data capture, or null before initialization. Exposed for the window layer.</summary>
        public MarketDataCapture MarketDataCapture
        {
            get { lock (_gate) { return _marketDataCapture; } }
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
