using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// One forbidden-pattern or invariant hit found by the security scan. It carries only the
    /// stable rule id, its category and the repo-relative file/line — never a code fragment, so a
    /// hit can never leak source into a finding or an artifact.
    /// </summary>
    public sealed class SecurityScanFinding
    {
        public string RuleId { get; set; }

        public string Category { get; set; }

        /// <summary>Repo-relative path of the offending production source file.</summary>
        public string File { get; set; }

        /// <summary>1-based line number of the hit, or 0 when it could not be located.</summary>
        public int Line { get; set; }

        public SecurityScanFinding()
        {
            RuleId = string.Empty;
            Category = string.Empty;
            File = string.Empty;
        }
    }

    /// <summary>
    /// Observation of the read-only security scan, owned and executed by M8. It records what the
    /// scan actually observed so the module can decide PASS / FAIL / INCONCLUSIVE without ever
    /// turning unavailable security information into a PASS.
    /// <para>
    /// The scan is read-only: it reads production source text and never writes, and it never
    /// touches, mutates or reloads any NinjaTrader configuration or host state.
    /// </para>
    /// </summary>
    public sealed class SecurityScanObservation
    {
        /// <summary>True only when the scan actually ran over a readable source set.</summary>
        public bool Executed { get; set; }

        /// <summary>True when the read-only source was readable and at least one file was scanned.</summary>
        public bool SourceAvailable { get; set; }

        /// <summary>
        /// True only when the whole permitted source set was covered: the source reported itself
        /// complete and every file in the set was read. A partial or incomplete scan is never
        /// reported as a clean PASS.
        /// </summary>
        public bool SourceComplete { get; set; }

        /// <summary>Why the scan could not run, when <see cref="Executed"/> is false.</summary>
        public string UnavailableReason { get; set; }

        /// <summary>Number of production source files scanned.</summary>
        public int FilesScanned { get; set; }

        /// <summary>Number of forbidden-pattern rules applied.</summary>
        public int RuleCount { get; set; }

        /// <summary>True when the AddOn's trading-disabled declaration was observable.</summary>
        public bool TradingDisabledObservable { get; set; }

        /// <summary>True when the AddOn declared the trading-disabled invariant.</summary>
        public bool TradingDisabledDeclared { get; set; }

        /// <summary>True when the reviewed identity-definition file was present.</summary>
        public bool IdentityDefinitionPresent { get; set; }

        /// <summary>True when the identity literals were confined to the definition file.</summary>
        public bool IdentityDefinitionReviewed { get; set; }

        /// <summary>Total forbidden-pattern / invariant hits.</summary>
        public int HitCount { get { return Hits == null ? 0 : Hits.Count; } }

        /// <summary>Every hit found. Empty on a clean scan.</summary>
        public IList<SecurityScanFinding> Hits { get; set; }

        /// <summary>Stable reason codes summarising the scan (never a raw code fragment).</summary>
        public IList<string> ReasonCodes { get; set; }

        public SecurityScanObservation()
        {
            UnavailableReason = string.Empty;
            Hits = new List<SecurityScanFinding>();
            ReasonCodes = new List<string>();
        }
    }
}
