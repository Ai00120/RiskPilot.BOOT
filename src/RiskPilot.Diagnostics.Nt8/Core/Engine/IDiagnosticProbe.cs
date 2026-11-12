using System.Collections.Generic;
using RiskPilot.Diagnostics.Nt8.Core.Result;

namespace RiskPilot.Diagnostics.Nt8.Core.Engine
{
    /// <summary>
    /// A single diagnostic module. A probe is pure with respect to the host: it reads the
    /// read-only <see cref="ProbeContext"/> and returns results; it never mutates host
    /// state and never calls an order, execution or account-modifying API.
    /// </summary>
    public interface IDiagnosticProbe
    {
        /// <summary>Stable module id (for example "M1").</summary>
        string ModuleId { get; }

        /// <summary>Human-readable module title.</summary>
        string Title { get; }

        /// <summary>Evaluates the module and returns one result per check.</summary>
        IReadOnlyList<DiagnosticResult> Evaluate(ProbeContext context);
    }
}
