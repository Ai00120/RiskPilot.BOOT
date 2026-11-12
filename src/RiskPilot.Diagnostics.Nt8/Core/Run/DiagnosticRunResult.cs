using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Report;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// The complete outcome of one diagnostic run, held by the view-model and used to drive
    /// the UI, the export action and the acceptance summary. It is a plain data object with
    /// no host types, so it can be built and asserted in tests.
    /// </summary>
    public sealed class DiagnosticRunResult
    {
        public DiagnosticMode Mode { get; set; }

        public string ModeId { get; set; }

        public string SessionId { get; set; }

        public string CorrelationId { get; set; }

        public DateTime StartedAtUtc { get; set; }

        public DateTime FinishedAtUtc { get; set; }

        public RunState FinalState { get; set; }

        /// <summary>The report produced by the pass (may hold partial results after a cancel).</summary>
        public DiagnosticReport Report { get; set; }

        /// <summary>Directory the evidence package was written to, or empty when not exported.</summary>
        public string EvidenceDirectory { get; set; }

        /// <summary>Ordered per-module statuses for the status grid.</summary>
        public IList<ModuleStatusView> Modules { get; set; }

        public DiagnosticRunResult()
        {
            ModeId = string.Empty;
            SessionId = string.Empty;
            CorrelationId = string.Empty;
            Modules = new List<ModuleStatusView>();
            EvidenceDirectory = string.Empty;
        }

        /// <summary>Aggregate status across the modules, using fail-closed precedence.</summary>
        public DiagnosticStatus AggregateStatus
        {
            get
            {
                var status = DiagnosticStatus.NOT_APPLICABLE;
                foreach (var module in Modules)
                {
                    status = status.Worst(module.Status);
                }

                return status;
            }
        }

        public double DurationSeconds
        {
            get
            {
                var span = FinishedAtUtc - StartedAtUtc;
                return span.TotalSeconds < 0 ? 0d : span.TotalSeconds;
            }
        }
    }

    /// <summary>One row of the per-module status grid shown by the UI and the report.</summary>
    public sealed class ModuleStatusView
    {
        public string ModuleId { get; set; }

        public string Title { get; set; }

        public DiagnosticStatus Status { get; set; }

        /// <summary>Number of checks the module reported.</summary>
        public int CheckCount { get; set; }

        /// <summary>True when the module ran and produced at least one check.</summary>
        public bool Executed { get; set; }

        public ModuleStatusView()
        {
            ModuleId = string.Empty;
            Title = string.Empty;
        }
    }
}
