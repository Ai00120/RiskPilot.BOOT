using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Evidence
{
    /// <summary>
    /// The canonical list of files in an evidence package. This is the single source of truth
    /// for the package layout: the builder writes exactly these names and the manifest lists
    /// exactly these names, so a package can never silently omit an artifact.
    /// </summary>
    public static class EvidencePackageCatalog
    {
        public const string SummaryHtml = "summary.html";
        public const string FullReportHtml = "full-report.html";
        public const string AcceptanceMatrixCsv = "acceptance-matrix.csv";
        public const string EnvironmentJson = "environment.json";
        public const string ConnectionsJson = "connections.json";
        public const string AccountsJson = "accounts.json";
        public const string InstrumentsJson = "instruments.json";
        public const string OrdersCsv = "orders.csv";
        public const string ExecutionsCsv = "executions.csv";
        public const string PositionsCsv = "positions.csv";
        public const string MarketDataSummaryJson = "market-data-summary.json";
        public const string DomSummaryJson = "dom-summary.json";
        public const string OrderFlowSummaryJson = "orderflow-summary.json";
        public const string StrategiesJson = "strategies.json";
        public const string SafetyAuditJson = "safety-audit.json";
        public const string Stage34CoverageJson = "stage34-coverage.json";
        public const string FindingsJson = "findings.json";
        public const string ManifestJson = "manifest.json";

        /// <summary>The 18 artifacts, in the order they are listed in the manifest.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            SummaryHtml,
            FullReportHtml,
            AcceptanceMatrixCsv,
            EnvironmentJson,
            ConnectionsJson,
            AccountsJson,
            InstrumentsJson,
            OrdersCsv,
            ExecutionsCsv,
            PositionsCsv,
            MarketDataSummaryJson,
            DomSummaryJson,
            OrderFlowSummaryJson,
            StrategiesJson,
            SafetyAuditJson,
            Stage34CoverageJson,
            FindingsJson,
            ManifestJson
        };
    }
}
