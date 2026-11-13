using System.Collections.Generic;

namespace RiskPilot.Diagnostics.Nt8.Core.Models.Observations
{
    /// <summary>
    /// Read-only observation of the NinjaTrader AddOn/host lifecycle: the ordered <c>State</c>
    /// transitions the host reported through <c>OnStateChange</c>. The host layer records the raw
    /// transition names only; the host-free
    /// <see cref="Core.Identity.LifecycleVerifier"/> classifies them into phases
    /// (initialization / startup / connection / runtime / shutdown) and decides whether the
    /// observed sequence is valid. No verdict logic lives in the host layer.
    /// <para>
    /// This is the single M1 lifecycle surface. It replaces the previous untyped string list that
    /// was never populated by the host, so M1 could not actually see the lifecycle at all. A null
    /// or unobservable surface is reported INCONCLUSIVE, never PASS.
    /// </para>
    /// </summary>
    public sealed class LifecycleObservation
    {
        /// <summary>
        /// True when the host exposed the AddOn lifecycle surface at all. When false the
        /// lifecycle could not be observed and every lifecycle check stays INCONCLUSIVE.
        /// </summary>
        public bool LifecycleSurfaceObservable { get; set; }

        /// <summary>
        /// True when the observation originates from a compile-time stub or a test double rather
        /// than the real host. A stub/mock value can never establish the real lifecycle.
        /// </summary>
        public bool IsStubOrMockValue { get; set; }

        /// <summary>
        /// The observed <c>State</c> transitions in the order the host reported them. Each entry
        /// is the raw <c>State.ToString()</c> member name; an unrecognised member is reported as an
        /// unknown state (never guessed into a phase).
        /// </summary>
        public IList<string> ObservedStates { get; set; }

        public LifecycleObservation()
        {
            ObservedStates = new List<string>();
        }
    }
}
