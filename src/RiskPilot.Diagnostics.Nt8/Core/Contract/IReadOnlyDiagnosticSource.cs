using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Models;
using RiskPilot.Diagnostics.Nt8.Core.Result;

namespace RiskPilot.Diagnostics.Nt8.Core.Contract
{
    /// <summary>
    /// Optional, read-only diagnostic contract. When RiskPilot.BOT chooses to expose
    /// diagnostics (telemetry or a provider), it can implement this interface so the
    /// tool consumes normalized results without any dependency on BOT internals. The
    /// contract is strictly read-only: it returns data and cannot mutate host state.
    /// </summary>
    public interface IReadOnlyDiagnosticSource
    {
        /// <summary>Stable identifier of the source (for example "RiskPilot.BOT").</summary>
        string SourceId { get; }

        /// <summary>Read-only snapshots this source can provide.</summary>
        IReadOnlyList<DiagnosticResult> ReadResults();

        /// <summary>Read-only strategy observations, or an empty list when none exist.</summary>
        IReadOnlyList<StrategyRecord> ReadStrategies();
    }
}
