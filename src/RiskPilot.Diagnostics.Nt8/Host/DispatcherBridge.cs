using System;
using System.Windows.Threading;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// Marshals work onto a NinjaTrader dispatcher thread without ever using the blocking
    /// <c>Dispatcher.Invoke</c>. Asynchronous posting keeps host callbacks from
    /// deadlocking against the UI thread.
    /// </summary>
    public sealed class DispatcherBridge
    {
        private readonly Dispatcher _dispatcher;

        public DispatcherBridge(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public bool IsAvailable
        {
            get { return _dispatcher != null && !_dispatcher.HasShutdownStarted; }
        }

        public bool OnDispatcherThread
        {
            get { return _dispatcher != null && _dispatcher.CheckAccess(); }
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the dispatcher thread. When already on that
        /// thread the action runs inline; otherwise it is posted asynchronously. Returns
        /// false when the dispatcher is unavailable and the action was not queued.
        /// </summary>
        public bool Post(Action action)
        {
            if (action == null)
            {
                return false;
            }

            if (_dispatcher == null || _dispatcher.HasShutdownStarted)
            {
                return false;
            }

            if (_dispatcher.CheckAccess())
            {
                action();
                return true;
            }

            _dispatcher.InvokeAsync(action);
            return true;
        }
    }
}
