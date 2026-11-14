using System;
using System.Collections.Generic;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Logging;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Combined lifecycle integration tests for the three merged lifecycle changes: the UI
    /// dispatcher ownership (<see cref="SwitchableUiDispatcher"/>), the menu-item ownership
    /// (<see cref="MenuOwnershipRegistry"/>) and the single Diagnostic Center window
    /// (<see cref="DiagnosticWindowController"/>).
    /// <para>
    /// The three primitives are individually covered by <c>UiDispatcherLifecycleTests</c>,
    /// <c>MenuIntegrationLifecycleTests</c> and <c>WindowLifecycleTests</c>. These tests exercise
    /// the <em>combined</em> host sequence — Control Center created, an unrelated window
    /// created/destroyed, a run started before the window opens, the Diagnostic Center opened and
    /// closed, AddOn termination and reinitialisation — and assert that the three ownership models
    /// do not contradict each other.
    /// </para>
    /// <para>
    /// The host hook (<c>DiagnosticAddOn.Ui.cs</c>) and the WPF adapters are compiled only for the
    /// real host build, so this test mirrors the exact host sequence over the real host-free
    /// production primitives. A green run is <b>not</b> real-host proof; the real
    /// <c>NinjaTrader.Gui.ControlCenter</c> cast and the real WPF dispatcher are only provable
    /// inside NinjaTrader 8.
    /// </para>
    /// </summary>
    public class LifecycleIntegrationTests
    {
        private const string ItemId = DiagnosticMenu.ItemId;

        /// <summary>
        /// Mirrors <c>DiagnosticAddOn.OnWindowCreated</c>: only a Control Center window attaches
        /// the dispatcher (recorded with that window as its owner) and registers the menu item.
        /// </summary>
        private static bool HostCreateWindow(
            SwitchableUiDispatcher dispatcher,
            MenuOwnershipRegistry registry,
            object window,
            bool isControlCenter,
            IUiDispatcher ui,
            IMenuSurface menu,
            out MenuAttachStatus status)
        {
            status = MenuAttachStatus.UnsupportedOwner;
            if (!isControlCenter)
            {
                return false;
            }

            var attached = dispatcher.Attach(window, ui);
            if (menu != null)
            {
                registry.Attach(window, MenuSurfaceKind.ControlCenter, true, menu, ItemId, () => { }, null, out status);
            }

            return attached;
        }

        /// <summary>
        /// Mirrors <c>DiagnosticAddOn.OnWindowDestroyed</c>: detach only when the window owns the
        /// dispatcher, and only then reset the menu registration. An unrelated window's
        /// destruction is a complete no-op for the diagnostic UI.
        /// </summary>
        private static bool HostDestroyWindow(SwitchableUiDispatcher dispatcher, MenuOwnershipRegistry registry, object window)
        {
            if (!dispatcher.Detach(window))
            {
                return false;
            }

            registry.Clear();
            return true;
        }

        /// <summary>
        /// The full AddOn terminate teardown: unconditional dispatcher detach, window close, and
        /// menu reset. Mirrors <c>ShutdownUiLayer</c>.
        /// </summary>
        private static void HostTerminate(SwitchableUiDispatcher dispatcher, MenuOwnershipRegistry registry, DiagnosticWindowController controller)
        {
            dispatcher.Detach();
            controller.CloseIfOpen();
            registry.Clear();
        }

        // ---- Objectives 1, 2, 8: dispatcher ownership vs the real window lifecycle -----------

        [Fact]
        public void ControlCenterOwnsDispatcher_MenuAndWindow_AcrossTheFullHostSequence()
        {
            var dispatcher = new SwitchableUiDispatcher();
            var registry = new MenuOwnershipRegistry();
            var controller = new DiagnosticWindowController();

            var controlCenter = new object();
            var chart = new object();
            var ccDispatcher = new FakeUiDispatcher();
            var ccMenu = new FakeMenuSurface();

            // Control Center created: it owns the dispatcher and the menu item.
            Assert.True(HostCreateWindow(dispatcher, registry, controlCenter, true, ccDispatcher, ccMenu, out var status));
            Assert.Equal(MenuAttachStatus.Attached, status);
            Assert.Same(controlCenter, dispatcher.Owner);
            Assert.Equal(1, ccMenu.ItemCount);

            // A chart is created and destroyed: neither the dispatcher nor the menu changes.
            Assert.False(HostCreateWindow(dispatcher, registry, chart, false, new FakeUiDispatcher(), null, out _));
            Assert.False(HostDestroyWindow(dispatcher, registry, chart));
            Assert.Same(controlCenter, dispatcher.Owner);
            Assert.Equal(1, ccMenu.ItemCount);
            Assert.True(registry.IsAttached(controlCenter));

            // A run's notifications still marshal through the Control Center's dispatcher.
            Assert.True(dispatcher.Post(() => { }));
            Assert.NotEmpty(ccDispatcher.PostingThreads);

            // The Diagnostic Center window opens and closes on the same controller.
            var window = LoadedWindow();
            Assert.Equal(DiagnosticWindowOpenResult.Created, controller.OpenOrActivate(() => window));
            controller.CloseIfOpen();
            Assert.False(controller.IsOpen);

            // Destroying the Control Center releases the dispatcher and the menu item.
            Assert.True(HostDestroyWindow(dispatcher, registry, controlCenter));
            Assert.False(dispatcher.HasTarget);
            Assert.Null(dispatcher.Owner);
            Assert.Equal(0, ccMenu.ItemCount);
            Assert.Equal(0, registry.AttachedCount);
        }

        [Fact]
        public void RunStartedBeforeAnyWindow_IsNotLost_AndReachesTheUiOnceAttached()
        {
            // The coordinator is built at State.Active, before any window exists. The switchable
            // dispatcher has no target, so notifications run inline against the view state; a run
            // must therefore not be lost. Once a Control Center attaches, later notifications are
            // marshalled through it.
            var switchable = new SwitchableUiDispatcher();
            var runner = new FakeDiagnosticRunner { ModuleGate = new ManualResetEventSlim(false) };
            var viewState = new DiagnosticViewState();
            var log = new DiagnosticLog(128);
            using var coordinator = new DiagnosticRunCoordinator(runner, switchable, log);
            coordinator.ProgressChanged += viewState.ApplyProgress;
            coordinator.RunCompleted += r => viewState.ApplyResult(r, stubBuild: true);

            Assert.True(coordinator.Start(DiagnosticMode.Full));
            // The run is already active while no window exists; its initial progress was applied
            // inline (no target attached), so the run is not lost.
            Assert.True(viewState.IsRunning);

            // Now the Control Center appears and attaches its dispatcher. A real WPF dispatcher
            // queues a post (InvokeAsync) and runs it later on the UI thread, so the deferred
            // double below records the action instead of running it inline.
            var controlCenter = new object();
            var ccDispatcher = new DeferredUiDispatcher();
            Assert.True(switchable.Attach(controlCenter, ccDispatcher));

            runner.ModuleGate.Set();
            Assert.True(coordinator.WaitForCompletion(TimeSpan.FromSeconds(5)));
            Assert.Equal(RunState.Completed, coordinator.State);

            // The final notification was marshalled through the Control Center dispatcher, not run
            // on the worker thread; draining the queue (as the UI thread would) applies it.
            Assert.True(ccDispatcher.PendingCount > 0);
            ccDispatcher.RunPending();
            Assert.NotNull(viewState.Result);
            Assert.False(viewState.IsRunning);
        }

        [Fact]
        public void ShuttingDownControlCenterDispatcher_DoesNotFaultARun()
        {
            // The Control Center dispatcher begins shutting down (workspace close) while a run is
            // active. The switchable dispatcher reports the shutdown, so the coordinator drops
            // notifications instead of posting to a dead dispatcher, and the run still completes.
            var switchable = new SwitchableUiDispatcher();
            var inner = new FakeUiDispatcher();
            var controlCenter = new object();
            switchable.Attach(controlCenter, inner);

            var runner = new FakeDiagnosticRunner();
            using var coordinator = new DiagnosticRunCoordinator(runner, switchable, new DiagnosticLog(64));

            inner.ShutdownStarted = true;
            Assert.True(switchable.HasShutdownStarted);

            Assert.True(coordinator.Start(DiagnosticMode.Quick));
            Assert.True(coordinator.WaitForCompletion(TimeSpan.FromSeconds(5)));
            Assert.Equal(RunState.Completed, coordinator.State);
        }

        [Fact]
        public void ThreadedAttachAndPost_WithUnrelatedOwnerDetach_NeverDeadlocks()
        {
            // AddOn callbacks arrive on many window threads. Concurrent attach/post plus
            // unrelated-owner detach attempts must never deadlock or violate single ownership.
            // This intentionally does not exercise a detach by the current owner racing reattach.
            var switchable = new SwitchableUiDispatcher();
            var controlCenter = new object();
            var errors = new List<Exception>();
            var start = new ManualResetEventSlim(false);

            var threads = new List<Thread>();
            for (var i = 0; i < 6; i++)
            {
                var thread = new Thread(() =>
                {
                    start.Wait();
                    try
                    {
                        for (var n = 0; n < 1500; n++)
                        {
                            switchable.Attach(controlCenter, new FakeUiDispatcher());
                            switchable.Post(() => { });
                            Assert.False(switchable.Detach(new object())); // unrelated window: no-op
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (errors)
                        {
                            errors.Add(ex);
                        }
                    }
                });

                threads.Add(thread);
                thread.Start();
            }

            start.Set();
            foreach (var thread in threads)
            {
                Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
            }

            Assert.Empty(errors);
            Assert.Same(controlCenter, switchable.Owner);
        }

        // ---- Objective 4: closing the Diagnostic Center leaves no stale static reference ----

        [Fact]
        public void ClosingTheDiagnosticCenter_LeavesNoStaleReference_AndDoesNotTouchTheDispatcher()
        {
            var switchable = new SwitchableUiDispatcher();
            var registry = new MenuOwnershipRegistry();
            var controller = new DiagnosticWindowController();

            var controlCenter = new object();
            var ccDispatcher = new FakeUiDispatcher();
            var ccMenu = new FakeMenuSurface();
            HostCreateWindow(switchable, registry, controlCenter, true, ccDispatcher, ccMenu, out _);

            var window = LoadedWindow();
            Assert.Equal(DiagnosticWindowOpenResult.Created, controller.OpenOrActivate(() => window));
            Assert.True(controller.IsOpen);

            // The operator closes the Diagnostic Center. The window reference is cleared, the
            // Control Center keeps owning the dispatcher, and the menu item is untouched.
            window.RaiseClosed();
            Assert.False(controller.IsOpen);
            Assert.Same(controlCenter, switchable.Owner);
            Assert.Equal(1, ccMenu.ItemCount);
        }

        // ---- Objective 5: reopening the Diagnostic Center works correctly --------------------

        [Fact]
        public void ReopeningTheDiagnosticCenter_CreatesExactlyOneFreshWindow()
        {
            var controller = new DiagnosticWindowController();
            var created = 0;
            Func<IDiagnosticWindow> factory = () => { created++; return LoadedWindow(); };

            Assert.Equal(DiagnosticWindowOpenResult.Created, controller.OpenOrActivate(factory));
            Assert.Equal(DiagnosticWindowOpenResult.Activated, controller.OpenOrActivate(factory)); // no duplicate
            Assert.Equal(1, created);

            controller.CloseIfOpen();
            Assert.False(controller.IsOpen);

            Assert.Equal(DiagnosticWindowOpenResult.Created, controller.OpenOrActivate(factory));
            Assert.Equal(2, created); // a fresh window after the close, never a reused one
            Assert.True(controller.IsOpen);
        }

        // ---- Objectives 6, 7: termination cleanup and reinitialisation without duplicates ----

        [Fact]
        public void TerminateThenReinitialise_LeavesNoStaleState_AndNoDuplicates()
        {
            var dispatcher = new SwitchableUiDispatcher();
            var registry = new MenuOwnershipRegistry();
            var controller = new DiagnosticWindowController();

            // ---- First AddOn lifetime ----
            var firstControlCenter = new object();
            var firstDispatcher = new FakeUiDispatcher();
            var firstMenu = new FakeMenuSurface();
            HostCreateWindow(dispatcher, registry, firstControlCenter, true, firstDispatcher, firstMenu, out _);

            var firstWindow = LoadedWindow();
            Assert.Equal(DiagnosticWindowOpenResult.Created, controller.OpenOrActivate(() => firstWindow));
            Assert.True(controller.IsOpen);

            // Termination: unconditional detach, close the window, reset the menu.
            HostTerminate(dispatcher, registry, controller);
            Assert.False(dispatcher.HasTarget);
            Assert.Null(dispatcher.Owner);
            Assert.False(controller.IsOpen);
            Assert.Equal(0, firstMenu.ItemCount);
            Assert.Equal(0, registry.AttachedCount);
            Assert.Equal(1, firstWindow.CloseCount);

            // ---- Second AddOn lifetime (reinitialised) ----
            var secondControlCenter = new object();
            var secondDispatcher = new FakeUiDispatcher();
            var secondMenu = new FakeMenuSurface();
            Assert.True(HostCreateWindow(dispatcher, registry, secondControlCenter, true, secondDispatcher, secondMenu, out var status));
            Assert.Equal(MenuAttachStatus.Attached, status);
            Assert.Equal(1, secondMenu.ItemCount);        // exactly one item, no duplicate from the first lifetime
            Assert.Equal(1, registry.AttachedCount);
            Assert.Same(secondControlCenter, dispatcher.Owner);

            var secondWindow = LoadedWindow();
            Assert.Equal(DiagnosticWindowOpenResult.Created, controller.OpenOrActivate(() => secondWindow));
            Assert.Equal(1, secondWindow.ShowCount);

            // A second open request activates the fresh window instead of creating a duplicate.
            Assert.Equal(DiagnosticWindowOpenResult.Activated, controller.OpenOrActivate(() => LoadedWindow()));
            Assert.Equal(1, secondWindow.ShowCount);
        }

        [Fact]
        public void RepeatedControlCenterCreate_DuringOneLifetime_AddsExactlyOneMenuItem()
        {
            // NinjaTrader may raise OnWindowCreated more than once for the same Control Center
            // (a refresh). The menu registration must stay idempotent and the dispatcher refresh,
            // not duplicate.
            var dispatcher = new SwitchableUiDispatcher();
            var registry = new MenuOwnershipRegistry();
            var controlCenter = new object();
            var menu = new FakeMenuSurface();
            var ccDispatcher = new FakeUiDispatcher();

            Assert.True(HostCreateWindow(dispatcher, registry, controlCenter, true, ccDispatcher, menu, out var firstStatus));
            Assert.Equal(MenuAttachStatus.Attached, firstStatus);

            for (var i = 0; i < 4; i++)
            {
                Assert.True(HostCreateWindow(dispatcher, registry, controlCenter, true, ccDispatcher, menu, out var status));
                Assert.Equal(MenuAttachStatus.AlreadyAttached, status); // same owner, idempotent no-op
            }

            Assert.Equal(1, menu.ItemCount);
            Assert.Equal(1, menu.AddCalls);
            Assert.Equal(1, registry.AttachedCount);
            Assert.Same(controlCenter, dispatcher.Owner);
        }

        // ---- Objectives 9, 10: the three ownership models agree; no trading behavior changed -

        [Fact]
        public void UnrelatedWindowDestroyedAfterTermination_CannotClearAFreshDispatcher()
        {
            // After termination and reinitialisation the old Control Center is gone. A late
            // OnWindowDestroyed for it (or for a chart) must not clear the new owner's dispatcher.
            var dispatcher = new SwitchableUiDispatcher();
            var registry = new MenuOwnershipRegistry();
            var controller = new DiagnosticWindowController();

            var oldControlCenter = new object();
            HostCreateWindow(dispatcher, registry, oldControlCenter, true, new FakeUiDispatcher(), new FakeMenuSurface(), out _);
            HostTerminate(dispatcher, registry, controller);

            var newControlCenter = new object();
            var newDispatcher = new FakeUiDispatcher();
            var newMenu = new FakeMenuSurface();
            HostCreateWindow(dispatcher, registry, newControlCenter, true, newDispatcher, newMenu, out _);

            // A late destroy callback for the old window and for a chart: both must be no-ops.
            Assert.False(HostDestroyWindow(dispatcher, registry, oldControlCenter));
            Assert.False(HostDestroyWindow(dispatcher, registry, new object()));

            Assert.Same(newControlCenter, dispatcher.Owner);
            Assert.Equal(1, newMenu.ItemCount);
            Assert.Equal(1, registry.AttachedCount);
        }

        [Fact]
        public void DispatcherShutdownFinished_IsTreatedAsShutDown_ByEveryLifecycleLayer()
        {
            // A finished shutdown must read as "shutting down" in the layers exercised here:
            // the WPF dispatcher refuses posts and the window lifecycle marks the window unusable.
            // This test does not invoke DiagnosticCenterWindowManager.Show.
            var raw = new System.Windows.Threading.Dispatcher { HasShutdownFinished = true };
            var wpf = new RiskPilot.Diagnostics.Nt8.Host.WpfUiDispatcher(raw);
            Assert.True(wpf.HasShutdownStarted);
            Assert.False(wpf.Post(() => { }));

            var window = LoadedWindow();
            window.HasShutdownFinished = true;
            Assert.False(WindowLifecycle.CanInvoke(window));
            Assert.False(WindowLifecycle.IsUsable(window));
        }

        // ---- Shared doubles ---------------------------------------------------------------

        private static FakeDiagnosticWindow LoadedWindow(IUiDispatcher dispatcher = null)
        {
            return new FakeDiagnosticWindow { IsLoaded = true, Dispatcher = dispatcher };
        }

        /// <summary>In-memory <see cref="IDiagnosticWindow"/>; only the WPF window is substituted.</summary>
        private sealed class FakeDiagnosticWindow : IDiagnosticWindow
        {
            private bool _closed;

            public bool IsLoaded { get; set; }

            public bool IsClosed { get { return _closed; } }

            public bool HasShutdownStarted { get; set; }

            public bool HasShutdownFinished { get; set; }

            public IUiDispatcher Dispatcher { get; set; }

            public int ShowCount { get; private set; }

            public int CloseCount { get; private set; }

            public event EventHandler Closed;

            public void Show()
            {
                ShowCount++;
                IsLoaded = true;
            }

            public void Activate()
            {
            }

            public void Close()
            {
                CloseCount++;
                RaiseClosed();
            }

            public void RaiseClosed()
            {
                _closed = true;
                var handler = Closed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// An <see cref="IUiDispatcher"/> that queues posts instead of running them inline, like a
        /// real WPF <c>InvokeAsync</c>. A test drains the queue to model the UI thread running the
        /// marshalled work.
        /// </summary>
        private sealed class DeferredUiDispatcher : IUiDispatcher
        {
            private readonly object _gate = new object();
            private readonly List<Action> _pending = new List<Action>();

            public bool OnDispatcherThread { get { return false; } }

            public bool HasShutdownStarted { get { return false; } }

            public int PendingCount
            {
                get { lock (_gate) { return _pending.Count; } }
            }

            public bool Post(Action action)
            {
                if (action == null)
                {
                    return false;
                }

                lock (_gate)
                {
                    _pending.Add(action);
                }

                return true;
            }

            public void RunPending()
            {
                Action[] actions;
                lock (_gate)
                {
                    actions = _pending.ToArray();
                    _pending.Clear();
                }

                foreach (var action in actions)
                {
                    action();
                }
            }
        }

        /// <summary>Minimal in-memory <see cref="IMenuSurface"/>; records add/remove calls.</summary>
        private sealed class FakeMenuSurface : IMenuSurface
        {
            private readonly object _gate = new object();
            private readonly Dictionary<string, Action> _items = new Dictionary<string, Action>(StringComparer.Ordinal);

            public int AddCalls;

            public int ItemCount
            {
                get { lock (_gate) { return _items.Count; } }
            }

            public bool Contains(string itemId)
            {
                lock (_gate)
                {
                    return itemId != null && _items.ContainsKey(itemId);
                }
            }

            public bool Add(string itemId, Action onClick)
            {
                lock (_gate)
                {
                    AddCalls++;
                    if (itemId == null || onClick == null || _items.ContainsKey(itemId))
                    {
                        return false;
                    }

                    _items[itemId] = onClick;
                    return true;
                }
            }

            public bool Remove(string itemId)
            {
                lock (_gate)
                {
                    return itemId != null && _items.Remove(itemId);
                }
            }
        }
    }
}
