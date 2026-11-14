using System;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// The minimal host-free view of a single diagnostic window. It lets the window lifecycle
    /// policy be exercised without any WPF or NinjaTrader type: the real host supplies a WPF
    /// window adapter, and a test supplies an in-memory double.
    /// <para>
    /// Every member is read-only with respect to the host. Implementing it never enables trading
    /// and never touches an account, connection, position or RiskPilot setting.
    /// </para>
    /// </summary>
    public interface IDiagnosticWindow
    {
        /// <summary>
        /// True only while the window is fully created and loaded (a usable, activatable window).
        /// A window that has closed, or was never shown, reports false so the policy never reuses
        /// an invalid window.
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>True once the window has begun closing or has closed.</summary>
        bool IsClosed { get; }

        /// <summary>True when the window's dispatcher has begun shutting down.</summary>
        bool HasShutdownStarted { get; }

        /// <summary>True when the window's dispatcher has finished shutting down.</summary>
        bool HasShutdownFinished { get; }

        /// <summary>The dispatcher the window lives on, used to marshal its close. May be null.</summary>
        IUiDispatcher Dispatcher { get; }

        /// <summary>Shows a newly created window. Called once, on the UI thread.</summary>
        void Show();

        /// <summary>Brings an already-open window to the foreground.</summary>
        void Activate();

        /// <summary>Closes the window. Called on the window's own dispatcher thread.</summary>
        void Close();

        /// <summary>Raised when the window has closed. Used to clear the tracked reference.</summary>
        event EventHandler Closed;
    }
}
