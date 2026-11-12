using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// The three independent acceptance subjects. They are reported separately and never
    /// collapsed into one verdict: a green tool build is not a green trading repository.
    /// </summary>
    public enum AcceptanceSubject
    {
        /// <summary>(1) The AddOn itself: does it build and is it installed/loaded.</summary>
        AddOnImplementation = 0,

        /// <summary>(2) Runtime diagnostic coverage and evidence quality.</summary>
        RuntimeDiagnosticCoverage = 1,

        /// <summary>(3) RiskPilot.BOT acceptance.</summary>
        RiskPilotBotAcceptance = 2
    }

    /// <summary>One independently reported acceptance subject.</summary>
    public sealed class AcceptanceSubjectResult
    {
        public AcceptanceSubject Subject { get; set; }

        public string Title { get; set; }

        public DiagnosticStatus Status { get; set; }

        public string Summary { get; set; }

        public AcceptanceSubjectResult()
        {
            Title = string.Empty;
            Summary = string.Empty;
        }
    }

    /// <summary>
    /// Builds the three-subject acceptance summary from a run result. Subject 1 (AddOn
    /// implementation/build) is derived from the build mode and whether modules executed.
    /// Subject 2 (runtime coverage/evidence quality) is derived from the module statuses and
    /// the session's data-integrity counters. Subject 3 (RiskPilot.BOT acceptance) is
    /// ALWAYS <see cref="DiagnosticStatus.NOT_APPLICABLE"/> from inside this tool: the tool
    /// has no execution dependency on RiskPilot.BOT and can never certify it.
    /// </summary>
    public static class AcceptanceSummaryBuilder
    {
        public static IReadOnlyList<AcceptanceSubjectResult> Build(DiagnosticRunResult result, bool stubBuild)
        {
            var list = new List<AcceptanceSubjectResult>();

            var executedModules = 0;
            if (result != null)
            {
                foreach (var module in result.Modules)
                {
                    if (module.Executed)
                    {
                        executedModules++;
                    }
                }
            }

            // (1) AddOn implementation / build.
            var buildStatus = executedModules > 0 ? DiagnosticStatus.PASS : DiagnosticStatus.INCONCLUSIVE;
            if (stubBuild)
            {
                // A stub build proves the source compiles offline; it never proves host
                // compatibility, so the AddOn subject stays INCONCLUSIVE.
                buildStatus = DiagnosticStatus.INCONCLUSIVE;
            }

            list.Add(new AcceptanceSubjectResult
            {
                Subject = AcceptanceSubject.AddOnImplementation,
                Title = "AddOn implementation / build",
                Status = buildStatus,
                Summary = stubBuild
                    ? "Compiled against offline stubs. This proves the source compiles, NOT that the AddOn builds or runs against a real NinjaTrader 8 host."
                    : "Compiled against the real host assemblies and executed " + executedModules + " module(s)."
            });

            // (2) Runtime diagnostic coverage and evidence quality.
            var coverageStatus = DiagnosticStatus.INCONCLUSIVE;
            if (result != null && executedModules > 0)
            {
                coverageStatus = result.AggregateStatus == DiagnosticStatus.FAIL
                    ? DiagnosticStatus.FAIL
                    : (result.AggregateStatus == DiagnosticStatus.PASS
                        ? DiagnosticStatus.PASS
                        : DiagnosticStatus.INCONCLUSIVE);
            }

            list.Add(new AcceptanceSubjectResult
            {
                Subject = AcceptanceSubject.RuntimeDiagnosticCoverage,
                Title = "Runtime diagnostic coverage / evidence quality",
                Status = coverageStatus,
                Summary = result == null
                    ? "No run has completed yet."
                    : "Modules executed=" + executedModules + ", aggregate=" + result.AggregateStatus.ToWire()
                      + ". Missing or unobservable data is reported as INCONCLUSIVE, never PASS."
            });

            // (3) RiskPilot.BOT acceptance — never certified by this tool.
            list.Add(new AcceptanceSubjectResult
            {
                Subject = AcceptanceSubject.RiskPilotBotAcceptance,
                Title = "RiskPilot.BOT acceptance",
                Status = DiagnosticStatus.NOT_APPLICABLE,
                Summary = "Out of scope. This tool is independent of RiskPilot.BOT and cannot certify it. A successful AddOn build or diagnostic run is NOT a RiskPilot.BOT acceptance."
            });

            return list;
        }
    }
}
