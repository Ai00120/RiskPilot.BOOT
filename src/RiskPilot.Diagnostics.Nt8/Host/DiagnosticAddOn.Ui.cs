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
        /// Called by the host when a window is created. The first window is used to attach the
        /// UI dispatcher (so run notifications reach the UI thread) and to add the diagnostic
        /// menu item. Nothing else about the host window is modified.
        /// </summary>
        protected override void OnWindowCreated(Window window)
        {
            try
            {
                if (window == null || window.Dispatcher == null)
                {
                    return;
                }

                _uiDispatcher.Attach(new WpfUiDispatcher(window.Dispatcher));

                DiagnosticCenterWindowManager manager;
                lock (_gate)
                {
                    if (_windowManager == null && _viewState != null && _coordinator != null)
                    {
                        _windowManager = new DiagnosticCenterWindowManager(_viewState, _coordinator, BuildEvidenceSink(), ExportLastRunEvidence);
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

        /// <summary>Called by the host when a window is destroyed. Detaches the UI dispatcher.</summary>
        protected override void OnWindowDestroyed(Window window)
        {
            _uiDispatcher.Detach();
        }

        /// <summary>
        /// Real-host implementation of the UI teardown: detaches the dispatcher, drops the
        /// window manager and closes the window. Called from SafeDispose after the run
        /// coordinator has been stopped.
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
