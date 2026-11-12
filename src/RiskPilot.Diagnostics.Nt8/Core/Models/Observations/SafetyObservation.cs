using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// A single piece of runtime safety evidence. Each item states what was actually
    /// observed at runtime, so acceptance is never granted on a configuration value alone.
    /// </summary>
    public sealed class SafetyEvidenceItem
    {
        public string Name { get; set; }

        /// <summary>True only when the item was positively observed at runtime.</summary>
        public bool Observed { get; set; }

        /// <summary>Description of what was observed (or why it could not be).</summary>
        public string Detail { get; set; }

        /// <summary>Source of the evidence.</summary>
        public string Source { get; set; }

        public SafetyEvidenceItem()
        {
            Name = string.Empty;
            Detail = string.Empty;
            Source = string.Empty;
        }

        public SafetyEvidenceItem(string name, bool observed, string detail, string source)
        {
            Name = name ?? string.Empty;
            Observed = observed;
            Detail = detail ?? string.Empty;
            Source = source ?? string.Empty;
        }
    }

    /// <summary>
    /// M8 safety / acceptance observation. Holds the runtime evidence the module uses to
    /// decide whether read-only acceptance holds. A configuration value such as
    /// <c>Trading=Disabled</c> is not sufficient on its own; the runtime evidence below is
    /// what the module reports.
    /// </summary>
    public sealed class SafetyObservation
    {
        /// <summary>Runtime evidence that trading is disabled in the running tool.</summary>
        public bool TradingDisabledRuntimeEvidence { get; set; }

        /// <summary>
        /// Runtime evidence that no order-submission API is reachable from the compiled
        /// tool. The static scan proves this for the source; this flag records whether the
        /// tool could also confirm the posture at runtime.
        /// </summary>
        public bool ReadOnlyPostureObserved { get; set; }

        /// <summary>Number of forbidden order/account APIs found by the static scan, when supplied.</summary>
        public int StaticScanForbiddenApiCount { get; set; }

        /// <summary>Number of production source files covered by the static scan, when supplied.</summary>
        public int StaticScanFileCount { get; set; }

        /// <summary>True when the static scan was executed and passed.</summary>
        public bool StaticScanPassed { get; set; }

        /// <summary>True when the report artifacts were confirmed secret-free.</summary>
        public bool ReportSecretFree { get; set; }

        /// <summary>Items that could not be observed at runtime and remain unverifiable.</summary>
        public IList<string> UnobservableItems { get; set; }

        /// <summary>Individual evidence items.</summary>
        public IList<SafetyEvidenceItem> Evidence { get; set; }

        public SafetyObservation()
        {
            UnobservableItems = new List<string>();
            Evidence = new List<SafetyEvidenceItem>();
        }
    }
}
