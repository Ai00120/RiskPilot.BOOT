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
    /// present, records the process architecture and CLR/target framework, records the AddOn
    /// lifecycle states observed, and builds a capability matrix of what is available versus
    /// unobservable. The exact NinjaTrader runtime version is explicitly unobservable: it is
    /// never guessed from a file name or build id and is never reported as PASS.
    /// </summary>
    public sealed class M1EnvironmentProbe : IDiagnosticProbe
    {
        public string ModuleId { get { return "M1"; } }

        public string Title { get { return "Environment & API Compatibility"; } }

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
            results.Add(BuildArchitectureCheck(runtime, correlation, now));
            results.Add(BuildClrCheck(runtime, correlation, now));
            results.Add(BuildLifecycleCheck(runtime, correlation, now));
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
                .WithNextAction(is64 ? "None." : "NinjaTrader 8.1.8.4 is expected to be 64-bit; confirm the host installation.")
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

        private DiagnosticResult BuildLifecycleCheck(RuntimeObservation runtime, string correlation, DateTime now)
        {
            var states = runtime != null && runtime.LifecycleStates != null
                ? runtime.LifecycleStates
                : new List<string>();

            if (states.Count == 0)
            {
                return DiagnosticResult
                    .Inconclusive(ModuleId, "M1.LIFECYCLE", correlation, now)
                    .WithFindingId("M1.LIFECYCLE")
                    .WithReasonCode("LIFECYCLE_NOT_OBSERVED")
                    .WithObservation("No AddOn lifecycle state was recorded for this pass.")
                    .WithSource(FindingFactory.SourcePublicApi)
                    .WithMethod("AddOn OnStateChange transitions recorded by the host layer.")
                    .WithLimitation("Only states observed so far in this session are reported.")
                    .WithNextAction("Ensure the AddOn reached Active before running the diagnostic pass.");
            }

            var joined = string.Join(" -> ", states);
            return DiagnosticResult
                .Pass(ModuleId, "M1.LIFECYCLE", correlation, now)
                .WithFindingId("M1.LIFECYCLE")
                .WithReasonCode("LIFECYCLE_OBSERVED")
                .WithObservation("states=" + joined)
                .WithSource(FindingFactory.SourcePublicApi)
                .WithMethod("AddOn OnStateChange transitions recorded by the host layer.")
                .WithLimitation("Records observed transitions only; a skipped state is reported as skipped, not inferred.")
                .WithNextAction("None.")
                .WithEvidence(DiagnosticEvidence.Observable(FindingFactory.SourcePublicApi, "states=" + joined, now));
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
