using System;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Shared helpers that give every module's findings a consistent shape: an id, a
    /// status, a severity, an observation, a source, a method, a limitation and a next
    /// action. Centralizing them keeps the "no PASS without positive observation" and
    /// "state the limitation" rules uniform across M1–M8.
    /// </summary>
    public static class FindingFactory
    {
        public const string SourcePublicApi = "NinjaTrader Public API";
        public const string SourceClr = "CLR Environment";
        public const string SourceStaticScan = "verification/addon-smoke (static)";
        public const string SourceRuntimeCounters = "RiskPilot.Diagnostics runtime counters";
        public const string SourceUnobservable = "Unobservable";

        public static DiagnosticResult Check(
            string moduleId,
            string checkId,
            DiagnosticStatus status,
            string correlationId,
            DateTime atUtc)
        {
            switch (status)
            {
                case DiagnosticStatus.PASS:
                    return DiagnosticResult.Pass(moduleId, checkId, correlationId, atUtc);
                case DiagnosticStatus.FAIL:
                    return DiagnosticResult.Fail(moduleId, checkId, ErrorClass.DataUnavailable, correlationId, atUtc);
                case DiagnosticStatus.NOT_APPLICABLE:
                    return DiagnosticResult.NotApplicable(moduleId, checkId, correlationId, atUtc);
                default:
                    return DiagnosticResult.Inconclusive(moduleId, checkId, correlationId, atUtc);
            }
        }

        /// <summary>
        /// A finding whose observation could not be made. Always INCONCLUSIVE and always
        /// carries the reason it could not be observed.
        /// </summary>
        public static DiagnosticResult Unobservable(
            string moduleId,
            string checkId,
            string correlationId,
            DateTime atUtc,
            string reason,
            string nextAction)
        {
            return DiagnosticResult
                .Inconclusive(moduleId, checkId, correlationId, atUtc)
                .WithObservation("NOT OBSERVED: " + reason)
                .WithSource(SourceUnobservable)
                .WithMethod("No documented public API exposed the value.")
                .WithLimitation(reason)
                .WithNextAction(nextAction)
                .WithReasonCode("UNOBSERVABLE")
                .WithEvidence(DiagnosticEvidence.Unobservable(SourceUnobservable, reason, atUtc));
        }
    }
}
