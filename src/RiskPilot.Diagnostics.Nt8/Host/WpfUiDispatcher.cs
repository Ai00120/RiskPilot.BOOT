using System;
using System.Windows.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Run;

namespace RiskPilot.Diagnostics.Nt8.Host
{
    /// <summary>
    /// <see cref="IUiDispatcher"/> over a WPF <see cref="Dispatcher"/>. It only ever posts
    /// asynchronously via <c>InvokeAsync</c>; it never calls the blocking <c>Invoke</c>, so a
    /// diagnostic notification can never deadlock against the UI thread. When the dispatcher
    /// is already shutting down the post is refused rather than thrown.
    /// </summary>
    public sealed class WpfUiDispatcher : IUiDispatcher
    {
        private readonly Dispatcher _dispatcher;

        public WpfUiDispatcher(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public bool OnDispatcherThread
        {
            get { return _dispatcher != null && _dispatcher.CheckAccess(); }
        }

        public bool HasShutdownStarted
        {
            get { return _dispatcher == null || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished; }
        }

        public bool Post(Action action)
        {
            if (action == null)
            {
                return false;
            }

            if (_dispatcher == null || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
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
