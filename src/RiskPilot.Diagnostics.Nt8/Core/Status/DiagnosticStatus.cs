namespace RiskPilot.Diagnostics.Nt8.Core.Status
{
    /// <summary>
    /// Terminal outcome of a single diagnostic check. Missing or unverifiable data must
    /// never be reported as <see cref="PASS"/> (fail-closed).
    /// </summary>
    public enum DiagnosticStatus
    {
        PASS = 0,
        FAIL = 1,
        INCONCLUSIVE = 2,
        NOT_APPLICABLE = 3
    }

    /// <summary>Impact of a finding or check, independent of its pass/fail status.</summary>
    public enum DiagnosticSeverity
    {
        Info = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    public static class DiagnosticStatusExtensions
    {
        /// <summary>
        /// Aggregates several statuses into the single most severe outcome using a
        /// fail-closed precedence: FAIL &gt; INCONCLUSIVE &gt; PASS &gt; NOT_APPLICABLE.
        /// An empty set is <see cref="DiagnosticStatus.NOT_APPLICABLE"/>.
        /// </summary>
        public static DiagnosticStatus Worst(this DiagnosticStatus current, DiagnosticStatus other)
        {
            return Rank(current) >= Rank(other) ? current : other;
        }

        public static int Rank(this DiagnosticStatus status)
        {
            switch (status)
            {
                case DiagnosticStatus.FAIL:
                    return 3;
                case DiagnosticStatus.INCONCLUSIVE:
                    return 2;
                case DiagnosticStatus.PASS:
                    return 1;
                default:
                    return 0;
            }
        }

        public static string ToWire(this DiagnosticStatus status)
        {
            switch (status)
            {
                case DiagnosticStatus.PASS:
                    return "PASS";
                case DiagnosticStatus.FAIL:
                    return "FAIL";
                case DiagnosticStatus.INCONCLUSIVE:
                    return "INCONCLUSIVE";
                default:
                    return "NOT_APPLICABLE";
            }
        }

        public static string ToWire(this DiagnosticSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticSeverity.Critical:
                    return "CRITICAL";
                case DiagnosticSeverity.High:
                    return "HIGH";
                case DiagnosticSeverity.Medium:
                    return "MEDIUM";
                case DiagnosticSeverity.Low:
                    return "LOW";
                default:
                    return "INFO";
            }
        }

        public static bool TryParse(string value, out DiagnosticStatus status)
        {
            status = DiagnosticStatus.INCONCLUSIVE;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            switch (value.Trim().ToUpperInvariant())
            {
                case "PASS":
                    status = DiagnosticStatus.PASS;
                    return true;
                case "FAIL":
                    status = DiagnosticStatus.FAIL;
                    return true;
                case "INCONCLUSIVE":
                    status = DiagnosticStatus.INCONCLUSIVE;
                    return true;
                case "NOT_APPLICABLE":
                case "NOTAPPLICABLE":
                case "N/A":
                    status = DiagnosticStatus.NOT_APPLICABLE;
                    return true;
                default:
                    return false;
            }
        }
    }
}
