using System;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>Outcome of a single-window open request.</summary>
    public enum DiagnosticWindowOpenResult
    {
        /// <summary>No usable window existed and a new one was created.</summary>
        Created,

        /// <summary>A usable window already existed; it was activated instead of duplicated.</summary>
        Activated,

        /// <summary>The window could not be created (the factory failed); nothing is tracked.</summary>
        Unavailable
    }

    /// <summary>
    /// Host-free policy for a single diagnostic window's lifecycle. It encodes the two decisions
    /// that are easy to get wrong at the host boundary and that must not depend on any WPF type:
    /// <list type="bullet">
    /// <item><see cref="IsUsable"/> — whether a tracked window may be reused (only a loaded,
    /// not-closed window whose dispatcher has neither started nor finished shutting down);</item>
    /// <item><see cref="CanInvoke"/> — whether the window's dispatcher can still run work (both
    /// shutdown states are checked).</item>
    /// </list>
    /// </summary>
    public static class WindowLifecycle
    {
        /// <summary>
        /// True when <paramref name="window"/> is a valid, reusable window. A null window, a window
        /// that has begun closing, one that is not loaded, or one whose dispatcher is shutting down
        /// is never usable.
        /// </summary>
        public static bool IsUsable(IDiagnosticWindow window)
        {
            return window != null
                && !window.IsClosed
                && window.IsLoaded
                && CanInvoke(window);
        }

        /// <summary>
        /// True when the window's dispatcher can still accept work. Both
        /// <see cref="IDiagnosticWindow.HasShutdownStarted"/> and
        /// <see cref="IDiagnosticWindow.HasShutdownFinished"/> are checked: a dispatcher that has
        /// finished shutting down cannot run a posted action even though a start flag may have been
        /// cleared.
        /// </summary>
        public static bool CanInvoke(IDiagnosticWindow window)
        {
            return window != null
                && !window.HasShutdownStarted
                && !window.HasShutdownFinished;
        }
    }

    /// <summary>
    /// Owns the single Diagnostic Center window reference and the policy for opening, reusing and
    /// closing it. It is host-free: it drives <see cref="IDiagnosticWindow"/> instances and never
    /// references a WPF or NinjaTrader type, so its guarantees are unit-testable without
    /// NinjaTrader.
    /// <para>
    /// Guarantees:
    /// <list type="bullet">
    /// <item>at most one usable window exists; a second open request activates the existing one
    /// instead of creating a duplicate;</item>
    /// <item>a closed, unloaded or shutting-down window is never reused — it is dropped and a fresh
    /// window is created;</item>
    /// <item>the tracked reference is cleared reliably (on close, on open failure and on explicit
    /// close) so a stale window can never be reported as open;</item>
    /// <item>no lock is held while invoking a window or dispatcher operation, so a callback that
    /// re-enters the controller cannot deadlock;</item>
    /// <item>an asynchronous close only clears the reference it was started for, so it can never
    /// clear a newer window opened after the close was queued.</item>
    /// </list>
    /// The caller (the host adapter) is responsible for marshalling <see cref="OpenOrActivate"/>
    /// onto the UI thread; this controller only reads window state and closes windows, which is
    /// safe to do on the UI thread.
    /// </para>
    /// </summary>
    public sealed class DiagnosticWindowController
    {
        private readonly object _gate = new object();
        private IDiagnosticWindow _window;

        /// <summary>True when a window reference is currently tracked.</summary>
        public bool IsOpen
        {
            get { lock (_gate) { return _window != null; } }
        }

        /// <summary>
        /// Opens the window, reusing a usable one. <paramref name="factory"/> creates a new window;
        /// it is invoked at most once and only when no usable window exists. Must be called on the
        /// UI thread. Never throws to the caller.
        /// </summary>
        public DiagnosticWindowOpenResult OpenOrActivate(Func<IDiagnosticWindow> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            try
            {
                return OpenOrActivateCore(factory);
            }
            catch (Exception)
            {
                // An unreadable window state must never propagate to the host. Do not create a
                // duplicate: keep whatever reference we have and report that nothing was created.
                return DiagnosticWindowOpenResult.Unavailable;
            }
        }

        private DiagnosticWindowOpenResult OpenOrActivateCore(Func<IDiagnosticWindow> factory)
        {
            IDiagnosticWindow existing;
            lock (_gate)
            {
                existing = _window;
            }

            if (WindowLifecycle.IsUsable(existing))
            {
                try { existing.Activate(); } catch (Exception) { /* activation is best effort */ }
                return DiagnosticWindowOpenResult.Activated;
            }

            if (existing != null)
            {
                // The tracked window is closed, unloaded or on a shutting-down dispatcher. Drop the
                // stale reference so a fresh window can replace it.
                lock (_gate)
                {
                    if (ReferenceEquals(_window, existing))
                    {
                        _window = null;
                    }
                }
            }

            IDiagnosticWindow created;
            try
            {
                created = factory();
            }
            catch (Exception)
            {
                lock (_gate) { _window = null; }
                return DiagnosticWindowOpenResult.Unavailable;
            }

            if (created == null)
            {
                lock (_gate) { _window = null; }
                return DiagnosticWindowOpenResult.Unavailable;
            }

            var captured = created;
            captured.Closed += (_, __) => OnWindowClosed(captured);

            lock (_gate)
            {
                _window = captured;
            }

            try
            {
                created.Show();
            }
            catch (Exception)
            {
                // A window that fails to show is never left tracked.
                lock (_gate)
                {
                    if (ReferenceEquals(_window, captured))
                    {
                        _window = null;
                    }
                }

                return DiagnosticWindowOpenResult.Unavailable;
            }

            return DiagnosticWindowOpenResult.Created;
        }

        /// <summary>
        /// Closes the tracked window if it is open. Safe to call repeatedly and from any thread. A
        /// failure to close is contained and never blocks shutdown. <paramref name="onClosed"/> runs
        /// after the close has been requested, on the calling thread.
        /// </summary>
        public void CloseIfOpen(Action onClosed = null)
        {
            IDiagnosticWindow window;
            lock (_gate)
            {
                window = _window;
                _window = null;
            }

            // The reference is cleared before any UI work runs, so a re-entrant CloseIfOpen or a
            // second OpenOrActivate from the close path can never observe a half-closed window.
            if (window != null)
            {
                try
                {
                    // Both dispatcher shutdown states are checked before any work is queued: a
                    // dispatcher that has finished shutting down cannot run a close and must not be
                    // asked to.
                    if (WindowLifecycle.CanInvoke(window))
                    {
                        var dispatcher = window.Dispatcher;
                        if (dispatcher != null && !dispatcher.HasShutdownStarted)
                        {
                            dispatcher.Post(() => CloseSafely(window));
                        }
                        else
                        {
                            // No usable dispatcher: the caller is already on the UI thread (or the
                            // host is tearing down), so close inline.
                            CloseSafely(window);
                        }
                    }
                }
                catch (Exception)
                {
                    // The window is being torn down with the host; the reference is already cleared
                    // so no stale state remains and the exception is contained.
                }
            }

            if (onClosed != null)
            {
                try { onClosed(); } catch (Exception) { /* notification must never throw */ }
            }
        }

        private static void CloseSafely(IDiagnosticWindow window)
        {
            try { window.Close(); }
            catch (Exception) { /* closing during shutdown must never throw */ }
        }

        private void OnWindowClosed(IDiagnosticWindow window)
        {
            lock (_gate)
            {
                // Only clear the reference if it still points at the window that closed. An
                // asynchronous close of an older window can never clear a newer window.
                if (ReferenceEquals(_window, window))
                {
                    _window = null;
                }
            }
        }
    }
}
