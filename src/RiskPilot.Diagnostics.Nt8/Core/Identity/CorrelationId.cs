using System;
using System.Text;

namespace RiskPilot.Diagnostics.Nt8.Core.Identity
{
    /// <summary>
    /// Generates and normalizes short, non-reversible correlation identifiers used to
    /// tie log entries, findings and results together within a single diagnostic pass.
    /// </summary>
    public static class CorrelationId
    {
        public const int Length = 12;

        public static string New()
        {
            return Guid.NewGuid().ToString("N").Substring(0, Length);
        }

        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "none";
            }

            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '-' || c == '_')
                {
                    builder.Append(c);
                }
            }

            var cleaned = builder.ToString();
            return cleaned.Length == 0 ? "none" : cleaned;
        }
    }
}
