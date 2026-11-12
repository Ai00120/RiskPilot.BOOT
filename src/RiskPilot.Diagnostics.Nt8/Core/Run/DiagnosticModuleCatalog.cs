using System;
using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// Human-readable titles for each diagnostic module. Kept here so the UI and the evidence
    /// package can label a module without reaching into the engine.
    /// </summary>
    public static class DiagnosticModuleCatalog
    {
        private static readonly Dictionary<string, string> Titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "M1", "Environment & API Compatibility" },
            { "M2", "Connection & Provider Identity" },
            { "M3", "Account & Order Lifecycle (F-05)" },
            { "M4", "Instrument Attestation / D-078" },
            { "M5", "DOM & Market Data" },
            { "M6", "TradeDelta / OrderFlow" },
            { "M7", "Strategies / Risk Gate" },
            { "M8", "Safety / Acceptance" },
            { "M9", "Post-Trade Audit" }
        };

        public static string TitleFor(string moduleId)
        {
            if (!string.IsNullOrEmpty(moduleId) && Titles.TryGetValue(moduleId, out var title))
            {
                return title;
            }

            return moduleId ?? string.Empty;
        }
    }
}
