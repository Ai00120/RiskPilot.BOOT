using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Status;

namespace RiskPilot.Diagnostics.Nt8.Core.Result
{
    /// <summary>
    /// Outcome of a single diagnostic check, carrying status, severity, sanitized
    /// evidence and reason codes. This is the unit the report serializes per check.
    /// <para>
    /// Every check is also a *finding*: it carries a stable id, a human-readable
    /// observation, the evidence source, the observation method, the known limitation and
    /// a next action, so a reader can act on it without re-deriving the reasoning.
    /// </para>
    /// </summary>
    public sealed class DiagnosticResult
    {
        public string ModuleId { get; set; }

        public string CheckId { get; set; }

        /// <summary>Stable finding id (for example "F-05" or "M4.ATTESTATION").</summary>
        public string FindingId { get; set; }

        public DiagnosticStatus Status { get; set; }

        public DiagnosticSeverity Severity { get; set; }

        public ErrorClass ErrorClass { get; set; }

        /// <summary>Human-readable description of what was observed.</summary>
        public string Observation { get; set; }

        /// <summary>Source of the evidence ("NinjaTrader Public API", "Unobservable", ...).</summary>
        public string Source { get; set; }

        /// <summary>How the observation was made (public API read, counter snapshot, ...).</summary>
        public string Method { get; set; }

        /// <summary>Known limitation of this check, stated explicitly.</summary>
        public string Limitation { get; set; }

        /// <summary>Concrete next action for an operator. Never an automated mutation.</summary>
        public string NextAction { get; set; }

        public IReadOnlyList<DiagnosticEvidence> Evidence { get; set; }

        public string[] ReasonCodes { get; set; }

        /// <summary>Non-operational guidance retained for backward compatibility.</summary>
        public string RemediationHint { get; set; }

        public string CorrelationId { get; set; }

        public DateTime EvaluatedAtUtc { get; set; }

        public DiagnosticResult()
        {
            ModuleId = string.Empty;
            CheckId = string.Empty;
            FindingId = string.Empty;
            Observation = string.Empty;
            Source = string.Empty;
            Method = string.Empty;
            Limitation = string.Empty;
            NextAction = string.Empty;
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
                FindingId = checkId ?? string.Empty,
                Status = status,
                Severity = severity,
                CorrelationId = correlationId ?? string.Empty,
                EvaluatedAtUtc = atUtc
            };
        }

        public DiagnosticResult WithFindingId(string findingId)
        {
            FindingId = findingId ?? string.Empty;
            return this;
        }

        public DiagnosticResult WithObservation(string observation)
        {
            Observation = observation ?? string.Empty;
            return this;
        }

        public DiagnosticResult WithSource(string source)
        {
            Source = source ?? string.Empty;
            return this;
        }

        public DiagnosticResult WithMethod(string method)
        {
            Method = method ?? string.Empty;
            return this;
        }

        public DiagnosticResult WithLimitation(string limitation)
        {
            Limitation = limitation ?? string.Empty;
            return this;
        }

        public DiagnosticResult WithNextAction(string nextAction)
        {
            NextAction = nextAction ?? string.Empty;
            return this;
        }

        /// <summary>Sets severity explicitly, overriding the status default.</summary>
        public DiagnosticResult WithSeverity(DiagnosticSeverity severity)
        {
            Severity = severity;
            return this;
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
