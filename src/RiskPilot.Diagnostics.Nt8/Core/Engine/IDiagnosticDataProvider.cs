using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Models.Observations;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// Read-only data source consumed by diagnostic probes. The Host layer implements
    /// this against public NinjaTrader APIs; tests implement it against fakes. Probes
    /// never touch NinjaTrader types directly, which keeps the core host-free and
    /// testable.
    /// </summary>
    public interface IDiagnosticDataProvider
    {
        /// <summary>Current runtime environment snapshot.</summary>
        EnvironmentRecord GetEnvironment();

        /// <summary>Read-only snapshot of every known connection.</summary>
        IReadOnlyList<ConnectionRecord> GetConnections();

        /// <summary>Provider identity for the active connection, if any.</summary>
        ProviderRecord GetProvider();

        /// <summary>Read-only snapshot of accounts (privacy-preserving).</summary>
        IReadOnlyList<AccountRecord> GetAccounts();

        /// <summary>Resolves a read-only instrument snapshot by id, or null when absent.</summary>
        InstrumentRecord GetInstrument(string instrumentId);

        /// <summary>Names of the public APIs the host verified as present.</summary>
        IReadOnlyList<string> GetPresentApis();

        /// <summary>Names of the required public APIs the host found missing.</summary>
        IReadOnlyList<string> GetMissingApis();

        /// <summary>
        /// Complete read-only observation set for the M1–M8 modules. A member of the
        /// returned snapshot may be null when the host could not provide it; the modules
        /// treat a null member as INCONCLUSIVE, never PASS. Implementations must be
        /// read-only and must never throw for a normal "not available" condition.
        /// </summary>
        DiagnosticSnapshot GetSnapshot();

        /// <summary>
        /// Read-only source the M8 security scan reads production source through. It resolves the
        /// source file list and text but never writes and never touches host configuration. A
        /// provider that cannot supply one returns null (or an unavailable source), which M8
        /// reports as INCONCLUSIVE, never PASS.
        /// </summary>
        Core.Contract.ISecurityScanSource GetSecurityScanSource();
    }
}
