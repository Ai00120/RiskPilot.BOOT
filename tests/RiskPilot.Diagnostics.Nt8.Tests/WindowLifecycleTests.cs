using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Tests for the single Diagnostic Center window lifecycle. They exercise the real,
    /// host-free <see cref="DiagnosticWindowController"/> and <see cref="WindowLifecycle"/> over
    /// an in-memory window double: only the WPF window boundary is substituted, so the
    /// single-window, stale-reference, dispatcher-shutdown and reopen guarantees are covered
    /// without NinjaTrader.
    /// <para>
    /// These tests do NOT claim real NinjaTrader host compatibility; the WPF adapter
    /// (<c>DiagnosticCenterWindowManager</c>) is compiled only for the real host build.
    /// </para>
    /// </summary>
    public class WindowLifecycleTests
    {
        private static FakeDiagnosticWindow LoadedWindow(IUiDispatcher dispatcher = null)
        {
            return new FakeDiagnosticWindow { IsLoaded = true, Dispatcher = dispatcher };
        }

        [Fact]
        public void FirstShow_CreatesAndTracksTheWindow()
        {
            var controller = new DiagnosticWindowController();
            var window = LoadedWindow();

            var result = controller.OpenOrActivate(() => window);

            Assert.Equal(DiagnosticWindowOpenResult.Created, result);
            Assert.True(controller.IsOpen);
            Assert.True(window.IsLoaded);
            Assert.Equal(1, window.ShowCount);
        }

        [Fact]
        public void RepeatedShow_ActivatesExistingWindow_WithoutDuplicate()
        {
            var controller = new DiagnosticWindowController();
            var created = 0;
            var window = LoadedWindow();
            Func<IDiagnosticWindow> factory = () => { created++; return window; };

            Assert.Equal(DiagnosticWindowOpenResult.Created, controller.OpenOrActivate(factory));
            Assert.Equal(DiagnosticWindowOpenResult.Activated, controller.OpenOrActivate(factory));
            Assert.Equal(DiagnosticWindowOpenResult.Activated, controller.OpenOrActivate(factory));

            Assert.Equal(1, created);          // only one window was ever created
            Assert.Equal(1, window.ShowCount); // shown exactly once
            Assert.Equal(2, window.ActivateCount);
        }

        [Fact]
        public void WindowClosed_ClearsTheTrackedReference()
        {
            var controller = new DiagnosticWindowController();
            var window = LoadedWindow();
            controller.OpenOrActivate(() => window);
            Assert.True(controller.IsOpen);

            // Simulate the operator closing the window (the Closed event fires).
            window.RaiseClosed();

            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void CloseIfOpen_ClosesTheTrackedWindow_AndClearsTheReference()
        {
            var controller = new DiagnosticWindowController();
            var window = LoadedWindow();
            controller.OpenOrActivate(() => window);

            controller.CloseIfOpen();

            Assert.Equal(1, window.CloseCount);
            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void RepeatedCloseIfOpen_IsSafe_AndClosesOnlyOnce()
        {
            var controller = new DiagnosticWindowController();
            var window = LoadedWindow();
            controller.OpenOrActivate(() => window);

            controller.CloseIfOpen();
            controller.CloseIfOpen();
            controller.CloseIfOpen();

            Assert.Equal(1, window.CloseCount);
            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void CloseIfOpen_WithNoWindow_IsANoOp()
        {
            var controller = new DiagnosticWindowController();

            controller.CloseIfOpen();
            controller.CloseIfOpen();

            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void ReopeningAfterClosing_CreatesAFreshWindow()
        {
            var controller = new DiagnosticWindowController();
            var first = LoadedWindow();
            controller.OpenOrActivate(() => first);

            controller.CloseIfOpen();
            Assert.False(controller.IsOpen);

            var second = LoadedWindow();
            var result = controller.OpenOrActivate(() => second);

            Assert.Equal(DiagnosticWindowOpenResult.Created, result);
            Assert.True(controller.IsOpen);
            Assert.Equal(1, second.ShowCount);
            Assert.Equal(0, second.CloseCount);
        }

        [Fact]
        public void ReopeningAfterWindowClosed_CreatesAFreshWindow()
        {
            var controller = new DiagnosticWindowController();
            var first = LoadedWindow();
            controller.OpenOrActivate(() => first);

            first.RaiseClosed();
            Assert.False(controller.IsOpen);

            var second = LoadedWindow();
            Assert.Equal(DiagnosticWindowOpenResult.Created, controller.OpenOrActivate(() => second));
            Assert.True(controller.IsOpen);
        }

        [Fact]
        public void DispatcherShutdownStarted_MakesWindowUnusable()
        {
            var window = LoadedWindow();
            window.HasShutdownStarted = true;

            Assert.False(WindowLifecycle.CanInvoke(window));
            Assert.False(WindowLifecycle.IsUsable(window));
        }

        [Fact]
        public void DispatcherShutdownFinished_MakesWindowUnusable()
        {
            var window = LoadedWindow();
            window.HasShutdownFinished = true;

            Assert.False(WindowLifecycle.CanInvoke(window));
            Assert.False(WindowLifecycle.IsUsable(window));
        }

        [Fact]
        public void Show_ReplacesWindowWhoseDispatcherStartedShutdown()
        {
            var controller = new DiagnosticWindowController();
            var stale = LoadedWindow();
            controller.OpenOrActivate(() => stale);
            stale.HasShutdownStarted = true;

            var fresh = LoadedWindow();
            var result = controller.OpenOrActivate(() => fresh);

            Assert.Equal(DiagnosticWindowOpenResult.Created, result);
            Assert.True(controller.IsOpen);
            Assert.Equal(1, fresh.ShowCount);
        }

        [Fact]
        public void CloseIfOpen_WhenDispatcherShuttingDown_ClearsReference_AndDoesNotThrow()
        {
            var controller = new DiagnosticWindowController();
            var window = LoadedWindow();
            controller.OpenOrActivate(() => window);
            window.HasShutdownFinished = true;

            controller.CloseIfOpen();

            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void CloseIfOpen_WhenCloseThrows_StillClearsTheReference()
        {
            var controller = new DiagnosticWindowController();
            var window = LoadedWindow();
            window.ThrowOnClose = true;
            controller.OpenOrActivate(() => window);

            controller.CloseIfOpen(); // must not throw

            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void OpenOrActivate_WhenWindowStateReadThrows_IsContained_AndKeepsTheReference()
        {
            var controller = new DiagnosticWindowController();
            var window = LoadedWindow();
            controller.OpenOrActivate(() => window);

            // A window whose state getters throw must not fault the host, and must not be
            // duplicated: the existing reference is preserved.
            window.ThrowOnStateRead = true;

            var result = controller.OpenOrActivate(() => LoadedWindow());

            Assert.Equal(DiagnosticWindowOpenResult.Unavailable, result);
            Assert.True(controller.IsOpen);
        }

        [Fact]
        public void CloseIfOpen_WhenWindowStateReadThrows_IsContained_AndClearsTheReference()
        {
            var controller = new DiagnosticWindowController();
            var window = LoadedWindow();
            controller.OpenOrActivate(() => window);
            window.ThrowOnStateRead = true;

            controller.CloseIfOpen(); // must not throw

            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void CloseIfOpen_NotifiesOnClosed_EvenWhenStateReadThrows()
        {
            var controller = new DiagnosticWindowController();
            var window = LoadedWindow();
            controller.OpenOrActivate(() => window);
            window.ThrowOnStateRead = true;

            var notified = false;
            controller.CloseIfOpen(() => notified = true);

            Assert.True(notified);
            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void Show_WhenFactoryThrows_LeavesNoTrackedWindow()
        {
            var controller = new DiagnosticWindowController();

            var result = controller.OpenOrActivate(() => throw new InvalidOperationException("boom"));

            Assert.Equal(DiagnosticWindowOpenResult.Unavailable, result);
            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void AsyncClose_DoesNotClearANewerWindow()
        {
            var deferred = new DeferredUiDispatcher();
            var controller = new DiagnosticWindowController();
            var first = LoadedWindow(deferred);
            controller.OpenOrActivate(() => first);

            // Close is queued on the dispatcher but not yet run.
            controller.CloseIfOpen();
            Assert.False(controller.IsOpen);

            // A new window opens before the queued close runs.
            var second = LoadedWindow(deferred);
            Assert.Equal(DiagnosticWindowOpenResult.Created, controller.OpenOrActivate(() => second));
            Assert.True(controller.IsOpen);

            // The queued close of the OLD window now runs; it must not clear the NEW window.
            deferred.RunPending();

            Assert.True(first.IsClosed);
            Assert.True(controller.IsOpen);
            Assert.False(second.IsClosed);
        }

        [Fact]
        public void AsyncClose_OfTheCurrentWindow_ClearsTheReference()
        {
            var deferred = new DeferredUiDispatcher();
            var controller = new DiagnosticWindowController();
            var window = LoadedWindow(deferred);
            controller.OpenOrActivate(() => window);

            controller.CloseIfOpen();
            Assert.False(controller.IsOpen);

            deferred.RunPending();
            Assert.True(window.IsClosed);
            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void AddOnTermination_ThenReinitialization_RecoversTheLifecycle()
        {
            // One controller models the process-wide single-window owner across an AddOn
            // terminate / reload cycle.
            var controller = new DiagnosticWindowController();
            var first = LoadedWindow();
            controller.OpenOrActivate(() => first);

            // Termination: the UI layer closes the window and drops its reference.
            controller.CloseIfOpen();
            Assert.False(controller.IsOpen);

            // Re-initialization: a fresh window opens on the same controller.
            var second = LoadedWindow();
            Assert.Equal(DiagnosticWindowOpenResult.Created, controller.OpenOrActivate(() => second));
            Assert.True(controller.IsOpen);
            Assert.Equal(1, second.ShowCount);

            // And it can still be closed cleanly afterwards.
            controller.CloseIfOpen();
            Assert.False(controller.IsOpen);
            Assert.Equal(1, second.CloseCount);
        }

        [Fact]
        public void UnloadedWindow_IsNotUsable()
        {
            var window = new FakeDiagnosticWindow { IsLoaded = false };

            Assert.False(WindowLifecycle.IsUsable(window));
        }

        [Fact]
        public void Show_ReplacesAnUnloadedWindow()
        {
            var controller = new DiagnosticWindowController();
            var unloaded = LoadedWindow();
            controller.OpenOrActivate(() => unloaded);

            // Simulate the window being unloaded while still tracked (e.g. its content failed to
            // load): it is no longer reusable and must be replaced, not activated.
            unloaded.IsLoaded = false;

            var fresh = LoadedWindow();
            var result = controller.OpenOrActivate(() => fresh);

            Assert.Equal(DiagnosticWindowOpenResult.Created, result);
            Assert.Equal(1, fresh.ShowCount);
            Assert.Equal(0, unloaded.ActivateCount);
        }

        /// <summary>In-memory <see cref="IDiagnosticWindow"/>; only the WPF window is substituted.</summary>
        private sealed class FakeDiagnosticWindow : IDiagnosticWindow
        {
            private bool _closed;
            private bool _shutdownStarted;
            private bool _shutdownFinished;

            public bool IsLoaded { get; set; }

            public bool IsClosed
            {
                get { ThrowIfStateReadFaults(); return _closed; }
            }

            public bool HasShutdownStarted
            {
                get { ThrowIfStateReadFaults(); return _shutdownStarted; }
                set { _shutdownStarted = value; }
            }

            public bool HasShutdownFinished
            {
                get { ThrowIfStateReadFaults(); return _shutdownFinished; }
                set { _shutdownFinished = value; }
            }

            public IUiDispatcher Dispatcher { get; set; }

            public bool ThrowOnClose { get; set; }

            /// <summary>When set, every state getter throws (models a torn-down WPF window).</summary>
            public bool ThrowOnStateRead { get; set; }

            public int ShowCount { get; private set; }

            public int ActivateCount { get; private set; }

            public int CloseCount { get; private set; }

            public event EventHandler Closed;

            public void Show()
            {
                ShowCount++;
                IsLoaded = true;
            }

            public void Activate()
            {
                ActivateCount++;
            }

            public void Close()
            {
                CloseCount++;
                if (ThrowOnClose)
                {
                    throw new InvalidOperationException("close failed");
                }

                RaiseClosed();
            }

            /// <summary>Simulates the operator closing the window (the Closed event fires).</summary>
            public void RaiseClosed()
            {
                _closed = true;
                var handler = Closed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }

            private void ThrowIfStateReadFaults()
            {
                if (ThrowOnStateRead)
                {
                    throw new InvalidOperationException("window state unreadable");
                }
            }
        }

        /// <summary>
        /// An <see cref="IUiDispatcher"/> that records posted work instead of running it, so a
        /// test can complete an asynchronous close after a newer window has opened.
        /// </summary>
        private sealed class DeferredUiDispatcher : IUiDispatcher
        {
            private readonly List<Action> _pending = new List<Action>();

            public bool OnDispatcherThread { get { return true; } }

            public bool HasShutdownStarted { get { return false; } }

            public bool Post(Action action)
            {
                if (action == null)
                {
                    return false;
                }

                _pending.Add(action);
                return true;
            }

            public void RunPending()
            {
                var actions = _pending.ToArray();
                _pending.Clear();
                foreach (var action in actions)
                {
                    action();
                }
            }
        }
    }
}
