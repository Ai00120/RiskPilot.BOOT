using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>
    /// A single diagnostic observation with its classification. Findings are the
    /// normalized unit that a module emits and that the report serializes.
    /// </summary>
    public sealed class DiagnosticFinding
    {
        public string CheckId { get; set; }

        public string ModuleId { get; set; }

        public DiagnosticStatus Status { get; set; }

        public DiagnosticSeverity Severity { get; set; }

        public string Message { get; set; }

        /// <summary>Machine-readable reason codes, sanitized.</summary>
        public string[] ReasonCodes { get; set; }

        /// <summary>Error classification when the finding describes a failure.</summary>
        public ErrorClass ErrorClass { get; set; }

        public string CorrelationId { get; set; }

        public string SessionId { get; set; }

        public System.DateTime ObservedAtUtc { get; set; }

        public DiagnosticFinding()
        {
            CheckId = string.Empty;
            ModuleId = string.Empty;
            Message = string.Empty;
            ReasonCodes = new string[0];
            CorrelationId = string.Empty;
            SessionId = string.Empty;
            ErrorClass = ErrorClass.None;
        }
    }
}
