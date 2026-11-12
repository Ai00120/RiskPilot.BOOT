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
    /// </summary>
    public sealed class SwitchableUiDispatcher : IUiDispatcher
    {
        private readonly object _gate = new object();
        private IUiDispatcher _inner;

        public void Attach(IUiDispatcher dispatcher)
        {
            lock (_gate)
            {
                _inner = dispatcher;
            }
        }

        public void Detach()
        {
            lock (_gate)
            {
                _inner = null;
            }
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
