using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// Whether a diagnostic capability could be observed on this host. Capabilities that
    /// cannot be read through a documented public API are <see cref="Unobservable"/> and
    /// are never reported as available.
    /// </summary>
    public enum CapabilityAvailability
    {
        Unobservable = 0,
        Available = 1,
        Unavailable = 2
    }

    /// <summary>One entry in the runtime capability matrix (M1).</summary>
    public sealed class RuntimeCapability
    {
        public string Name { get; set; }

        public CapabilityAvailability Availability { get; set; }

        /// <summary>Why the capability is in this state, or the observed value when available.</summary>
        public string Detail { get; set; }

        public RuntimeCapability()
        {
            Name = string.Empty;
            Detail = string.Empty;
        }

        public RuntimeCapability(string name, CapabilityAvailability availability, string detail)
        {
            Name = name ?? string.Empty;
            Availability = availability;
            Detail = detail ?? string.Empty;
        }
    }

    /// <summary>
    /// M1 runtime environment observation. Only values that a documented public API or the
    /// CLR actually exposes are populated; everything else is left null/unknown so the
    /// module reports INCONCLUSIVE rather than guessing.
    /// </summary>
    public sealed class RuntimeObservation
    {
        /// <summary>True when the process is 64-bit (from <c>Environment.Is64BitProcess</c>).</summary>
        public bool? Is64BitProcess { get; set; }

        /// <summary>True when the process is 32-bit, when that could be determined.</summary>
        public bool? Is32BitProcess { get; set; }

        /// <summary>CLR version string from <c>Environment.Version</c>, when readable.</summary>
        public string ClrVersion { get; set; }

        /// <summary>Operating system description from <c>Environment.OSVersion</c>, when readable.</summary>
        public string OperatingSystem { get; set; }

        /// <summary>
        /// Literal NinjaTrader host version, only if a documented public API exposes it.
        /// NinjaTrader exposes no such API, so this stays null and is never guessed from a
        /// file name or build id.
        /// </summary>
        public string HostVersion { get; set; }

        /// <summary>True only when <see cref="HostVersion"/> came from a public API.</summary>
        public bool HostVersionObservable { get; set; }

        /// <summary>AddOn lifecycle states observed in this session, in order.</summary>
        public IList<string> LifecycleStates { get; set; }

        /// <summary>Capability matrix entries (present/absent/unobservable APIs).</summary>
        public IList<RuntimeCapability> Capabilities { get; set; }

        public RuntimeObservation()
        {
            ClrVersion = string.Empty;
            OperatingSystem = string.Empty;
            HostVersion = string.Empty;
            LifecycleStates = new List<string>();
            Capabilities = new List<RuntimeCapability>();
        }
    }
}
