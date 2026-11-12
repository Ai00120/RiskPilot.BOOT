using System;

namespace RiskPilot.Diagnostics.Nt8.Core.Result
{
    /// <summary>
    /// One sanitized piece of supporting evidence attached to a result. Evidence is
    /// always redacted before it reaches this type, and <see cref="IsObservable"/>
    /// distinguishes "value hidden" from "value unavailable".
    /// </summary>
    public sealed class DiagnosticEvidence
    {
        public string Source { get; set; }

        public string ObservedValue { get; set; }

        public bool IsObservable { get; set; }

        public DateTime ObservedAtUtc { get; set; }

        public DiagnosticEvidence()
        {
            Source = string.Empty;
            ObservedValue = string.Empty;
            IsObservable = true;
        }

        public static DiagnosticEvidence Observable(string source, string value, DateTime observedAtUtc)
        {
            return new DiagnosticEvidence
            {
                Source = source,
                ObservedValue = value,
                IsObservable = true,
                ObservedAtUtc = observedAtUtc
            };
        }

        public static DiagnosticEvidence Unobservable(string source, string reason, DateTime observedAtUtc)
        {
            return new DiagnosticEvidence
            {
                Source = source,
                ObservedValue = reason,
                IsObservable = false,
                ObservedAtUtc = observedAtUtc
            };
        }
    }
}
