using System;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// An <see cref="IUiDispatcher"/> whose target can be attached after construction. The
    /// AddOn builds its run coordinator while the host is initialising, before any window
    /// exists; when the Control Center window is created the real dispatcher is attached here.
    /// Until a target is attached, <see cref="Post"/> runs the action inline on the calling
    /// thread (the view state it updates is thread-safe), so a run started before the window
    /// opens is not lost. Once a target is attached, posts go through it.
    /// <para>
    /// Ownership: a target is attached together with the <paramref name="owner"/> that owns it
    /// (the host passes the owning window). Only the current owner may replace the target, and
    /// only the current owner may detach it, so an unrelated window can never detach the
    /// dispatcher the diagnostic window is using. NinjaTrader raises
    /// <c>OnWindowCreated</c>/<c>OnWindowDestroyed</c> for <em>every</em> NTWindow (charts, DOM,
    /// SuperDOM, ...), so this owner check is what keeps an unrelated window's lifetime from
    /// clearing the active dispatcher.
    /// </para>
    /// </summary>
    public sealed class SwitchableUiDispatcher : IUiDispatcher
    {
        private readonly object _gate = new object();
        private IUiDispatcher _inner;
        private object _owner;

        /// <summary>
        /// Attaches <paramref name="dispatcher"/> as the target on behalf of
        /// <paramref name="owner"/>. Returns false, leaving the current target unchanged, when
        /// the owner or dispatcher is null, when the dispatcher has already begun shutting down
        /// (a dead dispatcher is never adopted), or when a <em>live</em> target is already held by
        /// a different owner. A shut-down target is replaced regardless of owner, so a re-created
        /// Control Center can take over from a dead one. Re-attaching for the same owner is
        /// idempotent and simply refreshes the target, so a repeated host callback is harmless.
        /// </summary>
        public bool Attach(object owner, IUiDispatcher dispatcher)
        {
            if (owner == null || dispatcher == null || dispatcher.HasShutdownStarted)
            {
                return false;
            }

            lock (_gate)
            {
                if (_owner != null && !ReferenceEquals(_owner, owner) && _inner != null && !_inner.HasShutdownStarted)
                {
                    // A live dispatcher is already owned by another window. Refuse rather than
                    // steal it, so a later destruction of that other window cannot leave the
                    // diagnostic window without a dispatcher.
                    return false;
                }

                _owner = owner;
                _inner = dispatcher;
                return true;
            }
        }

        /// <summary>
        /// Detaches the target only when <paramref name="owner"/> is the current owner. Returns
        /// true when the target was cleared. An unrelated window (a chart, DOM, ...) that is
        /// destroyed is not the owner, so this is a no-op for it and the active dispatcher
        /// survives. Safe to call repeatedly.
        /// </summary>
        public bool Detach(object owner)
        {
            if (owner == null)
            {
                return false;
            }

            lock (_gate)
            {
                if (_owner == null || !ReferenceEquals(_owner, owner))
                {
                    return false;
                }

                _inner = null;
                _owner = null;
                return true;
            }
        }

        /// <summary>
        /// Detaches the target unconditionally, clearing both the target and its owner. Safe to
        /// call repeatedly and used by AddOn termination so no stale dispatcher reference survives
        /// an AddOn reload.
        /// </summary>
        public void Detach()
        {
            lock (_gate)
            {
                _inner = null;
                _owner = null;
            }
        }

        /// <summary>The owner of the current target, or null when no target is attached.</summary>
        public object Owner
        {
            get { lock (_gate) { return _owner; } }
        }

        public bool HasTarget
        {
            get { lock (_gate) { return _inner != null; } }
        }

        public bool OnDispatcherThread
        {
            get
            {
                lock (_gate)
                {
                    return _inner != null && _inner.OnDispatcherThread;
                }
            }
        }

        public bool HasShutdownStarted
        {
            get
            {
                lock (_gate)
                {
                    // "No target attached yet" is not "shutting down": until a window exists
                    // the holder runs notifications inline (see Post) rather than dropping
                    // them, so a run's progress is never lost before the window opens.
                    return _inner != null && _inner.HasShutdownStarted;
                }
            }
        }

        public bool Post(Action action)
        {
            if (action == null)
            {
                return false;
            }

            IUiDispatcher inner;
            lock (_gate)
            {
                inner = _inner;
            }

            if (inner == null)
            {
                // No window is attached. Execute inline so a run's progress/result is applied
                // to the (thread-safe) view state; the window marshals its own rendering onto
                // the UI thread when it reads the state.
                action();
                return true;
            }

            return inner.Post(action);
        }
    }
}
