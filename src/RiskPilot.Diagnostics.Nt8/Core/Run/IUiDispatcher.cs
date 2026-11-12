using System;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// Abstraction over a UI dispatcher so the run coordinator can marshal UI notifications
    /// without referencing any WPF or NinjaTrader type. The real host implements this with
    /// <c>Dispatcher.InvokeAsync</c>; tests implement it synchronously. Only asynchronous
    /// posting is modelled, so a coordinator can never block a host callback by invoking.
    /// </summary>
    public interface IUiDispatcher
    {
        /// <summary>True when the current thread is the dispatcher thread.</summary>
        bool OnDispatcherThread { get; }

        /// <summary>True when the dispatcher has begun shutting down and can no longer run work.</summary>
        bool HasShutdownStarted { get; }

        /// <summary>
        /// Posts <paramref name="action"/> to the dispatcher thread asynchronously. Returns
        /// false when the dispatcher is unavailable and the action was not queued.
        /// </summary>
        bool Post(Action action);
    }
}
