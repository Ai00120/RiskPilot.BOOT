using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Probes
{
    /// <summary>
    /// M1 — Environment &amp; API compatibility. Verifies that the required public APIs are
    /// present, records the process architecture and CLR/target framework, records and validates
    /// the AddOn lifecycle states observed (initialization / startup / runtime / shutdown), and
    /// builds a capability matrix of what is available versus unobservable. The exact NinjaTrader
    /// runtime version is explicitly unobservable: it is never guessed from a file name or build id
    /// and is never reported as PASS.
    /// <para>
    /// M1 is the single owner of the AddOn lifecycle surface. The connection phase is not an AddOn
    /// <c>State</c> member, so it is explicitly deferred to M2 (the connection/provider owner)
    /// rather than duplicated here.
    /// </para>
    /// </summary>
    public sealed class M1EnvironmentProbe : IDiagnosticProbe
    {
        public string ModuleId { get { return "M1"; } }

        public string Title { get { return "Environment, API & Lifecycle Compatibility"; } }

        public IReadOnlyList<DiagnosticResult> Evaluate(ProbeContext context)
        {
            var results = new List<DiagnosticResult>();
            var now = context.Clock.UtcNow;
            var correlation = context.CorrelationId;
            var snapshot = context.Snapshot;
            var runtime = snapshot != null ? snapshot.Runtime : null;

            var missing = context.DataProvider.GetMissingApis() ?? new List<string>();
            var present = context.DataProvider.GetPresentApis() ?? new List<string>();

            var apiCheck = missing.Count == 0
                ? DiagnosticResult.Pass(ModuleId, "M1.API_SURFACE", correlation, now)
                : DiagnosticResult.Fail(ModuleId, "M1.API_SURFACE", ErrorClass.HostUnavailable, correlation, now);

            apiCheck.WithReasonCode(missing.Count == 0 ? "ALL_REQUIRED_APIS_PRESENT" : "REQUIRED_APIS_MISSING")
                .WithObservation("present=" + present.Count + " missing=" + missing.Count)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Compile-time type presence of documented public NinjaTrader types.")
                .WithLimitation("Presence is established at compile time; it does not exercise the API at runtime.")
                .WithNextAction(missing.Count == 0
                    ? "None."
                    : "Verify the host build provides the missing public types.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "present=" + present.Count, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "missing=" + missing.Count, now))
                .WithRemediation(missing.Count == 0
                    ? "Required API surface is available."
                    : "One or more required public APIs are absent; verify the host build.");
            results.Add(apiCheck);

            var environment = context.DataProvider.GetEnvironment() ?? new EnvironmentRecord();
            var environmentObserved = environment.Environment != EnvironmentClass.Unknown;
            var envCheck = (environmentObserved
                    ? DiagnosticResult.Pass(ModuleId, "M1.ENVIRONMENT_CLASS", correlation, now)
                    : DiagnosticResult.Inconclusive(ModuleId, "M1.ENVIRONMENT_CLASS", correlation, now))
                .WithReasonCode(environmentObserved ? "ENVIRONMENT_CLASS_OBSERVED" : "ENVIRONMENT_CLASS_UNKNOWN")
                .WithObservation("environment=" + environment.Environment.ToWire())
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Derived from the observed account name convention (Sim* = Simulation).")
                .WithLimitation("Environment class is inferred from an account name, not an explicit API.")
                .WithNextAction(environmentObserved
                    ? "None."
                    : "Connect an account so the environment class becomes observable.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "environment=" + environment.Environment.ToWire(), now))
                .WithRemediation(environmentObserved
                    ? "Environment class recorded from the running host."
                    : "No account exposed the environment class; it cannot be proven.");
            results.Add(envCheck);

            results.Add(BuildHostVersionCheck(correlation, now));
            results.Add(BuildHostCompatibilityCheck(correlation, now));
            results.Add(BuildArchitectureCheck(runtime, correlation, now));
            results.Add(BuildClrCheck(runtime, correlation, now));
            results.Add(BuildLifecycleCheck(snapshot, correlation, now));
            results.Add(BuildLifecyclePhasesCheck(snapshot, correlation, now));
            results.Add(BuildCapabilityCheck(runtime, correlation, now));
            results.Add(BuildBuildModeCheck(correlation, now));

            return results;
        }

        private DiagnosticResult BuildHostVersionCheck(string correlation, DateTime now)
        {
            return DiagnosticResult
                .Inconclusive(ModuleId, "M1.HOST_VERSION", correlation, now)
                .WithFindingId("M1.HOST_VERSION")
                .WithReasonCode("HOST_VERSION_UNOBSERVABLE")
                .WithObservation("NOT OBSERVED: no documented public API exposes the exact NinjaTrader host build.")
                .WithSource(FindingFactory.SourceUnobservable)
                .WithMethod("Reviewed the public NinjaScript surface for a host-version member; none is documented.")
                .WithLimitation("The host version cannot be proven from inside the process; it is never guessed from a file name or build id.")
                .WithNextAction("Confirm the host version manually in NinjaTrader Help > About and record it in the acceptance report.")
                .WithEvidence(DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi, "NO_PUBLIC_API_FOR_HOST_VERSION", now))
                .WithRemediation("Host version cannot be proven through public APIs; confirm manually in Help > About.");
        }

        /// <summary>
        /// Reports the declared NinjaTrader compatibility posture: the intended target version, the
        /// minimum supported version and whether any host build has actually been verified at
        /// runtime. It is INCONCLUSIVE unless a real-host run has been recorded, because no public
        /// API exposes the installed host build — a documented target is an intention, never a
        /// runtime fact. This is why changing a document can never make compatibility PASS.
        /// </summary>
        private DiagnosticResult BuildHostCompatibilityCheck(string correlation, DateTime now)
        {
            var verified = NinjaTraderCompatibility.CompatibilityVerifiedAtRuntime;
            var result = verified
                ? DiagnosticResult.Pass(ModuleId, "M1.HOST_COMPATIBILITY", correlation, now)
                : DiagnosticResult.Inconclusive(ModuleId, "M1.HOST_COMPATIBILITY", correlation, now);

            return result
                .WithFindingId("M1.HOST_COMPATIBILITY")
                .WithReasonCode(
                    "TARGET_HOST=" + NinjaTraderCompatibility.TargetVersion,
                    "MIN_SUPPORTED_HOST=" + NinjaTraderCompatibility.MinimumSupportedVersion,
                    "VERIFIED_AT_RUNTIME=" + (verified ? "YES" : "NO"),
                    "HOST_COMPATIBILITY_UNVERIFIED")
                .WithObservation(NinjaTraderCompatibility.CompatibilityStatement())
                .WithSource(FindingFactory.SourceUnobservable)
                .WithMethod("Reviewed the documented public NinjaScript surface and the declared compatibility constants; no public API exposes the installed host build.")
                .WithLimitation("The declared target version is a project intention, not a runtime fact; no public API exposes the installed host build, so compatibility cannot be proven from inside the process.")
                .WithNextAction("Compile against the real NinjaTrader assemblies and record the actual host build in the acceptance report.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "targetVersion=" + NinjaTraderCompatibility.TargetVersion, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "minimumSupportedVersion=" + NinjaTraderCompatibility.MinimumSupportedVersion, now),
                    DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi, "NO_PUBLIC_API_FOR_INSTALLED_HOST_BUILD", now))
                .WithRemediation("Real-host compatibility is not verified offline; compile and run on a Windows host with NinjaTrader installed.");
        }

        private DiagnosticResult BuildArchitectureCheck(RuntimeObservation runtime, string correlation, DateTime now)
        {
            if (runtime == null || !runtime.Is64BitProcess.HasValue)
            {
                return FindingFactory.Unobservable(ModuleId, "M1.PROCESS_ARCHITECTURE", correlation, now,
                    "Process bitness was not observable in this pass.",
                    "Run the diagnostic inside the NinjaTrader process and re-run.");
            }

            var is64 = runtime.Is64BitProcess.Value;
            return DiagnosticResult
                .Pass(ModuleId, "M1.PROCESS_ARCHITECTURE", correlation, now)
                .WithFindingId("M1.PROCESS_ARCHITECTURE")
                .WithReasonCode(is64 ? "PROCESS_64BIT" : "PROCESS_32BIT")
                .WithObservation("processBitness=" + (is64 ? "64-bit" : "32-bit"))
                .WithSource(FindingFactory.SourceClr)
                .WithMethod("Environment.Is64BitProcess.")
                .WithLimitation("Reports the tool process bitness, which equals the host process bitness when hosted by NinjaTrader.")
                .WithNextAction(is64
                    ? "None."
                    : "NinjaTrader " + NinjaTraderCompatibility.TargetVersion + " (" + NinjaTraderCompatibility.RequiredArchitecture + ") is expected; confirm the host installation.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourceClr, "is64BitProcess=" + is64, now));
        }

        private DiagnosticResult BuildClrCheck(RuntimeObservation runtime, string correlation, DateTime now)
        {
            var framework = EnvironmentRecordTargetFramework();
            if (runtime == null || string.IsNullOrEmpty(runtime.ClrVersion))
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M1.CLR_RUNTIME", correlation, now)
                    .WithFindingId("M1.CLR_RUNTIME")
                    .WithReasonCode("CLR_VERSION_UNOBSERVABLE")
                    .WithObservation("targetFramework=" + framework)
                    .WithSource(FindingFactory.SourceClr)
                    .WithMethod("Compile-time target framework constant.")
                    .WithLimitation("The CLR version string was not readable; only the compile-time target is known.")
                    .WithNextAction("None; the target framework is fixed at build time.");
            }

            return DiagnosticResult
                .Pass(ModuleId, "M1.CLR_RUNTIME", correlation, now)
                .WithFindingId("M1.CLR_RUNTIME")
                .WithReasonCode("CLR_VERSION_OBSERVED")
                .WithObservation("clr=" + runtime.ClrVersion + " targetFramework=" + framework)
                .WithSource(FindingFactory.SourceClr)
                .WithMethod("Environment.Version plus the compile-time target framework constant.")
                .WithLimitation("Environment.Version reports the CLR the tool is loaded into, not the NinjaTrader build.")
                .WithNextAction("None.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourceClr, "clr=" + runtime.ClrVersion, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourceClr, "targetFramework=" + framework, now));
        }

        /// <summary>
        /// Reports the ordered AddOn lifecycle transitions M1 observed and validates the sequence.
        /// The classification and the verdict are delegated to the host-free
        /// <see cref="Core.Identity.LifecycleVerifier"/>; this method only renders the result. A
        /// missing/unobservable surface is INCONCLUSIVE, an out-of-order or post-termination
        /// transition is FAIL, and only an ordered lifecycle that reached startup is PASS.
        /// </summary>
        private DiagnosticResult BuildLifecycleCheck(DiagnosticSnapshot snapshot, string correlation, DateTime now)
        {
            var observation = snapshot != null ? snapshot.Lifecycle : null;
            var verdict = Core.Identity.LifecycleVerifier.Evaluate(observation);

            if (verdict.Status == DiagnosticStatus.PASS)
            {
                return DiagnosticResult
                    .Pass(ModuleId, "M1.LIFECYCLE", correlation, now)
                    .WithFindingId("M1.LIFECYCLE")
                    .WithReasonCode(ToArray(verdict.ReasonCodes))
                    .WithObservation("states=" + Join(verdict.ObservedStates) + " transitions=" + verdict.TransitionCount
                        + " current=" + (string.IsNullOrEmpty(verdict.CurrentState) ? "UNKNOWN" : verdict.CurrentState))
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("AddOn OnStateChange transitions recorded by the host layer and classified by the host-free lifecycle verifier.")
                    .WithLimitation("Records observed transitions only; a state the host never reported is reported as not observed, never inferred.")
                    .WithNextAction("None.")
                    .WithEvidence(BuildLifecycleEvidence(verdict, now));
            }

            if (verdict.Status == DiagnosticStatus.FAIL)
            {
                return DiagnosticResult
                    .Fail(ModuleId, "M1.LIFECYCLE", ErrorClass.ConfigurationInvalid, correlation, now)
                    .WithFindingId("M1.LIFECYCLE")
                    .WithReasonCode(ToArray(verdict.ReasonCodes))
                    .WithObservation("states=" + Join(verdict.ObservedStates) + " transitions=" + verdict.TransitionCount
                        + " current=" + (string.IsNullOrEmpty(verdict.CurrentState) ? "UNKNOWN" : verdict.CurrentState))
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("AddOn OnStateChange transitions recorded by the host layer and classified by the host-free lifecycle verifier.")
                    .WithLimitation("An out-of-order transition or a state observed after Terminated is abnormal; the observed sequence is reported as-is.")
                    .WithNextAction("Investigate why the AddOn lifecycle transitioned abnormally (backwards, or after Terminated).")
                    .WithEvidence(BuildLifecycleEvidence(verdict, now));
            }

            return DiagnosticResult
                .Inconclusive(ModuleId, "M1.LIFECYCLE", correlation, now)
                .WithFindingId("M1.LIFECYCLE")
                .WithReasonCode(ToArray(verdict.ReasonCodes))
                .WithObservation("states=" + Join(verdict.ObservedStates) + " transitions=" + verdict.TransitionCount)
                .WithSource(observation == null || !observation.LifecycleSurfaceObservable
                    ? FindingFactory.SourceUnobservable
                    : FindingFactory.SourcePublicApi)
                .WithMethod("AddOn OnStateChange transitions recorded by the host layer and classified by the host-free lifecycle verifier.")
                .WithLimitation("A lifecycle that was not observed, or that could not be classified, is never reported as PASS.")
                .WithNextAction("Run the pass inside the host so the AddOn reaches Active and the lifecycle becomes observable.")
                .WithEvidence(BuildLifecycleEvidence(verdict, now));
        }

        /// <summary>
        /// Reports which lifecycle phases M1 actually observed. Initialization/startup/runtime are
        /// derived from the observed transitions; shutdown is optional (the diagnostic normally runs
        /// while the AddOn is live) and is only reported when observed. The connection phase is
        /// explicitly deferred to M2 — no NinjaTrader <c>State</c> member carries it, so it is never
        /// inferred here and is never reported as PASS by M1.
        /// </summary>
        private DiagnosticResult BuildLifecyclePhasesCheck(DiagnosticSnapshot snapshot, string correlation, DateTime now)
        {
            var observation = snapshot != null ? snapshot.Lifecycle : null;
            var verdict = Core.Identity.LifecycleVerifier.Evaluate(observation);

            var observed = new List<string>();
            if (verdict.InitializationObserved) observed.Add(Core.Identity.LifecyclePhases.Initialization);
            if (verdict.StartupObserved) observed.Add(Core.Identity.LifecyclePhases.Startup);
            if (verdict.RuntimeObserved) observed.Add(Core.Identity.LifecyclePhases.Runtime);
            if (verdict.ShutdownObserved) observed.Add(Core.Identity.LifecyclePhases.Shutdown);

            // Startup must have been reached, and the sequence must not be abnormal: a lifecycle
            // that transitioned out of order (or after Terminated) is not reported as a healthy
            // phase view, so the phase check stays INCONCLUSIVE rather than contradicting M1.LIFECYCLE.
            var result = verdict.StartupObserved && verdict.Status != DiagnosticStatus.FAIL
                ? DiagnosticResult.Pass(ModuleId, "M1.LIFECYCLE_PHASES", correlation, now)
                : DiagnosticResult.Inconclusive(ModuleId, "M1.LIFECYCLE_PHASES", correlation, now);

            return result
                .WithFindingId("M1.LIFECYCLE_PHASES")
                .WithReasonCode(
                    "PHASES_OBSERVED=" + observed.Count,
                    "INITIALIZATION=" + verdict.InitializationObserved,
                    "STARTUP=" + verdict.StartupObserved,
                    "RUNTIME=" + verdict.RuntimeObserved,
                    "SHUTDOWN=" + verdict.ShutdownObserved,
                    "CONNECTION=DEFERRED_TO_M2")
                .WithObservation("phases=" + Join(observed) + " connection=DEFERRED_TO_M2")
                .WithSource(observation == null || !observation.LifecycleSurfaceObservable
                    ? FindingFactory.SourceUnobservable
                    : FindingFactory.SourcePublicApi)
                .WithMethod("Classified the observed OnStateChange transitions into lifecycle phases.")
                .WithLimitation("The connection phase is not an AddOn State member and is owned by M2, so it is never inferred here; shutdown is optional while the tool is live.")
                .WithNextAction(verdict.StartupObserved
                    ? "None."
                    : "Ensure the AddOn reached Active before running the pass.")
                .WithEvidence(
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "initializationObserved=" + verdict.InitializationObserved, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "startupObserved=" + verdict.StartupObserved, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "runtimeObserved=" + verdict.RuntimeObserved, now),
                    DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "shutdownObserved=" + verdict.ShutdownObserved, now),
                    DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi, "connection phase is not an AddOn State member (M2 owns it)", now));
        }

        private static DiagnosticEvidence[] BuildLifecycleEvidence(Core.Identity.LifecycleVerdict verdict, DateTime now)
        {
            var evidence = new List<DiagnosticEvidence>();
            foreach (var code in verdict.ReasonCodes)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "reason=" + code, now));
            }

            foreach (var state in verdict.ObservedStates)
            {
                evidence.Add(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "state=" + state, now));
            }

            foreach (var state in verdict.UnknownStates)
            {
                evidence.Add(DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi, "unclassified state=" + state, now));
            }

            return evidence.ToArray();
        }

        private static string Join(IList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "none";
            }

            return string.Join(" -> ", values);
        }

        private static string[] ToArray(IList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return new string[0];
            }

            var array = new string[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                array[i] = values[i];
            }

            return array;
        }

        private DiagnosticResult BuildCapabilityCheck(RuntimeObservation runtime, string correlation, DateTime now)
        {
            var capabilities = runtime != null && runtime.Capabilities != null
                ? runtime.Capabilities
                : new List<RuntimeCapability>();

            if (capabilities.Count == 0)
            {
                return FindingFactory.Unobservable(ModuleId, "M1.CAPABILITY_MATRIX", correlation, now,
                    "No capability matrix was supplied by the host layer.",
                    "Run inside the host; the capability matrix is derived from the loaded public API surface.");
            }

            var available = 0;
            var unavailable = 0;
            var unobservable = 0;
            var evidence = new List<DiagnosticEvidence>();
            foreach (var capability in capabilities)
            {
                switch (capability.Availability)
                {
                    case CapabilityAvailability.Available:
                        available++;
                        break;
                    case CapabilityAvailability.Unavailable:
                        unavailable++;
                        break;
                    default:
                        unobservable++;
                        break;
                }

                evidence.Add(capability.Availability == CapabilityAvailability.Unobservable
                    ? DiagnosticEvidence.Unobservable(FindingFactory.SourcePublicApi, capability.Name + "=" + capability.Detail, now)
                    : DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, capability.Name + "=" + capability.Availability + " (" + capability.Detail + ")", now));
            }

            var status = unavailable > 0 ? DiagnosticStatus.FAIL : DiagnosticStatus.PASS;
            var result = FindingFactory.Check(ModuleId, "M1.CAPABILITY_MATRIX", status, correlation, now);
            if (unavailable > 0)
            {
                result = DiagnosticResult.Fail(ModuleId, "M1.CAPABILITY_MATRIX", ErrorClass.HostUnavailable, correlation, now);
            }

            return result
                .WithFindingId("M1.CAPABILITY_MATRIX")
                .WithReasonCode("CAPABILITIES_AVAILABLE=" + available, "CAPABILITIES_UNAVAILABLE=" + unavailable, "CAPABILITIES_UNOBSERVABLE=" + unobservable)
                .WithObservation("available=" + available + " unavailable=" + unavailable + " unobservable=" + unobservable)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("Probed the documented public API surface for each capability.")
                .WithLimitation("An unobservable capability cannot be exercised; it is never counted as available.")
                .WithNextAction(unavailable > 0
                    ? "Inspect the unavailable capabilities; a required one is missing from the host build."
                    : "None.")
                .WithEvidence(evidence.ToArray());
        }

        private DiagnosticResult BuildBuildModeCheck(string correlation, DateTime now)
        {
            var buildCheck = DiagnosticResult
                .Pass(ModuleId, "M1.BUILD_MODE", correlation, now)
                .WithFindingId("M1.BUILD_MODE")
                .WithReasonCode("BUILD_MODE=" + BuildIdentity.BuildMode)
                .WithObservation("buildMode=" + BuildIdentity.BuildMode)
                .WithSource("Assembly")
                .WithMethod("Compile-time build-mode constant.")
                .WithLimitation("The build mode describes the tool assembly, not the host.")
                .WithNextAction("None.")
                .WithEvidence(DiagnosticEvidence.Observable("Assembly", "buildMode=" + BuildIdentity.BuildMode, now));

            if (BuildIdentity.IsStubBuild)
            {
                buildCheck = DiagnosticResult
                    .Inconclusive(ModuleId, "M1.BUILD_MODE", correlation, now)
                    .WithFindingId("M1.BUILD_MODE")
                    .WithReasonCode("STUB_BUILD_REAL_HOST_NOT_VERIFIED")
                    .WithObservation("buildMode=StubOffline")
                    .WithSource("Assembly")
                    .WithMethod("Compile-time build-mode constant.")
                    .WithLimitation("This assembly was compiled against offline stubs; real host compatibility is NOT established.")
                    .WithNextAction("Compile against the real NinjaTrader assemblies to establish host compatibility.")
                    .WithEvidence(DiagnosticEvidence.Unobservable("Assembly", "compiled against offline stubs", now))
                    .WithRemediation("Compile against real NinjaTrader assemblies to establish host compatibility.");
            }

            return buildCheck;
        }

        private static string EnvironmentRecordTargetFramework()
        {
#if USE_STUBS
            return "net8.0";
#else
            return "net48";
#endif
        }
    }
}
