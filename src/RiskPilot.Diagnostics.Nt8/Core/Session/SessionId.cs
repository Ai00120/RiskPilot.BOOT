using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RiskPilot.Diagnostics.Nt8.Core.Session
{
    /// <summary>
    /// Standard session identifier used across logs, records, findings and the report
    /// manifest. Format: <c>diag_yyyyMMdd_HHmmss_&lt;32 hex&gt;</c> in UTC, mirroring the
    /// recorder session convention observed in the evidence baseline.
    /// </summary>
    public static class SessionId
    {
        private const string Prefix = "diag";

        private static readonly Regex Pattern =
            new Regex(@"^diag_\d{8}_\d{6}_[0-9a-f]{32}$", RegexOptions.Compiled);

        public static string New(DateTime utcNow)
        {
            var stamp = utcNow.ToUniversalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var suffix = Guid.NewGuid().ToString("N");
            return Prefix + "_" + stamp + "_" + suffix;
        }

        public static bool IsValid(string sessionId)
        {
            return !string.IsNullOrEmpty(sessionId) && Pattern.IsMatch(sessionId);
        }
    }
}
