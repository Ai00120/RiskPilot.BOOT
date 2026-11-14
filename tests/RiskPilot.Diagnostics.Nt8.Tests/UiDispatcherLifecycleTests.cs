using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Dispatcher-lifecycle tests for the Diagnostic Center UI.
    /// <para>
    /// NinjaTrader raises <c>OnWindowCreated</c>/<c>OnWindowDestroyed</c> for <em>every</em>
    /// NTWindow (Control Center, charts, DOM, ...). The host hook that decides which window may
    /// own the UI dispatcher is thin (it only casts the window to
    /// <c>NinjaTrader.Gui.ControlCenter</c>); the ownership policy itself lives in the host-free
    /// <see cref="SwitchableUiDispatcher"/>, so it is testable offline. These tests drive that
    /// real production policy — and the real <see cref="WpfUiDispatcher"/> over the offline
    /// dispatcher stub — through the exact window create/destroy/terminate/reinitialise sequence
    /// the AddOn performs. They do not claim real NinjaTrader runtime behaviour.
    /// </para>
    /// </summary>
    public class UiDispatcherLifecycleTests
    {
        /// <summary>
        /// Mirrors the AddOn's <c>OnWindowCreated</c> guard: only the Control Center window
        /// (<c>window is NinjaTrader.Gui.ControlCenter</c>) may attach the dispatcher; any other
        /// window is ignored. This is test scaffolding for the thin host hook; the policy under
        /// test is the real <see cref="SwitchableUiDispatcher"/>.
        /// </summary>
        private static bool HostOnWindowCreated(SwitchableUiDispatcher dispatcher, object window, bool isControlCenter, IUiDispatcher ui)
        {
            if (!isControlCenter)
            {
                return false;
            }

            return dispatcher.Attach(window, ui);
        }

        /// <summary>Mirrors the AddOn's <c>OnWindowDestroyed</c>: detach only if the window owns it.</summary>
        private static bool HostOnWindowDestroyed(SwitchableUiDispatcher dispatcher, object window)
        {
            return dispatcher.Detach(window);
        }

        [Fact]
        public void Attach_WithNullOwner_IsRefused_AndPostsRunInline()
        {
            var switchable = new SwitchableUiDispatcher();
            var inner = new FakeUiDispatcher { RunInline = false };

            Assert.False(switchable.Attach(null, inner));
            Assert.False(switchable.HasTarget);
            Assert.Null(switchable.Owner);

            // With no target the action runs inline, so a run's progress is never lost.
            var ran = false;
            Assert.True(switchable.Post(() => ran = true));
            Assert.True(ran);
            Assert.Empty(inner.PostingThreads);
        }

        [Fact]
        public void Attach_WithNullDispatcher_IsRefused()
        {
            var switchable = new SwitchableUiDispatcher();
            Assert.False(switchable.Attach(new object(), null));
            Assert.False(switchable.HasTarget);
        }

        [Fact]
        public void Attach_ForOwner_BecomesTarget_AndRoutesPosts()
        {
            var switchable = new SwitchableUiDispatcher();
            var inner = new FakeUiDispatcher { RunInline = false };
            var owner = new object();

            Assert.True(switchable.Attach(owner, inner));
            Assert.True(switchable.HasTarget);
            Assert.Same(owner, switchable.Owner);

            var ran = false;
            Assert.True(switchable.Post(() => ran = true));
            // Routed through the inner dispatcher (recorded a posting thread) rather than inline.
            Assert.NotEmpty(inner.PostingThreads);
            Assert.False(ran); // RunInline=false, so the inner fake did not execute it.
        }

        [Fact]
        public void UnrelatedWindowCreation_DoesNotTakeOverTheActiveDispatcher()
        {
            var switchable = new SwitchableUiDispatcher();
            var controlCenter = new object();
            var ccDispatcher = new FakeUiDispatcher();
            var chartDispatcher = new FakeUiDispatcher();

            Assert.True(HostOnWindowCreated(switchable, controlCenter, isControlCenter: true, ccDispatcher));

            // A chart window is created: the host hook ignores it, and even a direct attach is
            // refused because a live dispatcher is already owned by the Control Center.
            Assert.False(HostOnWindowCreated(switchable, new object(), isControlCenter: false, chartDispatcher));
            Assert.False(switchable.Attach(new object(), chartDispatcher));

            Assert.Same(controlCenter, switchable.Owner);
            Assert.True(switchable.HasTarget);
        }

        [Fact]
        public void UnrelatedWindowDestruction_DoesNotDetachTheActiveDispatcher()
        {
            var switchable = new SwitchableUiDispatcher();
            var controlCenter = new object();
            var chart = new object();
            var ccDispatcher = new FakeUiDispatcher();
            switchable.Attach(controlCenter, ccDispatcher);

            // Destroying an unrelated window must be a no-op for the Diagnostic Center.
            Assert.False(HostOnWindowDestroyed(switchable, chart));

            Assert.True(switchable.HasTarget);
            Assert.Same(controlCenter, switchable.Owner);

            Assert.True(switchable.Post(() => { }));
            Assert.NotEmpty(ccDispatcher.PostingThreads); // still routed through the Control Center
        }

        [Fact]
        public void OwningWindowDestruction_DetachesTheDispatcher()
        {
            var switchable = new SwitchableUiDispatcher();
            var controlCenter = new object();
            var ccDispatcher = new FakeUiDispatcher { RunInline = false };
            switchable.Attach(controlCenter, ccDispatcher);

            Assert.True(HostOnWindowDestroyed(switchable, controlCenter));

            Assert.False(switchable.HasTarget);
            Assert.Null(switchable.Owner);

            // No target: notifications run inline again rather than being dropped.
            var ran = false;
            Assert.True(switchable.Post(() => ran = true));
            Assert.True(ran);
        }

        [Fact]
        public void HostHookSequence_CreateChart_DestroyChart_DestroyControlCenter()
        {
            var switchable = new SwitchableUiDispatcher();
            var controlCenter = new object();
            var chart = new object();
            var ccDispatcher = new FakeUiDispatcher();

            // Control Center appears first and owns the dispatcher.
            Assert.True(HostOnWindowCreated(switchable, controlCenter, isControlCenter: true, ccDispatcher));

            // A chart is created then destroyed: the dispatcher must survive both.
            Assert.False(HostOnWindowCreated(switchable, chart, isControlCenter: false, new FakeUiDispatcher()));
            Assert.False(HostOnWindowDestroyed(switchable, chart));
            Assert.True(switchable.HasTarget);
            Assert.Same(controlCenter, switchable.Owner);

            // The Control Center is destroyed: now the dispatcher is released.
            Assert.True(HostOnWindowDestroyed(switchable, controlCenter));
            Assert.False(switchable.HasTarget);
        }

        [Fact]
        public void RepeatedAttach_ForTheSameOwner_IsIdempotent_AndRefreshesTarget()
        {
            var switchable = new SwitchableUiDispatcher();
            var controlCenter = new object();
            var first = new FakeUiDispatcher();
            var second = new FakeUiDispatcher { RunInline = false };

            Assert.True(switchable.Attach(controlCenter, first));
            Assert.True(switchable.Attach(controlCenter, second)); // repeated host callback

            Assert.True(switchable.HasTarget);
            Assert.Same(controlCenter, switchable.Owner);

            Assert.True(switchable.Post(() => { }));
            Assert.NotEmpty(second.PostingThreads); // the refreshed target is now used
            Assert.Empty(first.PostingThreads);
        }

        [Fact]
        public void RepeatedDetach_IsSafe()
        {
            var switchable = new SwitchableUiDispatcher();
            var controlCenter = new object();
            switchable.Attach(controlCenter, new FakeUiDispatcher());

            Assert.True(switchable.Detach(controlCenter));
            Assert.False(switchable.Detach(controlCenter)); // second detach is a harmless no-op
            Assert.False(switchable.HasTarget);
        }

        [Fact]
        public void Attach_ShutDownDispatcher_IsRefused_AndKeepsTheExistingTarget()
        {
            var switchable = new SwitchableUiDispatcher();
            var controlCenter = new object();
            var live = new FakeUiDispatcher();
            switchable.Attach(controlCenter, live);

            var dead = new FakeUiDispatcher { ShutdownStarted = true };
            Assert.False(switchable.Attach(new object(), dead));

            Assert.Same(controlCenter, switchable.Owner);
            Assert.True(switchable.HasTarget);
        }

        [Fact]
        public void Attach_ReplacesAShutDownTarget_OwnedByAnotherWindow()
        {
            var switchable = new SwitchableUiDispatcher();
            var oldControlCenter = new object();
            var oldDispatcher = new FakeUiDispatcher();
            switchable.Attach(oldControlCenter, oldDispatcher);

            // The old Control Center's dispatcher begins shutting down (e.g. workspace close).
            oldDispatcher.ShutdownStarted = true;

            // A re-created Control Center may take over from the dead one.
            var newControlCenter = new object();
            var newDispatcher = new FakeUiDispatcher();
            Assert.True(switchable.Attach(newControlCenter, newDispatcher));
            Assert.Same(newControlCenter, switchable.Owner);
        }

        [Fact]
        public void UnconditionalDetach_ClearsOwner_AndAllowsReattach_AfterTermination()
        {
            var switchable = new SwitchableUiDispatcher();
            var controlCenter = new object();
            switchable.Attach(controlCenter, new FakeUiDispatcher());
            Assert.True(switchable.HasTarget);

            // AddOn termination: unconditional detach clears both target and owner, so no stale
            // reference survives an AddOn reload.
            switchable.Detach();
            Assert.False(switchable.HasTarget);
            Assert.Null(switchable.Owner);

            // Reinitialisation: a fresh owner can attach again.
            var reinitialised = new object();
            Assert.True(switchable.Attach(reinitialised, new FakeUiDispatcher()));
            Assert.Same(reinitialised, switchable.Owner);
        }

        [Fact]
        public void HasShutdownStarted_IsFalse_WhenNoTargetAttached()
        {
            var switchable = new SwitchableUiDispatcher();
            // "No target yet" must not read as "shutting down", or pre-window notifications
            // would be dropped instead of run inline.
            Assert.False(switchable.HasShutdownStarted);
        }

        [Fact]
        public void HasShutdownStarted_ReflectsTheAttachedTarget()
        {
            var switchable = new SwitchableUiDispatcher();
            var inner = new FakeUiDispatcher();
            switchable.Attach(new object(), inner);
            Assert.False(switchable.HasShutdownStarted);

            inner.ShutdownStarted = true;
            Assert.True(switchable.HasShutdownStarted);
        }

        // ---- WpfUiDispatcher over the offline dispatcher stub -------------------------------

        [Fact]
        public void WpfUiDispatcher_NullDispatcher_ReportsShutdown_AndRefusesPosts()
        {
            var dispatcher = new WpfUiDispatcher(null);
            Assert.True(dispatcher.HasShutdownStarted);
            Assert.False(dispatcher.OnDispatcherThread);
            Assert.False(dispatcher.Post(() => { }));
        }

        [Fact]
        public void WpfUiDispatcher_ShutdownFlags_AreBothHonoured()
        {
            var raw = new Dispatcher();
            var dispatcher = new WpfUiDispatcher(raw);
            Assert.False(dispatcher.HasShutdownStarted);

            raw.HasShutdownStarted = true;
            Assert.True(dispatcher.HasShutdownStarted);

            raw.HasShutdownStarted = false;
            raw.HasShutdownFinished = true;
            Assert.True(dispatcher.HasShutdownStarted);
        }

        [Fact]
        public void WpfUiDispatcher_Post_IsRefused_WhileShuttingDown()
        {
            var raw = new Dispatcher { HasShutdownStarted = true };
            var dispatcher = new WpfUiDispatcher(raw);

            var ran = false;
            Assert.False(dispatcher.Post(() => ran = true));
            Assert.False(ran);
        }

        [Fact]
        public void WpfUiDispatcher_Post_RunsInline_OnTheDispatcherThread()
        {
            var raw = new Dispatcher { OnDispatcherThread = true };
            var dispatcher = new WpfUiDispatcher(raw);

            var ran = false;
            Assert.True(dispatcher.Post(() => ran = true));
            Assert.True(ran);
        }

        [Fact]
        public void WpfUiDispatcher_Post_Marshals_WhenOffTheDispatcherThread()
        {
            var raw = new Dispatcher { OnDispatcherThread = false };
            var dispatcher = new WpfUiDispatcher(raw);

            var ran = false;
            Assert.True(dispatcher.Post(() => ran = true));
            Assert.True(ran); // the stub InvokeAsync runs the posted action
        }

        [Fact]
        public void WpfUiDispatcher_Post_IsContained_WhenTheDispatcherShutsDownMidPost()
        {
            // The dispatcher can begin shutting down between the flag check and InvokeAsync; a
            // real WPF dispatcher throws there. The post must be reported as not queued, never
            // fault the caller (the run worker).
            var raw = new Dispatcher { OnDispatcherThread = false, ThrowOnInvokeAsync = true };
            var dispatcher = new WpfUiDispatcher(raw);

            Assert.False(dispatcher.Post(() => { }));
        }

        [Fact]
        public void WpfUiDispatcher_Post_NullAction_ReturnsFalse()
        {
            var dispatcher = new WpfUiDispatcher(new Dispatcher());
            Assert.False(dispatcher.Post(null));
        }

        // ---- Thread safety ------------------------------------------------------------------

        [Fact]
        public void ConcurrentAttachDetachPost_DoesNotThrow_AndKeepsInvariants()
        {
            var switchable = new SwitchableUiDispatcher();
            var owner = new object();
            var errors = new List<Exception>();
            var start = new ManualResetEventSlim(false);

            var threads = new List<Thread>();
            for (var i = 0; i < 8; i++)
            {
                var thread = new Thread(() =>
                {
                    start.Wait();
                    try
                    {
                        for (var n = 0; n < 2000; n++)
                        {
                            switchable.Attach(owner, new FakeUiDispatcher());
                            switchable.Post(() => { });
                            // An unrelated window's detach must always be a no-op.
                            Assert.False(switchable.Detach(new object()));
                            Assert.True(switchable.HasTarget);
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
                Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
            }

            Assert.Empty(errors);
            Assert.True(switchable.HasTarget); // the owner is still attached
            Assert.Same(owner, switchable.Owner);
        }
    }
}
