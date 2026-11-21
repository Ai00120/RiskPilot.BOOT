using System;
using System.Collections.Generic;
using System.Threading;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Security;
using RiskPilot.Diagnostics.Nt8.Core.Run;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// The Full-mode readiness wait added for the D-097 acceptance run: a Full pass started right
    /// after <c>Active</c> must wait (bounded) until the host facts settle, while Quick and
    /// Post-Trade Audit keep their previous immediate semantics. Waiting is only a delay; it must
    /// never change a diagnostic verdict (a FAIL stays FAIL).
    /// </summary>
    public class ReadinessWaitTests
    {
        private static DiagnosticRuntime Runtime(FakeDataProvider provider)
        {
            var runtime = new DiagnosticRuntime(provider, new FakeClock(DateTime.UtcNow), 256, 64, 256);
            runtime.Initialize();
            return runtime;
        }

        private static DiagnosticRunContext Context(DiagnosticMode mode, Func<bool> isCancelled = null)
        {
            return new DiagnosticRunContext(
                mode,
                new[] { "M1", "M2" },
                isCancelled,
                null,
                null,
                null);
        }

        private static HostReadinessObservation NotReady()
        {
            return new HostReadinessObservation
            {
                SurfaceObservable = true,
                InstrumentResolved = ReadinessFactState.Unsatisfied,
                FullyReady = false
            };
        }

        private static HostReadinessObservation Ready()
        {
            return new HostReadinessObservation
            {
                SurfaceObservable = true,
                HostOperational = ReadinessFactState.Satisfied,
                HostAvailable = ReadinessFactState.Satisfied,
                MetadataReadAllowed = ReadinessFactState.Satisfied,
                InstrumentResolved = ReadinessFactState.Satisfied,
                MetadataObserved = ReadinessFactState.Satisfied,
                AccountVerified = ReadinessFactState.Satisfied,
                ConnectionVerified = ReadinessFactState.Satisfied,
                MarketDataSubscribed = ReadinessFactState.Satisfied,
                MarketDataObserved = ReadinessFactState.Satisfied,
                FullyReady = true
            };
        }

        [Fact]
        public void Full_WaitsUntilReady_ThenRuns()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider);

            var polls = 0;
            var sleeps = 0;
            var runner = new RuntimeDiagnosticRunner(
                runtime,
                new InMemoryEvidenceSink(),
                null,
                null,
                () =>
                {
                    polls++;
                    return polls < 3 ? NotReady() : Ready();
                },
                5000,
                1,
                _ => { sleeps++; Thread.Sleep(1); });

            var result = runner.Run(Context(DiagnosticMode.Full));

            Assert.Equal(3, polls);
            Assert.Equal(2, sleeps);
            Assert.Equal(RunState.Completed, result.FinalState);
            Assert.NotNull(result.Report);
        }

        [Fact]
        public void Full_WithZeroBudget_DoesNotWait()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider);

            var polls = 0;
            var runner = new RuntimeDiagnosticRunner(
                runtime,
                new InMemoryEvidenceSink(),
                null,
                null,
                () => { polls++; return NotReady(); },
                0);

            var result = runner.Run(Context(DiagnosticMode.Full));

            Assert.Equal(0, polls);
            Assert.Equal(RunState.Completed, result.FinalState);
        }

        [Fact]
        public void Full_StopsAtTheBoundedTimeout()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider);

            var polls = 0;
            var runner = new RuntimeDiagnosticRunner(
                runtime,
                new InMemoryEvidenceSink(),
                null,
                null,
                () => { polls++; return NotReady(); },
                20,
                1,
                _ => Thread.Sleep(1));

            var result = runner.Run(Context(DiagnosticMode.Full));

            Assert.True(polls > 0);
            Assert.Equal(RunState.Completed, result.FinalState);
        }

        [Fact]
        public void Quick_DoesNotConsultTheReadinessProbe()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider);

            var runner = new RuntimeDiagnosticRunner(
                runtime,
                new InMemoryEvidenceSink(),
                null,
                null,
                () => throw new InvalidOperationException("Quick must not wait for readiness"),
                5000);

            var result = runner.Run(Context(DiagnosticMode.Quick));

            Assert.Equal(RunState.Completed, result.FinalState);
        }

        [Fact]
        public void PostTradeAudit_DoesNotConsultTheReadinessProbe()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider);

            var runner = new RuntimeDiagnosticRunner(
                runtime,
                new InMemoryEvidenceSink(),
                null,
                null,
                () => throw new InvalidOperationException("Post-Trade Audit must not wait for readiness"),
                5000);

            var result = runner.Run(Context(DiagnosticMode.PostTradeAudit));

            Assert.Equal(RunState.Completed, result.FinalState);
        }

        [Fact]
        public void Full_CancelledBeforeTheWait_DoesNotPoll()
        {
            var provider = new FakeDataProvider();
            using var runtime = Runtime(provider);

            var polls = 0;
            var runner = new RuntimeDiagnosticRunner(
                runtime,
                new InMemoryEvidenceSink(),
                null,
                null,
                () => { polls++; return NotReady(); },
                5000);

            var result = runner.Run(Context(DiagnosticMode.Full, () => true));

            Assert.Equal(0, polls);
            Assert.Equal(RunState.Cancelled, result.FinalState);
        }

        [Fact]
        public void Full_WaitNeverTurnsAFailIntoAPass()
        {
            var failing = new AccountIdentityObservation
            {
                AccountSurfaceObservable = true,
                ObservedAccountCount = 1,
                MatchingAccountCount = 1,
                AccountRef = SyntheticRef.For("account", "SimRP.BOT"),
                AccountProviderObservable = true,
                AccountProviderMatch = ProviderIdentityMatch.Differs,
                ConnectionObserved = true,
                ConnectionNameObservable = true,
                ConnectionNameMatches = true,
                ConnectionProviderObservable = true,
                ConnectionProviderMatch = ProviderIdentityMatch.Differs,
                ConnectionStatusObservable = true,
                ConnectionConnected = true,
                ModeObservable = true,
                ModeIsSimulation = true,
                DenominationObservable = true,
                DenominationIsUsd = true
            };

            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { AccountIdentity = failing }
            };

            using var runtime = Runtime(provider);
            var runner = new RuntimeDiagnosticRunner(
                runtime,
                new InMemoryEvidenceSink(),
                null,
                null,
                Ready,
                5000);

            var result = runner.Run(Context(DiagnosticMode.Full));
            var identity = Find(result, "M2.ACCOUNT_IDENTITY");

            Assert.Equal(DiagnosticStatus.FAIL, identity.Status);
        }

        private static DiagnosticResult Find(DiagnosticRunResult result, string checkId)
        {
            foreach (var item in result.Report.Results)
            {
                if (item.CheckId == checkId)
                {
                    return item;
                }
            }

            throw new InvalidOperationException("check not found: " + checkId);
        }
    }
}
