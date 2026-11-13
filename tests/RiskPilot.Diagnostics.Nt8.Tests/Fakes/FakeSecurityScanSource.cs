using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Contract;

namespace RiskPilot.Diagnostics.Nt8.Tests.Fakes
{
    /// <summary>
    /// In-memory read-only source for the M8 security scan. It substitutes only for the file
    /// system boundary; the scan logic under test is the real production
    /// <see cref="RiskPilot.Diagnostics.Nt8.Core.Security.SecurityScanner"/>. It never writes.
    /// </summary>
    public sealed class FakeSecurityScanSource : ISecurityScanSource
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();

        public FakeSecurityScanSource()
        {
            IsAvailable = true;
            IsComplete = true;
        }

        public bool IsAvailable { get; set; }

        /// <summary>
        /// Whether the in-memory set is fully covered. Defaults to true; a test sets it false to
        /// simulate a partially enumerated / skipped-file source set.
        /// </summary>
        public bool IsComplete { get; set; }

        public FakeSecurityScanSource Add(string relativePath, string text)
        {
            _files[relativePath] = text;
            if (!_order.Contains(relativePath))
            {
                _order.Add(relativePath);
            }

            return this;
        }

        /// <summary>
        /// A minimal, clean read-only source: a production AddOn declaring the trading-disabled
        /// invariant and the reviewed identity definition carrying the Owner identity literal.
        /// It lets a test exercise the real scanner to a PASS.
        /// </summary>
        public static FakeSecurityScanSource Clean()
        {
            return new FakeSecurityScanSource()
                .Add("Host/DiagnosticAddOn.cs",
                    "public sealed partial class DiagnosticAddOn { public const bool TradingDisabled = true; }")
                .Add("Core/Identity/AccountIdentityVerifier.cs",
                    "public static class ExpectedAccountIdentity { public const string AccountName = \""
                    + RiskPilot.Diagnostics.Nt8.Core.Identity.ExpectedAccountIdentity.AccountName + "\"; }");
        }

        public IReadOnlyList<string> GetSourceFiles()
        {
            return new List<string>(_order);
        }

        public string ReadFile(string relativePath)
        {
            return relativePath != null && _files.TryGetValue(relativePath, out var text) ? text : null;
        }

        public bool FileExists(string relativePath)
        {
            return relativePath != null && _files.ContainsKey(relativePath);
        }
    }
}
