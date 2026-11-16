using System;
using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Identity;
using RiskPilot.Diagnostics.Nt8.Host;

namespace RiskPilot.Diagnostics.Nt8.Tests
{
    /// <summary>
    /// Offline test scaffolding for the host data provider.
    /// <para>
    /// The real NinjaTrader <c>Provider</c> enum has no dedicated <c>Rithmic</c> member, so the
    /// host reports the Rithmic adapter as a generic placeholder slot (<c>Provider14</c>) that
    /// carries no vendor identity. The fixtures therefore set <c>Provider.Provider14</c> and supply
    /// the owner-authorized slot mapping below, which is the ONLY way a placeholder slot may be
    /// accepted. This mirrors how production would resolve the slot once runtime evidence records
    /// the mapping; the production default stays empty (fail closed).
    /// </para>
    /// </summary>
    internal static class TestHost
    {
        /// <summary>Owner-authorized mapping resolving the observed placeholder slot to Rithmic.</summary>
        internal static readonly IReadOnlyDictionary<string, string> RithmicSlotMapping =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Provider14", "Rithmic" }
            };

        /// <summary>
        /// Creates a host provider with the verified slot mapping, matching the common test
        /// construction (readiness probe plus optional requested instrument id).
        /// </summary>
        internal static Nt8HostDataProvider Provider(bool masterMetadataReady, string requestedInstrumentId = null)
        {
            return new Nt8HostDataProvider(
                () => masterMetadataReady,
                null,
                null,
                RithmicSlotMapping,
                requestedInstrumentId);
        }

        /// <summary>
        /// Creates a host provider with the verified slot mapping and a live readiness probe, so a
        /// test can flip readiness after construction (the AddOn-constructed-at-Active case).
        /// </summary>
        internal static Nt8HostDataProvider Provider(System.Func<bool> masterMetadataReady, string requestedInstrumentId = null)
        {
            return new Nt8HostDataProvider(
                masterMetadataReady,
                null,
                null,
                RithmicSlotMapping,
                requestedInstrumentId);
        }
    }
}
