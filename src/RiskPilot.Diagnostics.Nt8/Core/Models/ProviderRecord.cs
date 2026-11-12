namespace RiskPilot.Diagnostics.Nt8.Core.Models
{
    /// <summary>
    /// Normalized provider identity. NinjaTrader exposes a provider *name* but no public
    /// API for the exact provider build/version, so that field is explicitly unobservable.
    /// </summary>
    public sealed class ProviderRecord : NormalizedRecord
    {
        /// <summary>Provider name (e.g. "Rithmic").</summary>
        public string Name { get; set; }

        /// <summary>How the name was resolved: "String" or "Enum".</summary>
        public string ResolutionMethod { get; set; }

        /// <summary>True when the provider is the expected evidence provider (Rithmic).</summary>
        public bool IsExpectedEvidenceProvider { get; set; }

        /// <summary>Always false until a documented API exposes the provider build.</summary>
        public bool ProviderBuildObservable { get; set; }

        /// <summary>Reason the provider build cannot be observed.</summary>
        public string UnobservableReason { get; set; }

        public ProviderRecord()
        {
            Name = string.Empty;
            ResolutionMethod = string.Empty;
            UnobservableReason = "NO_PUBLIC_API_FOR_PROVIDER_BUILD";
        }
    }
}
