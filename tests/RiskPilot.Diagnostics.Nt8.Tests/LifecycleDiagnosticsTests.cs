using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript;
using RiskPilot.Diagnostics.Nt8.Core.Engine;
using RiskPilot.Diagnostics.Nt8.Core.Engine.Probes;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;
using RiskPilot.Diagnostics.Nt8.Host;
using RiskPilot.Diagnostics.Nt8.Tests.Fakes;
using Xunit;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Regression tests for the M1 lifecycle diagnostics: the host-free
    /// <see cref="LifecycleVerifier"/>, the M1 probe's <c>M1.LIFECYCLE</c> /
    /// <c>M1.LIFECYCLE_PHASES</c> checks, and the host adapter that reduces the AddOn
    /// <c>OnStateChange</c> transitions into a <see cref="LifecycleObservation"/>.
    /// <para>
    /// They cover valid and abnormal transitions, and assert the fail-closed contract: missing or
    /// unobservable lifecycle data is INCONCLUSIVE (never PASS), an out-of-order or
    /// post-termination transition is FAIL, and a stub/mock value can never establish the lifecycle.
    /// </para>
    /// </summary>
    public class LifecycleDiagnosticsTests
    {
        private static ProbeContext Context(FakeDataProvider provider)
        {
            var session = new SessionContext(SessionId.New(DateTime.UtcNow), DateTime.UtcNow);
            return new ProbeContext(provider, session, new FakeClock(DateTime.UtcNow), "corr00000000");
        }

        private static DiagnosticResult Find(IEnumerable<DiagnosticResult> results, string checkId)
        {
            foreach (var result in results)
            {
                if (result.CheckId == checkId)
                {
                    return result;
                }
            }

            throw new InvalidOperationException("check not found: " + checkId);
        }

        private static LifecycleObservation Observed(params string[] states)
        {
            var observation = new LifecycleObservation { LifecycleSurfaceObservable = true };
            foreach (var state in states)
            {
                observation.ObservedStates.Add(state);
            }

            return observation;
        }

        // ---------------- LifecycleVerifier ----------------

        [Fact]
        public void Verifier_OrderedLifecycle_ToStartup_IsPass()
        {
            var verdict = LifecycleVerifier.Evaluate(Observed("SetDefaults", "Active"));

            Assert.Equal(DiagnosticStatus.PASS, verdict.Status);
            Assert.True(verdict.InitializationObserved);
            Assert.True(verdict.StartupObserved);
            Assert.False(verdict.RuntimeObserved);
            Assert.False(verdict.ShutdownObserved);
            Assert.Equal("Active", verdict.CurrentState);
        }

        [Fact]
        public void Verifier_FullLifecycle_IncludingRuntimeAndShutdown_IsPass()
        {
            var verdict = LifecycleVerifier.Evaluate(
                Observed("SetDefaults", "Active", "DataLoaded", "Realtime", "Terminated"));

            Assert.Equal(DiagnosticStatus.PASS, verdict.Status);
            Assert.True(verdict.RuntimeObserved);
            Assert.True(verdict.ShutdownObserved);
            Assert.Equal("Terminated", verdict.CurrentState);
        }

        [Fact]
        public void Verifier_RepeatedSetDefaults_IsPass()
        {
            // SetDefaults legitimately fires more than once; a repeat is not a backwards move.
            var verdict = LifecycleVerifier.Evaluate(Observed("SetDefaults", "SetDefaults", "Active"));

            Assert.Equal(DiagnosticStatus.PASS, verdict.Status);
        }

        [Fact]
        public void Verifier_BackwardsTransition_IsFail()
        {
            var verdict = LifecycleVerifier.Evaluate(Observed("SetDefaults", "Active", "DataLoaded", "Active"));

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(LifecycleReasonCodes.OutOfOrder, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_StateAfterTermination_IsFail()
        {
            var verdict = LifecycleVerifier.Evaluate(Observed("SetDefaults", "Active", "Terminated", "Active"));

            Assert.Equal(DiagnosticStatus.FAIL, verdict.Status);
            Assert.Contains(LifecycleReasonCodes.StateAfterTermination, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_UnknownState_IsInconclusive()
        {
            var verdict = LifecycleVerifier.Evaluate(Observed("SetDefaults", "Active", "SomethingNew"));

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(LifecycleReasonCodes.UnknownState, verdict.ReasonCodes);
            Assert.Contains("SomethingNew", verdict.UnknownStates);
        }

        [Fact]
        public void Verifier_StartupNotObserved_IsInconclusive()
        {
            var verdict = LifecycleVerifier.Evaluate(Observed("SetDefaults"));

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(LifecycleReasonCodes.StartupNotObserved, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_InitializationNotObserved_IsInconclusive()
        {
            var verdict = LifecycleVerifier.Evaluate(Observed("Active"));

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(LifecycleReasonCodes.InitializationNotObserved, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_NoStates_IsInconclusive()
        {
            var verdict = LifecycleVerifier.Evaluate(Observed());

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(LifecycleReasonCodes.NotObserved, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_NullObservation_IsInconclusive()
        {
            var verdict = LifecycleVerifier.Evaluate(null);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(LifecycleReasonCodes.Unobservable, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_UnobservableSurface_IsInconclusive()
        {
            var verdict = LifecycleVerifier.Evaluate(new LifecycleObservation { LifecycleSurfaceObservable = false });

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(LifecycleReasonCodes.Unobservable, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_StubValue_IsInconclusive()
        {
            var observation = Observed("SetDefaults", "Active");
            observation.IsStubOrMockValue = true;

            var verdict = LifecycleVerifier.Evaluate(observation);

            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, verdict.Status);
            Assert.Contains(LifecycleReasonCodes.StubOrMock, verdict.ReasonCodes);
        }

        [Fact]
        public void Verifier_ConnectionPhase_IsNeverClaimed()
        {
            var verdict = LifecycleVerifier.Evaluate(Observed("SetDefaults", "Active"));

            // No State member carries the connection phase; it is always false here (M2 owns it).
            Assert.False(verdict.ConnectionPhaseObservable);
        }

        [Fact]
        public void PhaseMapping_IsClosedSwitch()
        {
            Assert.Equal(LifecyclePhases.Initialization, LifecyclePhases.PhaseOf("SetDefaults"));
            Assert.Equal(LifecyclePhases.Initialization, LifecyclePhases.PhaseOf("Configure"));
            Assert.Equal(LifecyclePhases.Startup, LifecyclePhases.PhaseOf("Active"));
            Assert.Equal(LifecyclePhases.Runtime, LifecyclePhases.PhaseOf("DataLoaded"));
            Assert.Equal(LifecyclePhases.Runtime, LifecyclePhases.PhaseOf("Realtime"));
            Assert.Equal(LifecyclePhases.Shutdown, LifecyclePhases.PhaseOf("Terminated"));
            Assert.Equal(LifecyclePhases.Unknown, LifecyclePhases.PhaseOf("NotAState"));
            Assert.False(LifecyclePhases.IsRecognized("NotAState"));
        }

        // ---------------- M1 probe ----------------

        [Fact]
        public void M1_Lifecycle_Valid_IsPass()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { Lifecycle = Observed("SetDefaults", "Active", "DataLoaded") }
            };

            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            var check = Find(results, "M1.LIFECYCLE");
            Assert.Equal(DiagnosticStatus.PASS, check.Status);
            Assert.Contains("SetDefaults -> Active -> DataLoaded", check.Observation);
        }

        [Fact]
        public void M1_Lifecycle_Abnormal_IsFail()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { Lifecycle = Observed("SetDefaults", "Active", "DataLoaded", "Active") }
            };

            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.FAIL, Find(results, "M1.LIFECYCLE").Status);
        }

        [Fact]
        public void M1_Lifecycle_NoSurface_IsInconclusive()
        {
            var provider = new FakeDataProvider();
            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M1.LIFECYCLE").Status);
        }

        [Fact]
        public void M1_LifecyclePhases_ReportsStartupAndDefersConnection()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { Lifecycle = Observed("SetDefaults", "Active", "DataLoaded") }
            };

            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            var check = Find(results, "M1.LIFECYCLE_PHASES");
            Assert.Equal(DiagnosticStatus.PASS, check.Status);
            Assert.Contains("INITIALIZATION", check.Observation);
            Assert.Contains("STARTUP", check.Observation);
            Assert.Contains("RUNTIME", check.Observation);
            Assert.Contains("DEFERRED_TO_M2", check.Observation);
        }

        [Fact]
        public void M1_LifecyclePhases_AbnormalLifecycle_IsInconclusive()
        {
            var provider = new FakeDataProvider
            {
                Snapshot = new DiagnosticSnapshot { Lifecycle = Observed("SetDefaults", "Active", "Terminated", "Active") }
            };

            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            Assert.Equal(DiagnosticStatus.INCONCLUSIVE, Find(results, "M1.LIFECYCLE_PHASES").Status);
        }

        [Fact]
        public void M1_Lifecycle_NoPassWithoutObservation()
        {
            // With an empty provider the lifecycle checks must never be PASS.
            var provider = new FakeDataProvider();
            var results = new M1EnvironmentProbe().Evaluate(Context(provider));
            Assert.NotEqual(DiagnosticStatus.PASS, Find(results, "M1.LIFECYCLE").Status);
            Assert.NotEqual(DiagnosticStatus.PASS, Find(results, "M1.LIFECYCLE_PHASES").Status);
        }

        // ---------------- Host adapter (offline stub surface) ----------------

        [Collection("StubHostState")]
        public class HostAdapter
        {
            [Fact]
            public void Host_LifecycleProbe_ReducesTransitions()
            {
                var states = new List<string> { "SetDefaults", "Active", "DataLoaded" };
                var provider = new Nt8HostDataProvider(() => true, () => states);

                var observation = provider.GetSnapshot().Lifecycle;

                Assert.True(observation.LifecycleSurfaceObservable);
                Assert.Equal(3, observation.ObservedStates.Count);
                Assert.Equal("SetDefaults", observation.ObservedStates[0]);
                Assert.Equal("DataLoaded", observation.ObservedStates[2]);
                Assert.Equal(DiagnosticStatus.PASS, LifecycleVerifier.Evaluate(observation).Status);
            }

            [Fact]
            public void Host_LifecycleProbe_IsLiveNotCaptured()
            {
                var states = new List<string> { "SetDefaults" };
                var provider = new Nt8HostDataProvider(() => true, () => states);

                Assert.Single(provider.GetSnapshot().Lifecycle.ObservedStates);

                // The host advances while the provider lives; a fresh read must reflect it.
                states.Add("Active");
                Assert.Equal(2, provider.GetSnapshot().Lifecycle.ObservedStates.Count);
            }

            [Fact]
            public void Host_NoLifecycleProbe_IsUnobservable()
            {
                var provider = new Nt8HostDataProvider(masterMetadataReady: false);

                var observation = provider.GetSnapshot().Lifecycle;

                Assert.False(observation.LifecycleSurfaceObservable);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, LifecycleVerifier.Evaluate(observation).Status);
            }

            [Fact]
            public void Host_ThrowingLifecycleProbe_FailsClosedToUnobservable()
            {
                var provider = new Nt8HostDataProvider(() => false, () => throw new InvalidOperationException("boom"));

                var observation = provider.GetSnapshot().Lifecycle;

                Assert.False(observation.LifecycleSurfaceObservable);
                Assert.Equal(DiagnosticStatus.INCONCLUSIVE, LifecycleVerifier.Evaluate(observation).Status);
            }

            [Fact]
            public void Host_LifecycleObservation_MatchesRealStubStateEnum()
            {
                // The states M1 classifies must be the actual NinjaTrader State members, so a
                // classification regression is caught against the stub enum.
                foreach (State state in Enum.GetValues(typeof(State)))
                {
                    Assert.True(LifecyclePhases.IsRecognized(state.ToString()),
                        "unrecognised State member: " + state);
                }
            }
        }
    }
}
