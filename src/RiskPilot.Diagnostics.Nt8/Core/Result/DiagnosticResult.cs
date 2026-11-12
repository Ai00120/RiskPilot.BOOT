using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Result
{
    /// <summary>
    /// Outcome of a single diagnostic check, carrying status, severity, sanitized
    /// evidence and reason codes. This is the unit the report serializes per check.
    /// </summary>
    public sealed class DiagnosticResult
    {
        public string ModuleId { get; set; }

        public string CheckId { get; set; }

        public DiagnosticStatus Status { get; set; }

        public DiagnosticSeverity Severity { get; set; }

        public ErrorClass ErrorClass { get; set; }

        public IReadOnlyList<DiagnosticEvidence> Evidence { get; set; }

        public string[] ReasonCodes { get; set; }

        public string RemediationHint { get; set; }

        public string CorrelationId { get; set; }

        public DateTime EvaluatedAtUtc { get; set; }

        public DiagnosticResult()
        {
            ModuleId = string.Empty;
            CheckId = string.Empty;
            Evidence = new List<DiagnosticEvidence>();
            ReasonCodes = new string[0];
            RemediationHint = string.Empty;
            CorrelationId = string.Empty;
            ErrorClass = ErrorClass.None;
        }

        /// <summary>
        /// Builds a PASS result. Callers must have positively observed the condition;
        /// absent data must use <see cref="Inconclusive"/> instead.
        /// </summary>
        public static DiagnosticResult Pass(string moduleId, string checkId, string correlationId, DateTime atUtc)
        {
            return Create(moduleId, checkId, DiagnosticStatus.PASS, DiagnosticSeverity.Info, correlationId, atUtc);
        }

        public static DiagnosticResult Fail(string moduleId, string checkId, ErrorClass errorClass, string correlationId, DateTime atUtc)
        {
            var result = Create(moduleId, checkId, DiagnosticStatus.FAIL, DiagnosticSeverity.High, correlationId, atUtc);
            result.ErrorClass = errorClass;
            return result;
        }

        public static DiagnosticResult Inconclusive(string moduleId, string checkId, string correlationId, DateTime atUtc)
        {
            return Create(moduleId, checkId, DiagnosticStatus.INCONCLUSIVE, DiagnosticSeverity.Medium, correlationId, atUtc);
        }

        public static DiagnosticResult NotApplicable(string moduleId, string checkId, string correlationId, DateTime atUtc)
        {
            return Create(moduleId, checkId, DiagnosticStatus.NOT_APPLICABLE, DiagnosticSeverity.Info, correlationId, atUtc);
        }

        private static DiagnosticResult Create(
            string moduleId,
            string checkId,
            DiagnosticStatus status,
            DiagnosticSeverity severity,
            string correlationId,
            DateTime atUtc)
        {
            return new DiagnosticResult
            {
                ModuleId = moduleId ?? string.Empty,
                CheckId = checkId ?? string.Empty,
                Status = status,
                Severity = severity,
                CorrelationId = correlationId ?? string.Empty,
                EvaluatedAtUtc = atUtc
            };
        }

        public DiagnosticResult WithReasonCode(params string[] codes)
        {
            ReasonCodes = codes ?? new string[0];
            return this;
        }

        public DiagnosticResult WithEvidence(params DiagnosticEvidence[] evidence)
        {
            var list = new List<DiagnosticEvidence>();
            if (evidence != null)
            {
                list.AddRange(evidence);
            }

            Evidence = list;
            return this;
        }

        public DiagnosticResult WithRemediation(string hint)
        {
            RemediationHint = hint ?? string.Empty;
            return this;
        }
    }
}
