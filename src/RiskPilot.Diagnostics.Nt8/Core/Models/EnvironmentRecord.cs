namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>
    /// Runtime environment observed by the tool. This never carries the historical
    /// recorded baseline; it reflects only what the current host exposes.
    /// </summary>
    public sealed class EnvironmentRecord : NormalizedRecord
    {
        public EnvironmentClass Environment { get; set; }

        /// <summary>NinjaTrader host version if a documented API exposes it, else null.</summary>
        public string HostVersion { get; set; }

        /// <summary>Whether the host version could be observed through a public API.</summary>
        public bool HostVersionObservable { get; set; }

        /// <summary>Build mode of this diagnostic assembly (stub offline vs real host).</summary>
        public string ToolBuildMode { get; set; }

        /// <summary>CLR / target framework the tool was compiled for.</summary>
        public string TargetFramework { get; set; }

        /// <summary>Machine timezone offset from UTC, minutes, for correlation only.</summary>
        public int UtcOffsetMinutes { get; set; }

        public EnvironmentRecord()
        {
            HostVersion = string.Empty;
            ToolBuildMode = string.Empty;
            TargetFramework = string.Empty;
        }
    }
}
