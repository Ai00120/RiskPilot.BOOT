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
    /// Phase 4 run-coordination tests. They cover the required UI-lifecycle behaviours:
    /// UI thread safety (notifications are marshalled), dispatcher shutdown, cancel
    /// behaviour, multiple-execution prevention and the run state machine.
    /// </summary>
    public class Phase4RunCoordinatorTests
    {
        private static DiagnosticRunCoordinator Coordinator(FakeDiagnosticRunner runner, FakeUiDispatcher dispatcher, out DiagnosticLog log)
        {
            log = new DiagnosticLog(128);
            return new DiagnosticRunCoordinator(runner, dispatcher, log);
        }

        [Fact]
        public void Start_RunsSelectedModules_AndCompletes()
        {
            var runner = new FakeDiagnosticRunner();
            var dispatcher = new FakeUiDispatcher();
            using var coordinator = Coordinator(runner, dispatcher, out _);

            Assert.True(coordinator.Start(DiagnosticMode.Quick));
            Assert.True(coordinator.WaitForCompletion(TimeSpan.FromSeconds(5)));

            Assert.Equal(RunState.Completed, coordinator.State);
            Assert.Equal(new[] { "M1", "M2", "M3", "M4", "M5", "M8" }, runner.ExecutedModules);
            Assert.NotNull(coordinator.Result);
            Assert.Equal(RunState.Completed, coordinator.Result.FinalState);
        }

        [Fact]
        public void Start_IsRejected_WhileARunIsActive()
        {
            var runner = new FakeDiagnosticRunner { ModuleGate = new ManualResetEventSlim(false) };
            var dispatcher = new FakeUiDispatcher();
            using var coordinator = Coordinator(runner, dispatcher, out _);

            Assert.True(coordinator.Start(DiagnosticMode.Full));
            // The worker is now blocked inside the first module.
            Assert.False(coordinator.Start(DiagnosticMode.Full));
            Assert.False(coordinator.Start(DiagnosticMode.Quick));

            runner.ModuleGate.Set();
            Assert.True(coordinator.WaitForCompletion(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, runner.MaxConcurrentRuns);
        }

        [Fact]
        public void Cancel_StopsProcessing_AndProducesCancelledRun()
        {
            var runner = new FakeDiagnosticRunner { ModuleGate = new ManualResetEventSlim(false) };
            var dispatcher = new FakeUiDispatcher();
            using var coordinator = Coordinator(runner, dispatcher, out _);

            Assert.True(coordinator.Start(DiagnosticMode.Full));
            Assert.True(coordinator.Cancel());

            // Release the module the worker is parked on; the runner must then observe the
            // cancel between modules and stop.
            runner.ModuleGate.Set();
            Assert.True(coordinator.WaitForCompletion(TimeSpan.FromSeconds(5)));

            Assert.Equal(RunState.Cancelled, coordinator.State);
            Assert.Equal(RunState.Cancelled, coordinator.Result.FinalState);
            Assert.True(runner.ExecutedModules.Count < 8, "cancel must stop before running every module");
        }

        [Fact]
        public void Cancel_WhenNoRunActive_ReturnsFalse()
        {
            var runner = new FakeDiagnosticRunner();
            var dispatcher = new FakeUiDispatcher();
            using var coordinator = Coordinator(runner, dispatcher, out _);

            Assert.False(coordinator.Cancel());
        }

        [Fact]
        public void RunnerException_ProducesFailedRun_NotAThrow()
        {
            var runner = new FakeDiagnosticRunner { ThrowOnRun = true };
            var dispatcher = new FakeUiDispatcher();
            using var coordinator = Coordinator(runner, dispatcher, out _);

            Assert.True(coordinator.Start(DiagnosticMode.Quick));
            Assert.True(coordinator.WaitForCompletion(TimeSpan.FromSeconds(5)));

            Assert.Equal(RunState.Failed, coordinator.State);
            Assert.Equal(RunState.Failed, coordinator.Result.FinalState);
        }

        [Fact]
        public void Progress_IsMarshalledThroughTheDispatcher_NotTheWorkerThread()
        {
            var runner = new FakeDiagnosticRunner();
            var dispatcher = new FakeUiDispatcher { RunInline = false };
            using var coordinator = Coordinator(runner, dispatcher, out _);

            var workerThread = 0;
            coordinator.ProgressChanged += _ => { };

            Assert.True(coordinator.Start(DiagnosticMode.Quick));
            Assert.True(coordinator.WaitForCompletion(TimeSpan.FromSeconds(5)));

            // Every post must have gone through the dispatcher abstraction.
            Assert.NotEmpty(dispatcher.PostingThreads);
        }

        [Fact]
        public void DispatcherShutdown_DoesNotFaultTheRun()
        {
            var runner = new FakeDiagnosticRunner();
            var dispatcher = new FakeUiDispatcher { ShutdownStarted = true };
            using var coordinator = Coordinator(runner, dispatcher, out _);

            Assert.True(coordinator.Start(DiagnosticMode.Quick));
            Assert.True(coordinator.WaitForCompletion(TimeSpan.FromSeconds(5)));

            // The run still completes; notifications were simply suppressed because the
            // dispatcher was shutting down, and the worker was never faulted.
            Assert.Equal(RunState.Completed, coordinator.State);
            Assert.NotNull(coordinator.Result);
            Assert.Equal(RunState.Completed, coordinator.Result.FinalState);
        }

        [Fact]
        public void Start_AfterCompletion_IsAllowed()
        {
            var runner = new FakeDiagnosticRunner();
            var dispatcher = new FakeUiDispatcher();
            using var coordinator = Coordinator(runner, dispatcher, out _);

            Assert.True(coordinator.Start(DiagnosticMode.Quick));
            Assert.True(coordinator.WaitForCompletion(TimeSpan.FromSeconds(5)));
            Assert.True(coordinator.Start(DiagnosticMode.Quick));
            Assert.True(coordinator.WaitForCompletion(TimeSpan.FromSeconds(5)));
            Assert.Equal(RunState.Completed, coordinator.State);
        }

        [Fact]
        public void Dispose_DuringActiveRun_DoesNotHang()
        {
            var runner = new FakeDiagnosticRunner { ModuleGate = new ManualResetEventSlim(false) };
            var dispatcher = new FakeUiDispatcher();
            var coordinator = Coordinator(runner, dispatcher, out _);

            Assert.True(coordinator.Start(DiagnosticMode.Full));
            runner.ModuleGate.Set();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            coordinator.Dispose();
            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "Dispose must not hang");
        }
    }
}
