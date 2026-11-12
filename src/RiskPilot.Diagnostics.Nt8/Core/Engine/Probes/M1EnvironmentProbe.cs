using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine.Probes
{
    /// <summary>
    /// M1 — Environment &amp; API compatibility. Verifies that the required public APIs
    /// are present and records the runtime environment. The exact NinjaTrader runtime
    /// version is explicitly unobservable and is never reported as PASS.
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

            var missing = context.DataProvider.GetMissingApis() ?? new List<string>();
            var present = context.DataProvider.GetPresentApis() ?? new List<string>();

            var apiCheck = missing.Count == 0
                ? DiagnosticResult.Pass(ModuleId, "M1.API_SURFACE", correlation, now)
                : DiagnosticResult.Fail(ModuleId, "M1.API_SURFACE", ErrorClass.HostUnavailable, correlation, now);

            apiCheck.WithReasonCode(missing.Count == 0 ? "ALL_REQUIRED_APIS_PRESENT" : "REQUIRED_APIS_MISSING")
                .WithEvidence(
                    DiagnosticEvidence.Observable("NinjaTrader Public API", "present=" + present.Count, now),
                    DiagnosticEvidence.Observable("NinjaTrader Public API", "missing=" + missing.Count, now))
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
                .WithEvidence(DiagnosticEvidence.Observable("NinjaTrader API", "environment=" + environment.Environment.ToWire(), now))
                .WithRemediation(environmentObserved
                    ? "Environment class recorded from the running host."
                    : "No account exposed the environment class; it cannot be proven.");
            results.Add(envCheck);

            var versionCheck = DiagnosticResult.Inconclusive(ModuleId, "M1.HOST_VERSION", correlation, now)
                .WithReasonCode("HOST_VERSION_UNOBSERVABLE")
                .WithEvidence(DiagnosticEvidence.Unobservable("NinjaTrader Public API", "NO_PUBLIC_API_FOR_HOST_VERSION", now))
                .WithRemediation("Host version cannot be proven through public APIs; confirm manually in Help > About.");
            results.Add(versionCheck);

            var buildCheck = DiagnosticResult.Pass(ModuleId, "M1.BUILD_MODE", correlation, now)
                .WithReasonCode("BUILD_MODE=" + BuildIdentity.BuildMode)
                .WithEvidence(DiagnosticEvidence.Observable("Assembly", "buildMode=" + BuildIdentity.BuildMode, now));
            if (BuildIdentity.IsStubBuild)
            {
                buildCheck = DiagnosticResult.Inconclusive(ModuleId, "M1.BUILD_MODE", correlation, now)
                    .WithReasonCode("STUB_BUILD_REAL_HOST_NOT_VERIFIED")
                    .WithEvidence(DiagnosticEvidence.Unobservable("Assembly", "compiled against offline stubs", now))
                    .WithRemediation("Compile against real NinjaTrader assemblies to establish host compatibility.");
            }

            results.Add(buildCheck);
            return results;
        }
    }
}
