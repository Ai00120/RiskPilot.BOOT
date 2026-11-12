using System;
using System.Windows.Threading;
using NinjaTrader.Gui.Tools;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Security;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Owns the single Diagnostic Center window. Only one instance is ever shown: a second
    /// request activates the existing window instead of opening a duplicate. Every call is
    /// marshalled onto the window's dispatcher with <c>InvokeAsync</c> only, so a call from a
    /// NinjaTrader callback thread can never block or deadlock.
    /// <para>
    /// This file is part of the real-host build only (see the csproj); it is not compiled for
    /// the offline stub build.
    /// </para>
    /// </summary>
    public sealed class DiagnosticCenterWindowManager
    {
        private static readonly object Gate = new object();
        private static DiagnosticCenterWindow _window;

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

        /// <summary>True when a window is currently open.</summary>
        public static bool IsOpen
        {
            get { lock (Gate) { return _window != null; } }
        }

        /// <summary>
        /// Shows the window, creating it on the given dispatcher when it does not exist.
        /// Returns false when the dispatcher is unavailable. Never throws to the caller.
        /// </summary>
        public bool Show(Dispatcher dispatcher)
        {
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                return false;
            }

            try
            {
                dispatcher.InvokeAsync(() =>
                {
                    lock (Gate)
                    {
                        if (_window != null && _window.IsLoaded)
                        {
                            _window.Activate();
                            return;
                        }

                        _window = new DiagnosticCenterWindow(_viewState, _coordinator, _evidenceSink, _exportAction);
                        _window.Closed += (_, __) => { lock (Gate) { _window = null; } };
                        _window.Show();
                    }
                });

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Closes the window if it is open. Safe to call from any thread and during shutdown;
        /// a failure to close is contained and never blocks shutdown.
        /// </summary>
        public static void CloseIfOpen()
        {
            DiagnosticCenterWindow window;
            lock (Gate)
            {
                window = _window;
                _window = null;
            }

            if (window == null)
            {
                return;
            }

            try
            {
                var dispatcher = window.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted)
                {
                    return;
                }

                dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        window.Close();
                    }
                    catch (Exception)
                    {
                        // Closing during shutdown must never throw.
                    }
                });
            }
            catch (Exception)
            {
                // Ignore: the window is being torn down with the host.
            }
        }
    }
}
