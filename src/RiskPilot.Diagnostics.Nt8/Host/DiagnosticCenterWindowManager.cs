using System;
using System.Windows.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Security;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Owns the single Diagnostic Center window. Only one instance is ever shown: a second request
    /// activates the existing window instead of opening a duplicate. Every call is marshalled onto
    /// the window's dispatcher with <c>InvokeAsync</c> only, so a call from a NinjaTrader callback
    /// thread can never block or deadlock.
    /// <para>
    /// The single-window reference discipline — reuse only a loaded window, clear stale references,
    /// check both dispatcher shutdown states, tolerate repeated closes, and never let an
    /// asynchronous close clear a newer window — lives in the host-free
    /// <see cref="DiagnosticWindowController"/>, which this class adapts to a WPF window. Keeping
    /// the policy host-free is what makes those guarantees unit-testable without NinjaTrader.
    /// </para>
    /// <para>
    /// This file is part of the real-host build only (see the csproj); it is not compiled for the
    /// offline stub build.
    /// </para>
    /// </summary>
    public sealed class DiagnosticCenterWindowManager
    {
        // A process-wide controller, so the "only one window" guarantee survives an AddOn
        // terminate/reload cycle within the same NinjaTrader process: ShutdownUiLayer clears it on
        // termination, and a re-initialised AddOn reuses the same (now empty) controller.
        private static readonly DiagnosticWindowController Controller = new DiagnosticWindowController();

        private readonly DiagnosticViewState _viewState;
        private readonly DiagnosticRunCoordinator _coordinator;
        private readonly IEvidenceSink _evidenceSink;
        private readonly Action _exportAction;

        public DiagnosticCenterWindowManager(DiagnosticViewState viewState, DiagnosticRunCoordinator coordinator, IEvidenceSink evidenceSink)
            : this(viewState, coordinator, evidenceSink, null)
        {
        }

        public DiagnosticCenterWindowManager(DiagnosticViewState viewState, DiagnosticRunCoordinator coordinator, IEvidenceSink evidenceSink, Action exportAction)
        {
            _viewState = viewState ?? throw new ArgumentNullException(nameof(viewState));
            _coordinator = coordinator;
            _evidenceSink = evidenceSink;
            _exportAction = exportAction;
        }

        /// <summary>True when a window is currently tracked.</summary>
        public static bool IsOpen
        {
            get { return Controller.IsOpen; }
        }

        /// <summary>
        /// Shows the window, creating it on the given dispatcher when no usable window exists.
        /// Returns false when the dispatcher is unavailable or already shutting down. Never throws
        /// to the caller.
        /// </summary>
        public bool Show(Dispatcher dispatcher)
        {
            // Both shutdown states are checked: a dispatcher that has finished shutting down can
            // never run a posted action, so queueing a window onto it would leak a dead reference.
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return false;
            }

            try
            {
                // The open decision runs on the UI thread; the host-free controller owns the
                // single-window reference discipline. Returning true means the request was queued
                // (the original contract), not that the window already exists.
                dispatcher.InvokeAsync(() => Controller.OpenOrActivate(
                    () => new WpfDiagnosticWindow(new DiagnosticCenterWindow(_viewState, _coordinator, _evidenceSink, _exportAction))));

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Closes the window if it is open. Safe to call repeatedly and from any thread; a failure
        /// to close is contained and never blocks shutdown. The tracked reference is cleared before
        /// the close is queued, so a repeated call is a no-op and a re-open is never blocked by a
        /// half-closed window.
        /// </summary>
        public static void CloseIfOpen()
        {
            Controller.CloseIfOpen();
        }

        /// <summary>
        /// Adapts the real WPF <see cref="DiagnosticCenterWindow"/> to the host-free
        /// <see cref="IDiagnosticWindow"/> contract. It only reads window/dispatcher state and
        /// closes the window; it never touches an account, connection or position.
        /// </summary>
        private sealed class WpfDiagnosticWindow : IDiagnosticWindow
        {
            private readonly DiagnosticCenterWindow _window;
            private readonly IUiDispatcher _dispatcher;
            private bool _shown;
            private volatile bool _closed;

            public WpfDiagnosticWindow(DiagnosticCenterWindow window)
            {
                _window = window;
                _dispatcher = new WpfUiDispatcher(window.Dispatcher);
                _window.Closed += (_, __) => OnClosed();
            }

            public bool IsLoaded
            {
                // A window that has been shown is usable even before WPF raises Loaded, so a
                // second Show during that window of time activates it instead of duplicating it.
                get { return _shown || _window.IsLoaded; }
            }

            public bool IsClosed
            {
                get { return _closed; }
            }

            public bool HasShutdownStarted
            {
                get { return _window.Dispatcher == null || _window.Dispatcher.HasShutdownStarted; }
            }

            public bool HasShutdownFinished
            {
                get { return _window.Dispatcher == null || _window.Dispatcher.HasShutdownFinished; }
            }

            public IUiDispatcher Dispatcher
            {
                get { return _dispatcher; }
            }

            public void Show()
            {
                _shown = true;
                _window.Show();
            }

            public void Activate()
            {
                _window.Activate();
            }

            public void Close()
            {
                _closed = true;
                _window.Close();
            }

            public event EventHandler Closed;

            private void OnClosed()
            {
                _closed = true;
                var handler = Closed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }
    }
}
