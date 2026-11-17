using RiskPilot.Diagnostics.Nt8.Core.Instruments;

namespace RiskPilot.Diagnostics.Nt8.Core.Run
{
    /// <summary>
    /// The operator-facing target-instrument selection surface the Diagnostic Center drives. It
    /// exposes the observable <see cref="TargetInstrumentSelection"/> model, a refresh that reloads
    /// the available list from the live host surface without changing the current selection, and an
    /// explicit select that records the operator's choice. It is host-free so the window is a thin
    /// view and the selection state management is unit-testable offline; the real AddOn implements
    /// it over the NinjaTrader discovery surface.
    /// <para>
    /// Every method is read-only with respect to the host: refresh enumerates the loaded instrument
    /// names, select only records a choice, and neither ever mutates a NinjaTrader selection.
    /// </para>
    /// </summary>
    public interface IInstrumentSelectionController
    {
        /// <summary>The observable selection model (never null).</summary>
        TargetInstrumentSelection Selection { get; }

        /// <summary>
        /// Reloads the available-instrument list from the live host surface and returns the current
        /// selection's resolution. The current selection is never changed by a refresh.
        /// </summary>
        InstrumentSelectionResult RefreshAvailable();

        /// <summary>
        /// Records the operator's explicit selection (an exact instrument FullName, or null to
        /// clear). Never selects implicitly and never mutates the host selection.
        /// </summary>
        void Select(string fullName);
    }
}
