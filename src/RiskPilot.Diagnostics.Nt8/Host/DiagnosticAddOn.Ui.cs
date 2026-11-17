using System;
using System.Windows;
using RiskPilot.Diagnostics.Nt8.Core.Run;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// The window-facing half of the Diagnostic Center AddOn. It is compiled only for the real
    /// host build (see the csproj), because it depends on WPF and the NinjaTrader.Gui menu
    /// surface. It only ever opens the read-only diagnostic window and adds a menu item; it
    /// never enables trading and never touches an account, connection or position.
    /// </summary>
    public sealed partial class DiagnosticAddOn
    {
        private DiagnosticCenterWindowManager _windowManager;

        /// <summary>
        /// Called by the host whenever <em>any</em> NTWindow is created (the Control Center, a
        /// chart, a DOM, ...). Only the Control Center owns the UI dispatcher and the menu item,
        /// so every other window is ignored: attaching to a chart's dispatcher would both
        /// misroute notifications and let a later chart close clear the active dispatcher.
        /// </summary>
        protected override void OnWindowCreated(Window window)
        {
            try
            {
                // NinjaTrader.Gui.ControlCenter is the single Control Center window. A generic
                // Window is cast to it exactly as the NinjaScript documentation prescribes.
                if (!(window is NinjaTrader.Gui.ControlCenter) || window.Dispatcher == null)
                {
                    return;
                }

                // Attach (or refresh) the UI dispatcher for the owning window. The switchable
                // dispatcher records this window as the owner and refuses any other window, so an
                // unrelated window can never take over the active dispatcher. A refusing attach
                // (e.g. a dispatcher already shutting down) leaves the previous target in place.
                _uiDispatcher.Attach(window, new WpfUiDispatcher(window.Dispatcher));

                DiagnosticCenterWindowManager manager;
                lock (_gate)
                {
                    if (_windowManager == null && _viewState != null && _coordinator != null)
                    {
                        _windowManager = new DiagnosticCenterWindowManager(_viewState, _coordinator, BuildEvidenceSink(), ExportLastRunEvidence, this);
                    }

                    manager = _windowManager;
                }

                if (manager != null)
                {
                    MenuIntegration.Attach(window, manager);
                }
            }
            catch (Exception ex)
            {
                Log("RiskPilot Diagnostics window hook failed: " + Core.Errors.ErrorClassifier.Describe(ex),
                    NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        /// <summary>
        /// Called by the host whenever an NTWindow is destroyed. The dispatcher is detached only
        /// when <paramref name="window"/> is the window that owns it; the switchable dispatcher
        /// performs that identity check, so closing an unrelated chart, DOM or workspace window
        /// never clears the dispatcher the Diagnostic Center is using.
        /// </summary>
        protected override void OnWindowDestroyed(Window window)
        {
            try
            {
                if (window == null)
                {
                    return;
                }

                if (!_uiDispatcher.Detach(window))
                {
                    // Not the owner of the active dispatcher: an unrelated window's destruction
                    // must not affect the Diagnostic Center.
                    return;
                }

                // The owning Control Center is gone. Drop the window manager and menu registration
                // so a re-created Control Center rebuilds them and no stale reference survives.
                lock (_gate)
                {
                    _windowManager = null;
                }

                MenuIntegration.Reset();
            }
            catch (Exception ex)
            {
                Log("RiskPilot Diagnostics window teardown failed: " + Core.Errors.ErrorClassifier.Describe(ex),
                    NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        /// <summary>
        /// Real-host implementation of the UI teardown: unconditionally detaches the dispatcher
        /// (clearing any stale target and owner), drops the window manager and closes the window.
        /// Called from SafeDispose after the run coordinator has been stopped, so no notification
        /// can race the teardown and no stale dispatcher reference survives an AddOn reload.
        /// </summary>
        partial void ShutdownUiLayer()
        {
            _uiDispatcher.Detach();
            lock (_gate)
            {
                _windowManager = null;
            }

            DiagnosticCenterWindowManager.CloseIfOpen();
            MenuIntegration.Reset();
        }

        /// <summary>
        /// Re-exports the evidence package for the last completed run to a fresh directory.
        /// The run is not repeated and no host state is touched. The new directory is written
        /// back to the view state so the UI reflects it.
        /// </summary>
        private void ExportLastRunEvidence()
        {
            DiagnosticRuntime runtime;
            DiagnosticRunResult result;
            lock (_gate)
            {
                runtime = _runtime;
                result = _viewState != null ? _viewState.Result : null;
            }

            if (runtime == null || result == null)
            {
                return;
            }

            var directory = string.Empty;
            var ok = runtime.WriteEvidencePackage(result, BuildEvidenceSink(), out directory);
            if (_viewState != null)
            {
                _viewState.ApplyEvidenceDirectory(ok ? directory : result.EvidenceDirectory);
            }
        }
    }
}
