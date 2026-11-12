using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Result;
using RiskPilot.Diagnostics.Nt8.Core.Session;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Report
{
    /// <summary>
    /// Complete, serializable diagnostic report for one session. The aggregate status is
    /// derived from the individual results with fail-closed precedence; it is never set
    /// directly by a module.
    /// </summary>
    public sealed class DiagnosticReport
    {
        public string SchemaVersion { get; set; }

        public string ToolVersion { get; set; }

        public string BuildMode { get; set; }

        public string SessionId { get; set; }

        public string CorrelationId { get; set; }

        public DateTime GeneratedAtUtc { get; set; }

        public DateTime SessionStartedAtUtc { get; set; }

        public string EnvironmentClass { get; set; }

        public bool TradingDisabled { get; set; }

        public string SourceTimeBasis { get; set; }

        public IList<DiagnosticResult> Results { get; private set; }

        public IList<DiagnosticFinding> Findings { get; private set; }

        public SessionCounters Counters { get; set; }

        public IList<string> Unobservable { get; private set; }

        public DiagnosticReport()
        {
            SchemaVersion = BuildIdentity.SchemaVersion;
            ToolVersion = BuildIdentity.ToolVersion;
            BuildMode = BuildIdentity.BuildMode;
            SessionId = string.Empty;
            CorrelationId = string.Empty;
            EnvironmentClass = "UNKNOWN";
            TradingDisabled = true;
            SourceTimeBasis = Core.Models.SourceTimeBasis.ServerTimeUnverified.ToWire();
            Results = new List<DiagnosticResult>();
            Findings = new List<DiagnosticFinding>();
            Unobservable = new List<string>();
            Counters = new SessionCounters();
        }

        public DiagnosticReport Add(DiagnosticResult result)
        {
            if (result != null)
            {
                Results.Add(result);
            }

            return this;
        }

        public DiagnosticReport AddFinding(DiagnosticFinding finding)
        {
            if (finding != null)
            {
                Findings.Add(finding);
            }

            return this;
        }

        public DiagnosticReport AddUnobservable(string item)
        {
            if (!string.IsNullOrEmpty(item))
            {
                Unobservable.Add(item);
            }

            return this;
        }

        /// <summary>
        /// Aggregate status across all results using fail-closed precedence. An empty
        /// report is <see cref="DiagnosticStatus.NOT_APPLICABLE"/>.
        /// </summary>
        public DiagnosticStatus AggregateStatus()
        {
            if (Results.Count == 0)
            {
                return DiagnosticStatus.NOT_APPLICABLE;
            }

            var status = DiagnosticStatus.NOT_APPLICABLE;
            foreach (var result in Results)
            {
                status = status.Worst(result.Status);
            }

            return status;
        }
    }

    /// <summary>Immutable snapshot of the session counters embedded in a report.</summary>
    public sealed class SessionCounters
    {
        public long Enqueued { get; set; }

        public long Processed { get; set; }

        public long Dropped { get; set; }

        public long Duplicates { get; set; }

        public long SequenceGaps { get; set; }

        public long TimestampInvalid { get; set; }

        public static SessionCounters From(SessionContext session)
        {
            if (session == null)
            {
                return new SessionCounters();
            }

            return new SessionCounters
            {
                Enqueued = session.EnqueuedCount,
                Processed = session.ProcessedCount,
                Dropped = session.DroppedCount,
                Duplicates = session.DuplicateCount,
                SequenceGaps = session.SequenceGapCount,
                TimestampInvalid = session.TimestampInvalidCount
            };
        }
    }
}
