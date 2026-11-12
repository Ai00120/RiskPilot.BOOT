using System;
using NinjaTrader.NinjaScript;
using RiskPilot.Diagnostics.Nt8.Core.Clock;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Logging;

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
    public sealed class DiagnosticAddOn : AddOnBase
    {
        private readonly object _gate = new object();
        private readonly System.Collections.Generic.List<string> _lifecycleStates = new System.Collections.Generic.List<string>();
        private DiagnosticRuntime _runtime;
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
                    var provider = new Nt8HostDataProvider(masterMetadataReady: State == State.DataLoaded || State == State.Realtime);
                    _runtime = new DiagnosticRuntime(provider, SystemClock.Instance, 4096, 8192);
                    _runtime.Initialize();
                    Log("RiskPilot Diagnostics initialized. Trading=Disabled.", NinjaTrader.Cbi.LogLevel.Information);
                }
                catch (Exception ex)
                {
                    _runtime = null;
                    Log("RiskPilot Diagnostics initialization failed: " + Core.Errors.ErrorClassifier.Describe(ex),
                        NinjaTrader.Cbi.LogLevel.Error);
                }
            }
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
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                runtime = _runtime;
                _runtime = null;
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
    }
}
