using System;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// An <see cref="IUiDispatcher"/> whose target can be attached after construction. The
    /// AddOn builds its run coordinator while the host is initialising, before any window
    /// exists; when the Control Center window is created the real dispatcher is attached here.
    /// Until then the holder reports that it is not on a dispatcher thread and refuses to post,
    /// so notifications are dropped rather than executed on the wrong thread.
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
                    return _inner == null || _inner.HasShutdownStarted;
                }
            }
        }

        public bool Post(Action action)
        {
            IUiDispatcher inner;
            lock (_gate)
            {
                inner = _inner;
            }

            if (inner == null || action == null)
            {
                return false;
            }

            return inner.Post(action);
        }
    }
}
