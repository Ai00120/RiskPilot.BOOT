using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;

namespace RiskPilot.Diagnostics.Nt8.Core.Evidence
{
    /// <summary>
    /// Raw, read-only evidence gathered during one diagnostic run. The host fills it from the
    /// public NinjaTrader API and the ingest pipeline; the evidence builder only reads it.
    /// Every member is optional: a member that the host could not observe stays null/empty and
    /// is reported as missing-with-reason rather than substituted.
    /// </summary>
    public sealed class EvidenceDataset
    {
        public EnvironmentRecord Environment { get; set; }

        public IList<ConnectionRecord> Connections { get; set; }

        public ProviderRecord Provider { get; set; }

        public IList<AccountRecord> Accounts { get; set; }

        public IList<InstrumentRecord> Instruments { get; set; }

        public IList<OrderRecord> Orders { get; set; }

        public IList<ExecutionRecord> Executions { get; set; }

        public IList<PositionRecord> Positions { get; set; }

        public IList<MarketDataRecord> MarketData { get; set; }

        public IList<DomRecord> Dom { get; set; }

        public IList<StrategyRecord> Strategies { get; set; }

        /// <summary>Ordered lifecycle events for the post-trade audit (M9).</summary>
        public IList<OrderLifecycleEvent> OrderLifecycleEvents { get; set; }

        /// <summary>The point-in-time observation snapshot for M1–M8.</summary>
        public DiagnosticSnapshot Snapshot { get; set; }

        /// <summary>Dropped event count observed by the ingest pipeline.</summary>
        public long DroppedCount { get; set; }

        /// <summary>Duplicate event count observed by the ingest pipeline.</summary>
        public long DuplicateCount { get; set; }

        /// <summary>Sequence gap count observed by the ingest pipeline.</summary>
        public long SequenceGapCount { get; set; }

        /// <summary>Reason the run stopped early, when it was truncated rather than completed.</summary>
        public string TruncationReason { get; set; }

        /// <summary>
        /// Provenance of this dataset. <see cref="EvidenceProvenance.Runtime"/> for a real
        /// in-host run (the default), <see cref="EvidenceProvenance.Sample"/> for synthetic
        /// documentation data. It is carried into the manifest so a sample package can never be
        /// mistaken for a live runtime result.
        /// </summary>
        public string Provenance { get; set; }

        public EvidenceDataset()
        {
            Connections = new List<ConnectionRecord>();
            Accounts = new List<AccountRecord>();
            Instruments = new List<InstrumentRecord>();
            Orders = new List<OrderRecord>();
            Executions = new List<ExecutionRecord>();
            Positions = new List<PositionRecord>();
            MarketData = new List<MarketDataRecord>();
            Dom = new List<DomRecord>();
            Strategies = new List<StrategyRecord>();
            OrderLifecycleEvents = new List<OrderLifecycleEvent>();
            TruncationReason = string.Empty;
            Provenance = EvidenceProvenance.Runtime;
        }
    }

    /// <summary>Canonical provenance values for an evidence dataset.</summary>
    public static class EvidenceProvenance
    {
        /// <summary>A real diagnostic run inside the NinjaTrader host.</summary>
        public const string Runtime = "RUNTIME";

        /// <summary>Synthetic data produced for documentation; never a runtime observation.</summary>
        public const string Sample = "SAMPLE";
    }
}
