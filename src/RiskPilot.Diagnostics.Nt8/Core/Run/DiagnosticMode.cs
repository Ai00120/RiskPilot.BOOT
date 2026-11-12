using System;
using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// The three user-facing diagnostic modes of the Diagnostic Center. Each mode selects a
    /// deterministic set of modules; the selection is fixed here so the UI, the engine and
    /// the evidence package all agree on what a mode means.
    /// </summary>
    public enum DiagnosticMode
    {
        /// <summary>Quick checks: environment, provider/connection, account, instrument, market data, safety.</summary>
        Quick = 0,

        /// <summary>All eight modules M1–M8.</summary>
        Full = 1,

        /// <summary>Read-only post-trade audit of already-recorded order/execution/position events.</summary>
        PostTradeAudit = 2
    }

    /// <summary>
    /// Static description of a diagnostic mode: its id, title, description and the exact set
    /// of module ids it runs. A mode never selects a module that mutates state; the module
    /// catalog below only ever lists read-only diagnostic modules.
    /// </summary>
    public sealed class DiagnosticModeDefinition
    {
        public DiagnosticMode Mode { get; private set; }

        public string Id { get; private set; }

        public string Title { get; private set; }

        public string Description { get; private set; }

        /// <summary>Module ids this mode runs, in execution order.</summary>
        public IReadOnlyList<string> ModuleIds { get; private set; }

        public DiagnosticModeDefinition(DiagnosticMode mode, string id, string title, string description, IReadOnlyList<string> moduleIds)
        {
            Mode = mode;
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            ModuleIds = moduleIds ?? new string[0];
        }
    }

    /// <summary>
    /// The single source of truth for what each mode runs. The UI labels, the engine filter
    /// and the acceptance matrix are all derived from this catalog.
    /// </summary>
    public static class DiagnosticModeCatalog
    {
        public static readonly DiagnosticModeDefinition Quick = new DiagnosticModeDefinition(
            DiagnosticMode.Quick,
            "quick",
            "Quick Diagnostic",
            "Fast read-only checks: environment, provider/connection, account visibility, instrument, market-data readiness and safety status.",
            new[]
            {
                "M1", // Environment / API
                "M2", // Provider / Connection
                "M3", // Account visibility
                "M4", // Instrument
                "M5", // Market data readiness
                "M8"  // Safety status
            });

        public static readonly DiagnosticModeDefinition Full = new DiagnosticModeDefinition(
            DiagnosticMode.Full,
            "full",
            "Full Diagnostic",
            "Runs all eight modules: environment/API, connection/provider, account/order, instrument/D-078, DOM/market data, TradeDelta/OrderFlow, strategies/risk gate and safety/acceptance.",
            new[] { "M1", "M2", "M3", "M4", "M5", "M6", "M7", "M8" });

        public static readonly DiagnosticModeDefinition PostTradeAudit = new DiagnosticModeDefinition(
            DiagnosticMode.PostTradeAudit,
            "post-trade-audit",
            "Post-Trade Audit",
            "Read-only analysis of already-recorded events only: order lifecycle, execution lifecycle, position updates, Account.Orders snapshots, order-id classification, correlation/timing and missing or duplicated events. It never creates or changes a trade.",
            new[] { "M9" });

        public static IReadOnlyList<DiagnosticModeDefinition> All
        {
            get { return new[] { Quick, Full, PostTradeAudit }; }
        }

        public static DiagnosticModeDefinition For(DiagnosticMode mode)
        {
            switch (mode)
            {
                case DiagnosticMode.Quick:
                    return Quick;
                case DiagnosticMode.PostTradeAudit:
                    return PostTradeAudit;
                default:
                    return Full;
            }
        }
    }
}
